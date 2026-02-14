using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using TestTgChatBot.App.Areas.Admin.ViewModels;
using TestTgChatBot.Business.Services;
using TestTgChatBot.Dao;
using TestTgChatBot.Dao.Models;

namespace TestTgChatBot.App.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize]
    public class TestsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IExcelExportService _excelExportService;
        private readonly IExcelImportService _excelImportService;
        private readonly IWebTestService _webTestService;
        private readonly string _botUserName;

        public TestsController(AppDbContext db, IConfiguration cfg, IExcelExportService excelExportService, IExcelImportService excelImportService, IWebTestService webTestService)
        {
            _botUserName = cfg["Telegram:BotUserName"] ?? throw new ArgumentNullException("Telegram:BotUserName");
            _db = db;
            _excelExportService = excelExportService;
            _excelImportService = excelImportService;
            _webTestService = webTestService;
        }

        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var items = await _db.Tests
                .AsNoTracking()
                .OrderByDescending(t => t.Id)
                .Select(t => new TestsListViewModel
                {
                    Id = t.Id,
                    Name = t.Name,
                    IsActive = t.IsActive,
                    StartTime = t.StartTime,
                    EndTime = t.EndTime,
                    Questions = t.Questions.Count,
                    IsDeleted = t.IsDeleted
                })
                .ToListAsync(ct);
            
            items.ForEach(x => x.TgTestLink = GetLink(x.Id));

            return View(items);
        }

        public async Task<IActionResult> Details(int id, CancellationToken ct)
        {
            var test = await _db.Tests
                .Include(t => t.Questions)
                .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (test == null) return NotFound();
            return View(test);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Activate(int id, CancellationToken ct)
        {
            var isAnyTestActive = await _db.Tests.CountAsync(x => x.IsActive == true, cancellationToken: ct) > 0;
            if (isAnyTestActive)
            {
                TempData["Error"] = "Одновременно может быть активен только один тест.";
                return RedirectToAction(nameof(Index));
            }

            var test = await _db.Tests.FindAsync([id], ct);
            if (test == null) return NotFound();

            test.IsActive = true;
            test.StartTime = DateTime.UtcNow;
            test.EndTime = null;

            await _db.SaveChangesAsync(ct);
            TempData["Ok"] = $"Тест #{id} включён.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
        {
            var test = await _db.Tests.FindAsync([id], ct);
            if (test == null) return NotFound();

            test.IsActive = false;
            test.EndTime = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            TempData["Ok"] = $"Тест #{id} выключен.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Qr(int id, CancellationToken ct)
        {
            var test = await _db.Tests.FindAsync([id], ct);
            if (test == null) return NotFound();

            var link = $"https://t.me/{_botUserName}?start=test_{test.Id}";

            using var gen = new QRCodeGenerator();
            var data = gen.CreateQrCode(link, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(data).GetGraphic(10);
            return File(png, "image/png", $"test_{id}.png");
        }

        [HttpGet]
        public async Task<IActionResult> WebQr(int id, CancellationToken ct)
        {
            var test = await _db.Tests.FindAsync([id], ct);
            if (test == null) return NotFound();
            return View(test);
        }

        [HttpPost]
        public async Task<IActionResult> WebQr(int id, int count, CancellationToken ct)
        {
            var test = await _db.Tests.FindAsync([id], ct);
            if (test == null) return NotFound();

            if (count <= 0 || count > 100) count = 10;

            var codes = new List<(string Token, byte[] QrCode)>();
            
            // Generate tokens
            // Using loop because we need individual QR codes
            using var gen = new QRCodeGenerator();
            
            for (int i = 0; i < count; i++)
            {
                await _webTestService.GenerateTokenAsync(id);
            }

            // Get ALL unused tokens for this test
            var unusedTokens = await _webTestService.GetUnusedTokensAsync(id);
            
            foreach (var token in unusedTokens)
            {
                // URL: https://HOST/Test/Entry/GUID
                var path = Url.Content($"~/Test/Entry/{token.Id}");
                var url = $"https://{Request.Host}{path}";

                var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                var png = new PngByteQRCode(data).GetGraphic(5);
                
                codes.Add((token.Id.ToString(), png));
            }

            ViewBag.TestName = test.Name;
            return View("WebQrPrint", codes);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var test = await _db.Tests
                .Include(t => t.Questions)
                .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (test == null) return NotFound();

            test.IsDeleted = true;
            await _db.SaveChangesAsync(ct);
            TempData["Ok"] = $"Тест #{id} удалён.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult Create()
        {
            var viewModel = new TestEditViewModel
            {
                Questions =
                {
                    new QuestionEditViewModel
                    {
                        Weight = 1,
                        Options = { new OptionEditViewModel(), new OptionEditViewModel() }
                    }
                }
            };
            return View("Edit", viewModel);
        }

        public async Task<IActionResult> CreateBasedOn(int id, CancellationToken ct)
        {
            var test = await _db.Tests
                .Include(t => t.Questions)
                .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (test == null) return NotFound();

            var viewModel = new TestEditViewModel
            {
                Name = test.Name,
                Description = test.Description,
                Questions = test.Questions
                    .OrderBy(q => q.Id)
                    .Select(q => new QuestionEditViewModel
                    {
                        Text = q.Text,
                        Weight = q.Weight,
                        Options = q.Options
                            .OrderBy(o => o.Id)
                            .Select(o => new OptionEditViewModel { Text = o.Text, IsCorrect = o.IsCorrect })
                            .ToList()
                    })
                    .ToList()
            };
            
            return View("Edit", viewModel);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TestEditViewModel viewModel, CancellationToken ct)
        {
            ValidateTest(viewModel);
            if (!ModelState.IsValid)
            {
                TempData["Error"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).First();
                return View("Edit", viewModel);
            }

            var test = new Test
            {
                Name = viewModel.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(viewModel.Description) ? null : viewModel.Description.Trim(),
                IsActive = false
            };

            foreach (var q in viewModel.Questions!)
            {
                var question = new Question
                {
                    Text = q.Text.Trim(),
                    Weight = q.Weight,
                    Test = test
                };
                foreach (var o in q.Options!)
                {
                    question.Options.Add(new TestOption
                    {
                        Text = o.Text.Trim(),
                        IsCorrect = o.IsCorrect
                    });
                }

                test.Questions.Add(question);
            }

            _db.Tests.Add(test);
            await _db.SaveChangesAsync(ct);

            TempData["Ok"] = $"Тест «{test.Name}» создан.";
            return RedirectToAction(nameof(Details), new { id = test.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id, CancellationToken ct)
        {
            var test = await _db.Tests
                .Include(t => t.Questions)
                .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (test == null) return NotFound();

            var viewModel = new TestEditViewModel
            {
                Id = test.Id,
                Name = test.Name,
                Description = test.Description,
                Questions = test.Questions
                    .OrderBy(q => q.Id)
                    .Select(q => new QuestionEditViewModel
                    {
                        Text = q.Text,
                        Weight = q.Weight,
                        Id = q.Id,
                        Options = q.Options
                            .OrderBy(o => o.Id)
                            .Select(o => new OptionEditViewModel { Text = o.Text, IsCorrect = o.IsCorrect, Id = o.Id })
                            .ToList()
                    })
                    .ToList()
            };

            if (viewModel.Questions.Count == 0)
            {
                viewModel.Questions.Add(new QuestionEditViewModel
                {
                    Weight = 1,
                    Options = { new OptionEditViewModel(), new OptionEditViewModel() }
                });
            }

            return View("Edit", viewModel);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, TestEditViewModel vm, CancellationToken ct)
        {
            if (id != vm.Id) return BadRequest();

            ValidateTest(vm);
            if (!ModelState.IsValid) return View("Edit", vm);

            var test = await _db.Tests
                .Include(t => t.Questions)
                .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
            if (test == null) return NotFound();

            test.Name = vm.Name.Trim();
            test.Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim();

            foreach (var qvm in vm.Questions)
            {
                Question q;

                q = test.Questions.First(x => x.Id == qvm.Id);
                q.Text = qvm.Text.Trim();
                q.Weight = qvm.Weight;


                foreach (var ovm in qvm.Options)
                {
                    TestOption o;
                    if (ovm.Id <= 0)
                    {
                        continue;
                    }

                    o = q.Options.First(x => x.Id == ovm.Id);
                    o.Text = ovm.Text.Trim();
                    o.IsCorrect = ovm.IsCorrect;
                }
            }

            await _db.SaveChangesAsync(ct);
            TempData["Ok"] = $"Тест «{test.Name}» обновлён.";
            return RedirectToAction(nameof(Details), new { id = test.Id });
        }

        private void ValidateTest(TestEditViewModel viewModel)
        {
            if (string.IsNullOrWhiteSpace(viewModel.Name))
                ModelState.AddModelError(nameof(viewModel.Name), "Укажите название теста.");

            if (viewModel.Questions == null || viewModel.Questions.Count == 0)
                ModelState.AddModelError("", "Добавьте хотя бы один вопрос.");

            for (var i = 0; i < viewModel.Questions.Count; i++)
            {
                var q = viewModel.Questions[i];
                if (string.IsNullOrWhiteSpace(q.Text))
                    ModelState.AddModelError($"Questions[{i}].Text", "Заполните текст вопроса.");

                if (q.Weight <= 0)
                    ModelState.AddModelError($"Questions[{i}].Weight", "Вес должен быть больше 0.");

                if (q.Options == null || q.Options.Count == 0)
                    ModelState.AddModelError($"Questions[{i}].Options", "Добавьте варианты ответа.");

                if (q.Options != null && q.Options.All(o => !o.IsCorrect))
                    ModelState.AddModelError($"Questions[{i}].Options", "Должен быть хотя бы один правильный вариант.");
            }
        }
        
        public IActionResult Upload() => View();

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(UploadExcelViewModel vm, CancellationToken ct)
        {
            if (vm.File == null || vm.File.Length == 0)
            {
                ModelState.AddModelError("", "Выберите .xlsx файл.");
                return View(vm);
            }
            if (!vm.File.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("", "Поддерживается только .xlsx.");
                return View(vm);
            }

            await using var ms = new MemoryStream();
            await vm.File.CopyToAsync(ms, ct);
            ms.Position = 0;

            var (ok, err, testId, testName, qCount) = await _excelImportService.ImportAsync(ms, ct);
            if (!ok)
            {
                TempData["Error"] = $"Ошибка импорта: {err}";
                return RedirectToAction(nameof(Index));
            }

            TempData["Ok"] = $"Импортировано: «{testName}», вопросов: {qCount} (ID={testId}).";
            return RedirectToAction(nameof(Details), new { id = testId });
        }

        // Выгрузка по конкретному тесту.
        [HttpGet]
        public async Task<IActionResult> Export(int id, CancellationToken ct)
        {
            var bytes = await _excelExportService.ExportTestAsync(id, ct);

            var fn = $"results_test_{id}_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fn);
        }

        // Выгрузка всех результатов.
        [HttpGet]
        public async Task<IActionResult> ExportAll(CancellationToken ct)
        {
            var bytes = await _excelExportService.ExportAllAsync(ct);

            var fn = $"results_all_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fn);
        }

        // Выгрузка всех рейтинга.
        [HttpGet]
        public async Task<IActionResult> ExportRating(CancellationToken ct)
        {
            var bytes = await _excelExportService.ExportRatingAsync(ct);

            var fn = $"results_rating_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fn);
        }

        private string GetLink(int id)
        {
            var link = $"https://t.me/{_botUserName}?start=test_{id}";

            return link;
        }
    }
}