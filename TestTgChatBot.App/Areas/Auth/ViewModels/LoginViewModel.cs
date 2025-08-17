using System.ComponentModel.DataAnnotations;

namespace TestTgChatBot.App.Areas.Auth.ViewModels;

public class LoginViewModel
{
    [Required]
    public string Username { get; set; } = string.Empty;
    [Required]
    public string Password { get; set; } = string.Empty;
}