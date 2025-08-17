using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using TestTgChatBot.Dao;
using TestTgChatBot.Dao.Models;

namespace TestTgChatBot.Business.Services
{
    public class ExcelExportService(AppDbContext db) : IExcelExportService
    {
        private const string ExcelDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        public async Task<byte[]> ExportTestAsync(int testId, CancellationToken ct = default)
        {
            ExcelPackage.License.SetNonCommercialPersonal("quest");

            var test = await db.Tests
                .Include(t => t.Questions)
                .FirstOrDefaultAsync(t => t.Id == testId, ct);
            if (test == null) throw new InvalidOperationException("Тест не найден");

            var maxScore = test.Questions.Sum(q => q.Weight);

            var rows = await db.UserTests
                .Include(ut => ut.UserProfile)
                .Where(ut => ut.TestId == testId && ut.Status == UserTestStatus.Finished)
                .OrderByDescending(ut => ut.FinishedAt)
                .Select(ut => new
                {
                    ut.Id,
                    ut.UserProfile.FullName,
                    ut.UserProfile.Username,
                    ut.UserProfile.TelegramUserId,
                    ut.StartedAt,
                    ut.FinishedAt,
                    ut.Score
                })
                .ToListAsync(ct);

            using var pkg = new ExcelPackage();
            var ws = pkg.Workbook.Worksheets.Add($"Test_{test.Id}");

            ws.Cells["A1"].Value = $"Результаты теста: {test.Name}";
            ws.Cells["A1"].Style.Font.Bold = true;
            ws.Cells["A1"].Style.Font.Size = 14;

            object[] header = ["№", "ФИО", "Telegram логин", "TelegramId", "Начато", "Завершено", "Баллы", "Из макс."];
            ws.Cells["A3"].LoadFromArrays([header]);
            using (var r = ws.Cells[3, 1, 4, header.Length])
            {
                r.Style.Font.Bold = true;
                r.Style.Fill.PatternType = ExcelFillStyle.Solid;
                r.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(235, 235, 235));
            }

            int row = 5;
            int i = 1;
            foreach (var r in rows)
            {
                ws.Cells[row, 1].Value = i++;
                ws.Cells[row, 2].Value = r.FullName;
                ws.Cells[row, 3].Value = r.Username;
                ws.Cells[row, 4].Value = r.TelegramUserId;
                ws.Cells[row, 5].Value = r.StartedAt?.ToLocalTime();
                ws.Cells[row, 5].Style.Numberformat.Format = ExcelDateTimeFormat;
                ws.Cells[row, 6].Value = r.FinishedAt?.ToLocalTime();
                ws.Cells[row, 6].Style.Numberformat.Format = ExcelDateTimeFormat;
                ws.Cells[row, 7].Value = r.Score;
                ws.Cells[row, 8].Value = maxScore;
                row++;
            }

            ws.Cells.AutoFitColumns();

            return await pkg.GetAsByteArrayAsync(ct);
        }

        public async Task<byte[]> ExportAllAsync(CancellationToken ct = default)
        {
            ExcelPackage.License.SetNonCommercialPersonal("quest");

            // словарь максимальных баллов по тесту
            var maxScores = await db.Questions
                .GroupBy(q => q.TestId)
                .Select(g => new { g.Key, Sum = g.Sum(x => x.Weight) })
                .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

            var rows = await db.UserTests
                .Include(ut => ut.UserProfile)
                .Include(ut => ut.Test)
                .Where(ut => ut.Status == UserTestStatus.Finished)
                .OrderByDescending(ut => ut.FinishedAt)
                .Select(ut => new
                {
                    ut.Id,
                    ut.TestId,
                    TestName = ut.Test.Name,
                    ut.UserProfile.FullName,
                    ut.UserProfile.Username,
                    ut.UserProfile.TelegramUserId,
                    ut.StartedAt,
                    ut.FinishedAt,
                    ut.Score
                })
                .ToListAsync(ct);

            using var pkg = new ExcelPackage();
            var ws = pkg.Workbook.Worksheets.Add("All Results");

            object[] header =
            [
                "№",
                "Тест",
                "ID теста",
                "ФИО",
                "Telegram логин",
                "Telegram Id",
                "Начато",
                "Завершено",
                "Баллы",
                "Из макс."
            ];

            ws.Cells["A1"].LoadFromArrays([header]);
            using (var r = ws.Cells[1, 1, 1, header.Length])
            {
                r.Style.Font.Bold = true;
                r.Style.Fill.PatternType = ExcelFillStyle.Solid;
                r.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(235, 235, 235));
            }

