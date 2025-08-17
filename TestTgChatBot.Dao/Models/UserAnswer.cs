using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TestTgChatBot.Dao.Models;

public class UserAnswer
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(UserTest))]
    public int UserTestId { get; set; }

    public UserTest UserTest { get; set; } = null!;

    [ForeignKey(nameof(Question))]
    public int QuestionId { get; set; }

    public Question Question { get; set; } = null!;

    /// <summary>
    /// Выбранный вариант ответа (или null, если не успел ответить)
    /// </summary>
    [ForeignKey(nameof(SelectedOption))]
    public int? SelectedOptionId { get; set; }

    public TestOption? SelectedOption { get; set; }

    /// <summary>
    /// Время, когда пользователь прислал ответ (для контроля таймаута)
    /// </summary>
    public DateTime AnsweredAt { get; set; }
}