namespace TestTgChatBot.App.Areas.Admin.ViewModels;

public class TestsListViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int Questions { get; set; }
    public string TgTestLink { get; set; } = string.Empty;
}