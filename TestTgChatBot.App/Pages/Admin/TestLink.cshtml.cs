using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QRCoder; // Requires QRCoder package
using System.Drawing;
using System.Drawing.Imaging;
using TestTgChatBot.Dao;
using TestTgChatBot.Dao.Models;
using TestTgChatBot.Business.Services;

namespace TestTgChatBot.App.Pages.Admin;

public class TestLinkModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IWebTestService _webTestService;

    public TestLinkModel(AppDbContext db, IWebTestService webTestService)
    {
        _db = db;
        _webTestService = webTestService;
    }

    public string TestName { get; set; } = "";
    public List<(string Token, byte[] QrCode)> UnusedTokens { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int testId)
    {
        var test = await _db.Tests.FindAsync(testId);
        if (test == null) return NotFound();
        TestName = test.Name;
        
        await LoadUnusedTokens(testId);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int testId, int count)
    {
        var test = await _db.Tests.FindAsync(testId);
        if (test == null) return NotFound();
        TestName = test.Name;

        for (int i = 0; i < count; i++)
        {
            await _webTestService.GenerateTokenAsync(testId);
        }

        await LoadUnusedTokens(testId);

        return Page();
    }

    private async Task LoadUnusedTokens(int testId)
    {
        var tokens = await _webTestService.GetUnusedTokensAsync(testId);
        var qrGenerator = new QRCodeGenerator();
        
        foreach (var token in tokens)
        {
            var path = Url.Content($"~/Test/Entry/{token.Id}");
            var url = $"https://{Request.Host}{path}";

            var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeBytes = qrCode.GetGraphic(3);
            
            UnusedTokens.Add((token.Id.ToString(), qrCodeBytes));
        }
    }
}
