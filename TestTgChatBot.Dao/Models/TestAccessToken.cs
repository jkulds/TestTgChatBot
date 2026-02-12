using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TestTgChatBot.Dao.Models;

public class TestAccessToken
{
    [Key]
    public Guid Id { get; set; }

    [ForeignKey(nameof(Test))]
    public int TestId { get; set; }
    public Test Test { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? ExpiresAt { get; set; }

    public bool IsUsed { get; set; }

    [ForeignKey(nameof(UserTest))]
    public int? UsedByUserTestId { get; set; }
    public UserTest? UsedByUserTest { get; set; }
}
