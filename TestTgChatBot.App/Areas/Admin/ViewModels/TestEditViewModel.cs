using System.ComponentModel.DataAnnotations;

namespace TestTgChatBot.App.Areas.Admin.ViewModels
{
    public class TestEditViewModel
    {
        public int Id { get; set; }

        [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public List<QuestionEditViewModel>? Questions { get; set; } = [];
    }

    public class QuestionEditViewModel
    {
        public int Id { get; set; }
        [Required] public string Text { get; set; } = string.Empty;

        [Range(1, 3)] public int Weight { get; set; } = 1;

        public List<OptionEditViewModel>? Options { get; set; } = [];
        public int LocalId { get; set; }
    }

    public class OptionEditViewModel
    {
        public int Id { get; set; }
        [Required] public string Text { get; set; } = "";

        public bool IsCorrect { get; set; }
    }
}