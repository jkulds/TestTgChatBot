using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TestTgChatBot.Dao.Models;

public class TestOption
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(Question))]
    public int QuestionId { get; set; }

    public Question Question { get; set; } = null!;

    [Required]
    [MaxLength(64)]
    public string Text { get; set; } = null!;

    /// <summary>
    /// Является ли этот вариант правильным
    /// </summary>
    public bool IsCorrect { get; set; }
}