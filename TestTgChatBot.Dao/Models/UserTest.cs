using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TestTgChatBot.Dao.Models;

public enum UserTestStatus
{
    Pending = 0, // зарегистрирован, ждёт старта
    InProgress = 1, // идёт попытка
    Finished = 2 // завершено (нормально или админом)
}

public class UserTest
{
    [Key] public int Id { get; set; }

    [ForeignKey(nameof(UserProfile))] 
    public long UserProfileId { get; set; }

    public UserProfile UserProfile { get; set; } = null!;

    [ForeignKey(nameof(Test))] 
    public int TestId { get; set; }

    public Test Test { get; set; } = null!;

    /// <summary>
    /// Время старта прохождения теста
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Время окончания (если досрочно завершил или по таймауту)
    /// </summary>
    public DateTime? FinishedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Набрано баллов по итогам теста
    /// </summary>
    public int Score { get; set; }

    public UserTestStatus Status { get; set; } = UserTestStatus.Pending;

    /// <summary>
    /// Когда юзеру отправили приглашение «Начать»
    /// </summary>
    public DateTime? StartNotifiedAt { get; set; }

    public ICollection<UserAnswer> UserAnswers { get; set; } = new List<UserAnswer>();
}