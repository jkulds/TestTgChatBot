using System.ComponentModel.DataAnnotations;

namespace TestTgChatBot.Dao.Models;

public class UserProfile
{
    /// <summary>
    /// Telegram UserId (из Update.Message.From.Id)
    /// </summary>
    [Key]
    public long TelegramUserId { get; set; }

    /// <summary>
    /// Логин в Telegram
    /// </summary>
    [MaxLength(128)]
    public string? Username { get; set; }

    /// <summary>
    /// ФИО, которое вводит пользователь при первом запуске
    /// </summary>
    [Required, MaxLength(200)]
    public string? FullName { get; set; } = null!;
    
    public DateTime CreatedAt { get; set; }

    public ICollection<UserTest> UserTests { get; set; } = new List<UserTest>();
}