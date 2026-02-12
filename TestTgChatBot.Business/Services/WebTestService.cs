using Microsoft.EntityFrameworkCore;
using TestTgChatBot.Dao;
using TestTgChatBot.Dao.Models;

namespace TestTgChatBot.Business.Services;

public interface IWebTestService
{
    Task<Guid> GenerateTokenAsync(int testId, int validityMinutes = 43200); // Default 30 days
    Task<TestAccessToken?> GetTokenAsync(Guid tokenId);
    Task<UserProfile> RegisterWebUserAsync(string fullName, string phoneNumber, string schoolName);
    Task<UserTest> StartWebTestAsync(Guid tokenId, long userProfileId);
    Task SubmitAnswerAsync(int userTestId, int questionId, List<int> selectedOptionIds);
    Task FinishTestAsync(int userTestId);
    Task CheckTimeLimitAsync(int userTestId);
    Task<List<TestAccessToken>> GetUnusedTokensAsync(int testId);
}

public class WebTestService : IWebTestService
{
    private readonly AppDbContext _db;

    public WebTestService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> GenerateTokenAsync(int testId, int validityMinutes = 43200)
    {
        var token = new TestAccessToken
        {
            Id = Guid.NewGuid(),
            TestId = testId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(validityMinutes),
            IsUsed = false
        };

        _db.TestAccessTokens.Add(token);
        await _db.SaveChangesAsync();
        return token.Id;
    }

    public async Task<TestAccessToken?> GetTokenAsync(Guid tokenId)
    {
        return await _db.TestAccessTokens
            .Include(t => t.Test)
            .FirstOrDefaultAsync(t => t.Id == tokenId);
    }

    public async Task<UserProfile> RegisterWebUserAsync(string fullName, string phoneNumber, string schoolName)
    {
        
        var existing = await _db.UserProfiles
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);

        if (existing != null)
        {
            // Update info if needed
            existing.FullName = fullName;
            existing.SchoolName = schoolName;
            await _db.SaveChangesAsync();
            return existing;
        }

        var toId = -DateTime.UtcNow.Ticks; 
        // Ensure it's negative
        if (toId > 0) toId = -toId;

        // Verify uniqueness just in case
        while (await _db.UserProfiles.AnyAsync(u => u.TelegramUserId == toId))
        {
            toId--;
        }

        var newUser = new UserProfile
        {
            TelegramUserId = toId,
            FullName = fullName,
            PhoneNumber = phoneNumber,
            SchoolName = schoolName,
            CreatedAt = DateTime.UtcNow
        };

        _db.UserProfiles.Add(newUser);
        await _db.SaveChangesAsync();
        return newUser;
    }

    public async Task<UserTest> StartWebTestAsync(Guid tokenId, long userProfileId)
    {
        var token = await _db.TestAccessTokens.FirstOrDefaultAsync(t => t.Id == tokenId);
        if (token == null || token.IsUsed || (token.ExpiresAt.HasValue && token.ExpiresAt < DateTime.UtcNow))
        {
            throw new InvalidOperationException("Invalid or expired token.");
        }

        // Check if user has already taken this test
        var existingTest = await _db.UserTests
            .AnyAsync(ut => ut.UserProfileId == userProfileId && ut.TestId == token.TestId);
        
        if (existingTest)
        {
             throw new InvalidOperationException("Вы уже проходили этот тест.");
        }

        var userTest = new UserTest
        {
            UserProfileId = userProfileId,
            TestId = token.TestId,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            Status = UserTestStatus.InProgress
        };

        _db.UserTests.Add(userTest);
        
        // Mark token as used
        token.IsUsed = true;
        token.UsedByUserTest = userTest;
        
        await _db.SaveChangesAsync();
        return userTest;
    }

    public async Task SubmitAnswerAsync(int userTestId, int questionId, List<int> selectedOptionIds)
    {
        var userTest = await _db.UserTests.FindAsync(userTestId);
        if (userTest == null || userTest.Status != UserTestStatus.InProgress) return;

        // Check global time limit (30 mins)
        if (userTest.StartedAt.HasValue && (DateTime.UtcNow - userTest.StartedAt.Value).TotalMinutes > 30)
        {
            await FinishTestAsync(userTestId);
            return;
        }

        // Check if answer already exists - remove old ones if any (allow re-answer? usually not for this flow but safer to clear)
        var existingAnswers = await _db.UserAnswers
            .Where(ua => ua.UserTestId == userTestId && ua.QuestionId == questionId)
            .ToListAsync();

        if (existingAnswers.Any())
        {
             _db.UserAnswers.RemoveRange(existingAnswers);
        }

        foreach (var optionId in selectedOptionIds)
        {
            var answer = new UserAnswer
            {
                UserTestId = userTestId,
                QuestionId = questionId,
                SelectedOptionId = optionId,
                AnsweredAt = DateTime.UtcNow
            };
            _db.UserAnswers.Add(answer);
        }
        
        if (!selectedOptionIds.Any())
        {
            // TODO: что то сделать?
        }

        await _db.SaveChangesAsync();
    }

    public async Task FinishTestAsync(int userTestId)
    {
        var userTest = await _db.UserTests
            .Include(ut => ut.Test)
            .ThenInclude(t => t.Questions)
            .Include(ut => ut.UserAnswers)
            .ThenInclude(ua => ua.SelectedOption)
            .FirstOrDefaultAsync(ut => ut.Id == userTestId);

        if (userTest == null || userTest.Status == UserTestStatus.Finished) return;

        // Calculate score
        int score = 0;
        
        // Group answers by QuestionId
        var answersByQuestion = userTest.UserAnswers
            .GroupBy(ua => ua.QuestionId)
            .ToList();

        foreach (var question in userTest.Test.Questions)
        {
            var userAnswersForQ = answersByQuestion
                .FirstOrDefault(g => g.Key == question.Id)?
                .Select(ua => ua.SelectedOptionId)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToHashSet() ?? new HashSet<int>();

            var correctOptionIds = question.Options
                .Where(o => o.IsCorrect)
                .Select(o => o.Id)
                .ToHashSet();

            // Logic: 
            // 1. Must select all correct options.
            // 2. Must NOT select any incorrect option.
            
            bool matches = correctOptionIds.SetEquals(userAnswersForQ);
            
            if (matches)
            {
                score++; // Or question.Weight if we had weights
            }
        }

        userTest.Score = score;
        userTest.Status = UserTestStatus.Finished;
        userTest.FinishedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    public async Task CheckTimeLimitAsync(int userTestId)
    {
        // Helper to force finish if time is up
        var userTest = await _db.UserTests.FindAsync(userTestId);
        if (userTest == null || userTest.Status != UserTestStatus.Finished)
        {
             if (userTest?.StartedAt != null && (DateTime.UtcNow - userTest.StartedAt.Value).TotalMinutes > 30)
             {
                 await FinishTestAsync(userTestId);
             }
        }
    }

    public async Task<List<TestAccessToken>> GetUnusedTokensAsync(int testId)
    {
        return await _db.TestAccessTokens
            .Where(t => t.TestId == testId && !t.IsUsed && (!t.ExpiresAt.HasValue || t.ExpiresAt > DateTime.UtcNow))
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
    }
}