            int row = 2;
            int i = 1;
            foreach (var x in rows)
            {
                var max = maxScores.TryGetValue(x.TestId, out var m) ? m : 0;

                ws.Cells[row, 1].Value = i++;
                ws.Cells[row, 2].Value = x.TestName;
                ws.Cells[row, 3].Value = x.TestId;
                ws.Cells[row, 4].Value = x.FullName;
                ws.Cells[row, 5].Value = x.Username;
                ws.Cells[row, 6].Value = x.TelegramUserId;
                ws.Cells[row, 7].Value = x.StartedAt?.ToLocalTime();
                ws.Cells[row, 7].Style.Numberformat.Format = ExcelDateTimeFormat;
                ws.Cells[row, 8].Value = x.FinishedAt?.ToLocalTime();
                ws.Cells[row, 8].Style.Numberformat.Format = ExcelDateTimeFormat;
                ws.Cells[row, 9].Value = x.Score;
                ws.Cells[row, 10].Value = max;
                row++;
            }

            ws.Cells.AutoFitColumns();
            return await pkg.GetAsByteArrayAsync(ct);
        }

        public async Task<byte[]> ExportRatingAsync(CancellationToken ct = default)
        {
            ExcelPackage.License.SetNonCommercialPersonal("quest");

            var maxScores = await db.Questions
                .GroupBy(q => q.TestId)
                .Select(g => new { g.Key, Sum = g.Sum(x => x.Weight) })
                .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

            var groupedRows = await db.UserTests
                .Include(x => x.UserProfile)
                .Include(x => x.Test)
                .Where(x => x.Status == UserTestStatus.Finished)
                .GroupBy(x => x.UserProfile.Username)
                .ToDictionaryAsync(x => x.Key!, y => (
                    passedTests: y.Count(),
                    totalScore: y.Sum(x => x.Score),
                    testIds: y.Select(x => x.TestId).ToHashSet(),
                    userId: y.First().UserProfile.TelegramUserId,
                    fullName: y.First().UserProfile.FullName), ct);


            using var pkg = new ExcelPackage();
            var ws = pkg.Workbook.Worksheets.Add("All Results");

            object[] header =
            [
                "№",
                "Количество пройденных тестов",
                "ФИО",
                "Telegram логин",
                "Telegram Id",
                "Набранные баллы",
                "Из возможных макс."
            ];

            ws.Cells["A1"].LoadFromArrays([header]);
            using (var r = ws.Cells[1, 1, 1, header.Length])
            {
                r.Style.Font.Bold = true;
                r.Style.Fill.PatternType = ExcelFillStyle.Solid;
                r.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(235, 235, 235));
            }

            var row = 2;
            var i = 1;
            foreach (var groupedRow in groupedRows.OrderByDescending(x => x.Value.totalScore))
            {
                var possibleMaxScore = maxScores.Where(x => groupedRow.Value.testIds.Contains(x.Key)).Sum(x => x.Value);

                ws.Cells[row, 1].Value = i++;
                ws.Cells[row, 2].Value = groupedRow.Value.passedTests;
                ws.Cells[row, 3].Value = groupedRow.Value.fullName;
                ws.Cells[row, 4].Value = groupedRow.Key;
                ws.Cells[row, 5].Value = groupedRow.Value.userId;
                ws.Cells[row, 6].Value = groupedRow.Value.totalScore;
                ws.Cells[row, 7].Value = possibleMaxScore;
                row++;
            }

            ws.Cells.AutoFitColumns();
            return await pkg.GetAsByteArrayAsync(ct);
        }
    }

    public interface IExcelExportService
    {
        Task<byte[]> ExportTestAsync(int testId, CancellationToken ct = default);
        Task<byte[]> ExportAllAsync(CancellationToken ct = default);
        Task<byte[]> ExportRatingAsync(CancellationToken ct = default);
    }
}