using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TestTgChatBot.App.Areas.Auth.ViewModels;

namespace TestTgChatBot.App.Areas.Auth.Controllers;

[Route("[controller]/[action]")]
public class AuthController(IConfiguration configuration) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = string.IsNullOrWhiteSpace(returnUrl) ? "/Admin" : returnUrl;
        return View();
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel viewModel, string? returnUrl = null)
    {
        var u = configuration["Auth:Username"] ?? "admin";
        var p = configuration["Auth:Password"] ?? string.Empty;

        if (!ModelState.IsValid)
            return View(viewModel);

        if (viewModel.Username == u && viewModel.Password == p)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, viewModel.Username),
                new(ClaimTypes.Role, "Admin")
            };
            var id = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(id);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/Admin" : returnUrl);
        }

        ModelState.AddModelError(string.Empty, "Неверный логин или пароль.");
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult Denied() => Content("Доступ запрещён");
}