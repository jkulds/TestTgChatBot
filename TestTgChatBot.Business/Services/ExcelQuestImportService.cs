using OfficeOpenXml;
using TestTgChatBot.Dao;
using TestTgChatBot.Dao.Models;

namespace TestTgChatBot.Business.Services;

public class ExcelImportService(AppDbContext db) : IExcelImportService
{
    public async Task<(bool Success, string? Error, int TestId, string TestName, int QuestionCount)>
        ImportAsync(Stream xlsx, CancellationToken ct = default)
    {
        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("quest");
            using var pkg = new ExcelPackage(xlsx);
            var ws = pkg.Workbook.Worksheets.FirstOrDefault();
            if (ws == null) return (false, "В книге нет листов", 0, string.Empty, 0);

            var testName = ws.Cells["A1"].GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(testName))
                return (false, "A1: не указано название теста", 0, string.Empty, 0);

            var description = ws.Cells["B1"].GetValue<string>()?.Trim();

            var test = new Test
            {
                Name = testName,
                Description = description,
                IsActive = false
            };

            db.Tests.Add(test);

            var questionCount = 0;
            var row = 3; // шапка в строке 2
            while (!string.IsNullOrWhiteSpace(ws.Cells[row, 1].Text))
            {
                var qText = ws.Cells[row, 1].GetValue<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(qText))
                {
                    row++;
                    continue;
                }

                var weight = ws.Cells[row, 2].GetValue<int>();
                if (weight <= 0) weight = 1;

                var q = new Question
                {
                    Test = test,
                    Text = qText,
                    Weight = weight
                };

                db.Questions.Add(q);

                // Опции с колонки 3 и далее
                var lastCol = ws.Dimension.End.Column;
                var anyOption = false;
                for (var col = 3; col <= lastCol; col++)
                {
                    var cell = ws.Cells[row, col].GetValue<string>();
                    if (string.IsNullOrWhiteSpace(cell)) continue;

                    // формат: "Текст|true/false"
                    var parts = cell.Split('|', StringSplitOptions.TrimEntries);
                    var opt = new TestOption
                    {
                        Question = q,
                        Text = parts[0],
                        IsCorrect = parts.Length > 1 && bool.TryParse(parts[1], out var isCorr) && isCorr
                    };

                    db.Options.Add(opt);
                    anyOption = true;
                }

                if (!anyOption)
                    return (false, $"Строка {row}: у вопроса нет вариантов", 0, string.Empty, 0);

                questionCount++;
                row++;
            }

            await db.SaveChangesAsync(ct);

            return (true, null, test.Id, test.Name, questionCount);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0, string.Empty, 0);
        }
    }
}

public interface IExcelImportService
{
    Task<(bool Success, string? Error, int TestId, string TestName, int QuestionCount)>
        ImportAsync(Stream xlsx, CancellationToken ct = default);
}

public record ImportResult(bool Success, string TestName = "", int QuestionCount = 0, string ErrorMessage = "");