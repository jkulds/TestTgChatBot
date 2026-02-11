using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TestTgChatBot.Dao;
using TestTgChatBot.Dao.Models;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TestTgChatBot.Business.Services;

public class TelegramBackgroundService(
    ITelegramBotClient botClient,
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramBackgroundService> logger)
    : BackgroundService
{
    private readonly ConcurrentDictionary<long, UserTestSession> _sessions = new();
    private readonly ConcurrentDictionary<long, bool> _awaitingName = new();
    private readonly ConcurrentDictionary<long, int> _pendingStart = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatLocks = new();
    private const string StartCbPrefix = "start:";

    // Tg max rate 30req/s
    private readonly TokenBucketRateLimiter _tgLimiter =
        new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 28,
            TokensPerPeriod = 28,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = true,
            QueueLimit = 2000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

    private async Task WithChatLock(long chatId, Func<Task> action)
    {
        var sem = _chatLocks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            sem.Release();
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync,
            new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery] },
            cancellationToken: stoppingToken);

        _ = Task.Run(() => CoordinatorLoopAsync(stoppingToken), stoppingToken);
        return Task.CompletedTask;
    }

    private async Task CoordinatorLoopAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // 1) Стартуем ожидания, когда тест активирован
                var toStart = await db.UserTests
                    .Include(ut => ut.UserProfile)
                    .Include(ut => ut.Test)
                    .Where(ut => ut.Status == UserTestStatus.Pending
                                 && ut.Test.IsActive
                                 && !ut.Test.IsDeleted
                                 && ut.StartNotifiedAt == null)
                    .ToListAsync(stoppingToken);

                foreach (var ut in toStart)
                {
                    // если пользователь уже в другом тесте — пропускаем
                    if (_sessions.ContainsKey(ut.UserProfile.TelegramUserId)) continue;

                    await SendStartPromptAsync(
                        ut.Id,
                        ut.UserProfile.TelegramUserId,
                        ut.Test.Name,
                        CancellationToken.None
                    );
                }

                // 2) Завершаем только по ЯВНОМУ выключению
                var toFinish = await db.UserTests
                    .Include(ut => ut.UserProfile)
                    .Include(ut => ut.Test)
                    .Where(ut => ut.FinishedAt == null
                                 && ut.Test.EndTime != null // ← выключение было
                                 && (
                                     ut.Status == UserTestStatus.InProgress
                                     // Pending завершаем ТОЛЬКО если он был зарегистрирован до момента выключения
                                     || (ut.Status == UserTestStatus.Pending && ut.CreatedAt <= ut.Test.EndTime)
                                 )
                    )
                    .ToListAsync(stoppingToken);

                foreach (var ut in toFinish)
                {
                    var chatId = ut.UserProfile.TelegramUserId;

                    if (_sessions.TryGetValue(chatId, out var session) && session.TestId == ut.TestId)
                    {
                        session.Cts?.Cancel();
                        await FinishTestAsync(session, CancellationToken.None);
                    }
                    else
                    {
                        ut.Status = UserTestStatus.Finished;
                        ut.FinishedAt ??= DateTime.UtcNow;
                        await db.SaveChangesAsync(stoppingToken);

                        await BotSendMessage(chatId, $"Тест «{ut.Test.Name}» завершён администратором.", null,
                            stoppingToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* нормальное завершение */
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, exception.Message);

        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        switch (update.Type)
        {
            case UpdateType.CallbackQuery when update.CallbackQuery is { } cq:
            {
                var chatId = cq.Message?.Chat.Id ?? cq.From.Id;
                await WithChatLock(chatId, async () =>
                {
                    var data = cq.Data ?? string.Empty;

                    if (data.StartsWith(StartCbPrefix, StringComparison.Ordinal))
                    {
                        await BotAnswerCallback(cq.Id, ct);

                        if (!int.TryParse(data.AsSpan(StartCbPrefix.Length), out var utId)) return;

                        using var scope = scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var ut = await db.UserTests
                            .Include(x => x.Test)
                            .Include(x => x.UserProfile)
                            .FirstOrDefaultAsync(x => x.Id == utId, ct);

                        // базовые проверки безопасности/состояния
                        if (ut == null) return;
                        if (ut.UserProfile.TelegramUserId != chatId) return;
                        switch (ut.Status)
                        {
                            case UserTestStatus.Finished:
                                return;
                            case UserTestStatus.InProgress:
                                await BotSendMessage(chatId, "Вы уже начали тест. Ответьте на текущий вопрос.", null,
                                    ct);
                                return;
                        }

                        if (!ut.Test.IsActive || ut.Test.IsDeleted)
                        {
                            await EnsureRegistrationOrStartAsync(chatId, ut.UserProfileId, ut.TestId, ct);
                            return;
                        }

                        await StartTestFlowAsync(chatId, ut.UserProfileId, ut.TestId, ct);
                        return;
                    }

                    if (_sessions.TryGetValue(chatId, out var session))
                    {
                        session.Cts?.Cancel();
                        await ProcessAnswerAsync(session, data, ct);
                    }

                    await BotAnswerCallback(cq.Id, ct);
                });
                return;
            }
            case UpdateType.Message when update.Message is { } msg:
            {
                var chatId = msg.Chat.Id;
                await WithChatLock(chatId, async () =>
                {
                    var text = msg.Text ?? string.Empty;

                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.TelegramUserId == chatId, ct);

                    // 1) Пытаемся получить ФИО пользователя, см. п.3
                    if (_awaitingName.ContainsKey(chatId))
                    {
                        await ProcessNameAwaitingMessage(ct, text, chatId, msg, db);
                        return; // важно: не продолжаем, пока не введут корректно
                    }

                    // 2) Пытаемся вытащить testId из deep-link (/start test_<id>)
                    var requestedTestId = TryGetRequestedTestId(text);

                    // 3) Профиля нет — просим ФИО и, если есть testId из ссылки, запоминаем его
                    if (profile == null)
                    {
                        await ProcessWithoutProfileUser(ct, requestedTestId, chatId);
                        return;
                    }

                    // 4) Профиль есть и deep-link прислали — сразу стартуем тест
                    if (requestedTestId.HasValue)
                    {
                        await EnsureRegistrationOrStartAsync(chatId, profile.TelegramUserId, requestedTestId.Value, ct);
                        return;
                    }

                    // 5) Другие команды/поведение:
                    // /test <id>
                    if (text.StartsWith("/test ", StringComparison.OrdinalIgnoreCase))
                    {
                        await ProcessTestCommand(ct, text, chatId, profile);
                    }
                });

                return;
            }
        }
    }

    private async Task SendStartPromptAsync(int userTestId, long chatId, string testName, CancellationToken ct)
    {
        var kb = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithCallbackData("Начать", $"{StartCbPrefix}{userTestId}")
        );

        var text =
            $"Тест «{testName}» запущен! 👇\n" +
            $"Нажмите «Начать», когда будете готовы. После старта на каждый вопрос будет 1 минута.";

        await BotSendMessage(chatId, text, kb, ct);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ut = await db.UserTests.FirstOrDefaultAsync(x => x.Id == userTestId, ct);
        if (ut != null)
        {
            ut.StartNotifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }


    private static int? TryGetRequestedTestId(string text)
    {
        int? requestedTestId = null;
        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase) != true)
        {
            return requestedTestId;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && parts[1].StartsWith("test_", StringComparison.OrdinalIgnoreCase)
                             && int.TryParse(parts[1].AsSpan("test_".Length), out var testId))
        {
            requestedTestId = testId;
        }

        return requestedTestId;
    }

    private async Task ProcessWithoutProfileUser(CancellationToken ct, int? requestedTestId, long chatId)
    {
        if (requestedTestId.HasValue)
            _pendingStart[chatId] = requestedTestId.Value;

        _awaitingName[chatId] = true;
        await BotSendMessage(chatId, "Пожалуйста, введите ваше ФИО и номер школы.\n" +
                                     "Пример: Иванов Иван СОШ № 777", null, ct);
    }

    private async Task ProcessNameAwaitingMessage(CancellationToken ct, string text, long chatId, Message msg,
        AppDbContext db)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
        {
            await BotSendMessage(
                chatId,
                "Введены некорректные данные. Введите ФИО в формате «Иванов Иван СОШ № 777».",
                null,
                ct);
            return;
        }

        var username = msg.From?.Username ?? string.Empty;

        var userProfile = new UserProfile
        {
            TelegramUserId = chatId,
            Username = username,
            FullName = new string(text.Take(200).ToArray()),
            CreatedAt = DateTime.UtcNow
        };

        db.UserProfiles.Add(userProfile);
        await db.SaveChangesAsync(ct);

        _awaitingName.TryRemove(chatId, out _);
        await botClient.SendMessage(chatId, "Спасибо! Ваши данные сохранены.", cancellationToken: ct);

        // если ранее пришли по ссылке — стартуем отложенный тест
        if (_pendingStart.TryRemove(chatId, out var pendingTestId))
        {
            await EnsureRegistrationOrStartAsync(chatId, userProfile.TelegramUserId, pendingTestId, ct);
        }
    }

    private async Task ProcessTestCommand(CancellationToken ct, string text, long chatId, UserProfile profile)
    {
        var idStr = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault();
        if (int.TryParse(idStr, out var testId))
        {
            await StartTestFlowAsync(chatId, profile.TelegramUserId, testId, ct);
        }
        else
        {
            await botClient.SendMessage(chatId, "Используйте: /test <id>", cancellationToken: ct);
        }
    }

    private async Task EnsureRegistrationOrStartAsync(long chatId, long userProfileId, int testId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var test = await db.Tests
            .Include(t => t.Questions)
            .FirstOrDefaultAsync(t => t.Id == testId, ct);

        if (test == null)
        {
            await botClient.SendMessage(chatId, "Тест не найден.", cancellationToken: ct);
            return;
        }

        // уже есть попытка?
        var attempt = await db.UserTests
            .FirstOrDefaultAsync(
                ut => ut.UserProfileId == userProfileId
                      && ut.TestId == testId
                      && ut.Status != UserTestStatus.Finished,
                ct);

        if (attempt != null)
        {
            if (attempt.Status == UserTestStatus.InProgress)
            {
                await botClient.SendMessage(chatId, "Вы уже проходите этот тест. Ответьте на текущий вопрос.",
                    cancellationToken: ct);
                return;
            }

            if (attempt.Status == UserTestStatus.Pending)
            {
                await botClient.SendMessage(chatId, "Вы зарегистрированы. Тест скоро начнётся, дождитесь уведомления.",
                    cancellationToken: ct);
                return;
            }
        }

        var isTestCompleted = await db.UserTests.CountAsync(
            ut => ut.UserProfileId == userProfileId && ut.TestId == testId && ut.Status == UserTestStatus.Finished,
            ct) > 0;
        if (isTestCompleted)
        {
            await botClient.SendMessage(chatId, $"Вы уже прошли тест \"{test.Name}\".", cancellationToken: ct);
            return;
        }

        if (test.IsActive)
        {
            // было: await StartTestFlowAsync(...)

            if (attempt == null)
            {
                attempt = new UserTest
                {
                    UserProfileId = userProfileId,
                    TestId = testId,
                    Status = UserTestStatus.Pending
                };
                db.UserTests.Add(attempt);
                await db.SaveChangesAsync(ct);
            }

            // если уведомление уже слали — не спамим
            if (attempt.StartNotifiedAt == null)
            {
                await SendStartPromptAsync(attempt.Id, chatId, test.Name, ct);
            }
            else
            {
                await BotSendMessage(chatId,
                    "Тест уже запущен. Нажмите «Начать» в предыдущем сообщении, чтобы перейти к вопросам.",
                    null, ct);
            }

            return;
        }

        // не активен — регистрируем и ждём включения
        var pending = new UserTest
        {
            UserProfileId = userProfileId,
            TestId = testId,
            Status = UserTestStatus.Pending,
            StartedAt = null,
            FinishedAt = null,
            Score = 0
        };

        db.UserTests.Add(pending);
        await db.SaveChangesAsync(ct);

        await BotSendMessage(
            chatId,
            $"Тест «{test.Name}» ещё не начался. Мы пришлём вопросы, как только администратор включит тест.",
            null,
            ct);
    }

    private async Task StartTestFlowAsync(long chatId, long userProfileId, int testId, CancellationToken ct)
    {
        if (_sessions.TryGetValue(chatId, out _))
        {
            await BotSendMessage(chatId,
                "Вы уже проходите тест. Ответьте на текущий вопрос.",
                null,
                ct);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var test = await db.Tests
            .Include(t => t.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(t => t.Id == testId, ct);

        if (test == null)
        {
            await BotSendMessage(chatId, "Тест не найден.", null, ct);
            return;
        }

        if (!test.IsActive)
        {
            // если внезапно выключили между кликом и запуском — уходим в ожидание
            await EnsureRegistrationOrStartAsync(chatId, userProfileId, testId, ct);
            return;
        }

        // находим/создаём попытку
        var userTest = await db.UserTests
            .FirstOrDefaultAsync(x => x.UserProfileId == userProfileId
                                      && x.TestId == testId
                                      && x.Status != UserTestStatus.Finished, ct);

        if (userTest == null)
        {
            userTest = new UserTest
                { UserProfileId = userProfileId, TestId = testId, Status = UserTestStatus.InProgress };
            db.UserTests.Add(userTest);
        }

        userTest.Status = UserTestStatus.InProgress;
        userTest.StartedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var session = new UserTestSession
        {
            ChatId = chatId,
            TestId = test.Id,
            UserTestId = userTest.Id,
            Questions = test.Questions.ToList(),
            CurrentIndex = 0,
            Score = 0,
            Cts = new CancellationTokenSource()
        };

        Shuffle(session.Questions);

        _sessions[chatId] = session;

        await BotSendMessage(chatId, $"Начинаем тест: {test.Name}\nНа каждый вопрос — 1 минута.", null, ct);

        await SendQuestionAsync(session, ct);

        static void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    private async Task SendQuestionAsync(UserTestSession session, CancellationToken ct)
    {
        if (session.CurrentIndex >= session.Questions.Count)
        {
            await FinishTestAsync(session, ct);
            return;
        }

        var question = session.Questions[session.CurrentIndex];

        var rows = question.Options
            .Select(o => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    o.Text,
                    $"test_{session.UserTestId}_q{session.CurrentIndex}_o{o.Id}")
            })
            .ToArray();
        var keyboard = new InlineKeyboardMarkup(rows);

        // Отправляем стартовое сообщение

        var sent = await BotSendMessage(
            chatId: session.ChatId,
            text:
            $"Вопрос {session.CurrentIndex + 1} из {session.Questions.Count}.\n{question.Text}\n\n⏱ Время пошло! У Вас 1 минута.",
            keyboard,
            ct);
        session.CurrentMessageId = sent.MessageId;

        // TODO: нужно для логики обновления времени в таймере вопроса, но пока от этого решено отказаться из-за лимитов TG.

        // var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        // session.DeadlineUtc = deadline;

        // Сбрасываем и создаём новый CTS (без CancelAfter!)
        session.Cts?.Cancel();
        session.Cts?.Dispose();
        session.Cts = new CancellationTokenSource();
        var token = session.Cts.Token;

        // Задача таймаута: если Delay завершился без Cancel — считаем, что время вышло
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), token);
                if (!token.IsCancellationRequested)
                {
                    await OnTimeoutAsync(session, CancellationToken.None);
                }
            }
            catch (OperationCanceledException)
            {
                // Ответили вовремя — просто выходим
            }
        });
    }

    private async Task ProcessAnswerAsync(UserTestSession session, string data, CancellationToken ct)
    {
        // Разбор данных
        // data format: test_{userTestId}_q{idx}_o{optionId}
        var parts = data.Split('_', 'q', 'o');
        // parts: ["test", "{userTestId}", "", "{idx}", "", "{optionId}"]
        if (parts.Length < 6) return;

        if (!int.TryParse(parts[1], out var userTestId) ||
            !int.TryParse(parts[3], out var idx) ||
            !int.TryParse(parts[5], out var optionId))
            return;

        if (idx != session.CurrentIndex) return; // клик по старому вопросу — игнор
        if (userTestId != session.UserTestId) return; // если тест не тот - игнор

        var question = session.Questions[idx];

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var answer = new UserAnswer
        {
            UserTestId = session.UserTestId,
            QuestionId = question.Id,
            SelectedOptionId = optionId,
            AnsweredAt = DateTime.UtcNow
        };
        db.UserAnswers.Add(answer);

        // Проверяем правильность
        var selected = question.Options.FirstOrDefault(o => o.Id == optionId);
        if (selected != null && selected.IsCorrect)
        {
            session.Score += question.Weight;
        }

        await db.SaveChangesAsync(ct);

        await session.Cts.CancelAsync();
        session.CurrentIndex++;

        if (session.CurrentMessageId is { } mid)
        {
            await BotDeleteMessage(session.ChatId, mid, ct);
            session.CurrentMessageId = null;
        }

        await SendQuestionAsync(session, ct);
    }

    private async Task OnTimeoutAsync(UserTestSession session, CancellationToken ct)
    {
        // Записываем неполученный ответ как пропуск (без баллов)
        await WithChatLock(session.ChatId, async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var question = session.Questions[session.CurrentIndex];

            db.UserAnswers.Add(new UserAnswer
            {
                UserTestId = session.UserTestId,
                QuestionId = question.Id,
                SelectedOptionId = null,
                AnsweredAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            session.CurrentIndex++;
            if (session.CurrentMessageId.HasValue)
            {
                await BotDeleteMessage(session.ChatId, session.CurrentMessageId.Value, ct);
                await BotSendMessage(session.ChatId,
                    $"К сожалению Вы не успели ответить на вопрос №{session.CurrentIndex}.", null, ct);
            }

            await SendQuestionAsync(session, ct);
        });
    }

    private async Task FinishTestAsync(UserTestSession session, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var testName = string.Empty;
        var ut = await db.UserTests.FindAsync([session.UserTestId], ct);
        if (ut != null)
        {
            ut.Score = session.Score;
            ut.Status = UserTestStatus.Finished;
            ut.FinishedAt ??= DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var test = await db.Tests.FirstOrDefaultAsync(x => x.Id == ut.TestId, ct);
            testName = test?.Name;
        }

        await BotSendMessage(
            session.ChatId,
            $"Тест {(string.IsNullOrEmpty(testName) ? string.Empty : testName + " ")}завершён!",
            null,
            ct);

        _sessions.TryRemove(session.ChatId, out _);
    }

    private async Task<T> TgLimit<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var lease = await _tgLimiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired) throw new InvalidOperationException("TG limiter queue overflow");
        try
        {
            return await call(ct);
        }
        catch (Telegram.Bot.Exceptions.ApiRequestException ex)
            when (ex is { ErrorCode: 429, Parameters.RetryAfter: not null })
        {
            // Если Телеграм всё же вернул 429, ждём retry_after и повторяем 1 раз
            await Task.Delay(TimeSpan.FromSeconds(ex.Parameters.RetryAfter.Value), ct);
            return await call(ct);
        }
    }

    private async Task TgLimit(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var lease = await _tgLimiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired) throw new InvalidOperationException("TG limiter queue overflow");
        try
        {
            await call(ct);
        }
        catch (Telegram.Bot.Exceptions.ApiRequestException ex)
            when (ex is { ErrorCode: 429, Parameters.RetryAfter: not null })
        {
            await Task.Delay(TimeSpan.FromSeconds(ex.Parameters.RetryAfter.Value), ct);
            await call(ct);
        }
    }

    // Удобные «сахарные» методы под нужные вызовы Telegram.Bot
    private Task<Message> BotSendMessage(long chatId, string text, ReplyMarkup? replyMarkup, CancellationToken ct) =>
        TgLimit(
            ct2 => botClient.SendMessage(chatId: chatId, text: text, replyMarkup: replyMarkup, cancellationToken: ct2),
            ct);

    private Task BotAnswerCallback(string callbackId, CancellationToken ct) =>
        TgLimit(ct2 => botClient.AnswerCallbackQuery(callbackId, cancellationToken: ct2), ct);

    private async Task BotDeleteMessage(long chatId, int messageId, CancellationToken ct) =>
        await TgLimit(async ct2 =>
        {
            try
            {
                await botClient.DeleteMessage(chatId: chatId, messageId: messageId, cancellationToken: ct2);
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException)
            {
                // Бывает 400/403 (бот не админ в группе / сообщение уже исчезло) — просто игнорируем
            }

            return 0;
        }, ct);

    // private Task BotEditMessageText(long chatId, int messageId, string text, InlineKeyboardMarkup? replyMarkup,
    //     CancellationToken ct) =>
    //     TgLimit(
    //         ct2 => _botClient.EditMessageText(chatId: chatId, messageId: messageId, text: text,
    //             replyMarkup: replyMarkup, cancellationToken: ct2), ct);

    private class UserTestSession
    {
        public long ChatId { get; set; }
        public int UserTestId { get; set; }
        public int TestId { get; set; }
        public List<Question> Questions { get; set; } = [];
        public int CurrentIndex { get; set; }
        public int Score { get; set; }
        public CancellationTokenSource Cts { get; set; }

        /// <summary>
        /// Идентификатор сообщения с вопросом.
        /// </summary>
        public int? CurrentMessageId { get; set; }
        // /// <summary>
        // /// дедлайн этого вопроса.
        // /// </summary>
        // public DateTimeOffset? DeadlineUtc { get; set; } // <-- 
    }
}