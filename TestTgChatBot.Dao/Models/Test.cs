using System.ComponentModel.DataAnnotations;

namespace TestTgChatBot.Dao.Models;

public class Test
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Время начала теста (если нужно запускать вручную)
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Время окончания теста (если нужно останавливать вручную)
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Пометка активно/неактивно
    /// </summary>
    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }
    public ICollection<Question> Questions { get; set; } = new List<Question>();
    public ICollection<UserTest> UserTests { get; set; } = new List<UserTest>();
}