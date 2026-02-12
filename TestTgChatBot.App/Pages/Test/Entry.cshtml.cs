using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TestTgChatBot.Business.Services;
using TestTgChatBot.Dao.Models;

namespace TestTgChatBot.App.Pages.Test;

public class EntryModel : PageModel
{
    private readonly IWebTestService _webTestService;

    public EntryModel(IWebTestService webTestService)
    {
        _webTestService = webTestService;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Token { get; set; }

    public TestAccessToken? TestToken { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Введите номер телефона")]
        [Phone(ErrorMessage = "Неверный формат телефона")]
        public string PhoneNumber { get; set; } = null!;

        [Required(ErrorMessage = "Введите ФИО")]
        public string FullName { get; set; } = null!;

        [Required(ErrorMessage = "Введите школу")]
        public string SchoolName { get; set; } = null!;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        TestToken = await _webTestService.GetTokenAsync(Token);
        if (TestToken == null)
        {
            ErrorMessage = "Токен не найден.";
        }
        else if (TestToken.IsUsed)
        {
            ErrorMessage = "Этот токен уже был использован.";
        }
        else if (TestToken.ExpiresAt < DateTime.UtcNow)
        {
            ErrorMessage = "Срок действия токена истек.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        TestToken = await _webTestService.GetTokenAsync(Token);
        if (TestToken == null || TestToken.IsUsed || TestToken.ExpiresAt < DateTime.UtcNow)
        {
            ErrorMessage = "Ссылка недействительна.";
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var user = await _webTestService.RegisterWebUserAsync(Input.FullName, Input.PhoneNumber, Input.SchoolName);
            var userTest = await _webTestService.StartWebTestAsync(Token, user.TelegramUserId); // Using TelegramUserId as ID

            // Set cookie or session to track userTestId
            // Simple approach: redirect to Run page with UserTestId (encrypted or just raw for internal usage if secured? No, raw is risky allow guessing)
            // Better: Put in TempData or Session. Or encrypted query param.
            // Let's use a signed cookie / simple cookie since this is a transient session.
            
            Response.Cookies.Append("CurrentTestId", userTest.Id.ToString(), new CookieOptions { HttpOnly = true, Expires = DateTime.UtcNow.AddMinutes(40) });

            return RedirectToPage("Run");
        }
        catch (Exception ex)
        {
            ErrorMessage = "Ошибка при регистрации: " + ex.Message;
            return Page();
        }
    }
}
