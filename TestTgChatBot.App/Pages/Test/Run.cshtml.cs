using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TestTgChatBot.Dao;
using TestTgChatBot.Dao.Models;
using TestTgChatBot.Business.Services;

namespace TestTgChatBot.App.Pages.Test;

public class RunModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IWebTestService _webTestService;

    public RunModel(AppDbContext db, IWebTestService webTestService)
    {
        _db = db;
        _webTestService = webTestService;
    }

    public UserTest? UserTest { get; set; }
    public Question? CurrentQuestion { get; set; }
    public int CurrentQuestionIndex { get; set; }
    public int TotalQuestions { get; set; }
    public int? Score { get; set; }
    public DateTime? TestStartedAt { get; set; }
    public double TotalSecondsRemaining { get; set; }

    [BindProperty]
    public int CurrentQuestionId { get; set; }

    [BindProperty]
    public List<int> SelectedOptionIds { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!Request.Cookies.TryGetValue("CurrentTestId", out var testIdStr) || !int.TryParse(testIdStr, out var userTestId))
        {
            return RedirectToPage("/Error");
        }

        UserTest = await _db.UserTests
            .Include(ut => ut.Test)
            .ThenInclude(t => t.Questions)
            .ThenInclude(q => q.Options)
            .Include(ut => ut.UserAnswers)
            .FirstOrDefaultAsync(ut => ut.Id == userTestId);

        if (UserTest == null) return RedirectToPage("/Error");

        TestStartedAt = UserTest.StartedAt;

        // Check if test is active
        if (!UserTest.Test.IsActive && UserTest.Status != UserTestStatus.Finished)
        {
             await _webTestService.FinishTestAsync(UserTest.Id);
             UserTest = await _db.UserTests.FindAsync(UserTest.Id);
        }

        // Check total time limit (removed 30 min limit, but keeping finished check)
        if (UserTest.Status == UserTestStatus.Finished)
        {
             Score = UserTest?.Score;
             TotalQuestions = UserTest?.Test?.Questions?.Count ?? 0;
             CurrentQuestion = null; // Test finished
             
             // Clear cookie
             Response.Cookies.Delete("CurrentTestId");
             
             return Page();
        }

        // Randomize questions order based on UserTestId seed
        // Using a fixed seed (UserTest.Id) ensures the order is random but consistent for the same user session
        // This prevents questions from jumping around if the user refreshes the page
        var random = new Random(UserTest.Id);
        var questions = UserTest.Test.Questions.OrderBy(q => random.Next()).ToList();
        TotalQuestions = questions.Count;

        // Find first unanswered question
        var answeredIds = UserTest.UserAnswers.Select(ua => ua.QuestionId).ToHashSet();
        
        var question = questions.FirstOrDefault(q => !answeredIds.Contains(q.Id));
        
        if (question == null)
        {
            // All answered
            await _webTestService.FinishTestAsync(UserTest.Id);
            return RedirectToPage();
        }

        CurrentQuestion = question;
        CurrentQuestionIndex = questions.IndexOf(question);

        // No total time limit displayed
        TotalSecondsRemaining = 0;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Request.Cookies.TryGetValue("CurrentTestId", out var testIdStr) || !int.TryParse(testIdStr, out var userTestId))
        {
            return RedirectToPage("/Error");
        }

        // Save answer
        await _webTestService.SubmitAnswerAsync(userTestId, CurrentQuestionId, SelectedOptionIds);

        return RedirectToPage();
    }
}
