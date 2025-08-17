using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TestTgChatBot.Dao.Models;

public class Question
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(Test))]
    public int TestId { get; set; }

    public Test Test { get; set; } = null!;

    [Required]
    [MaxLength(500)]
    public string Text { get; set; } = null!;

    /// <summary>
    /// Вес вопроса (баллы за правильный ответ)
    /// </summary>
    public int Weight { get; set; } = 1;

    public ICollection<TestOption> Options { get; set; } = new List<TestOption>();
    public ICollection<UserAnswer> UserAnswers { get; set; } = new List<UserAnswer>();
}