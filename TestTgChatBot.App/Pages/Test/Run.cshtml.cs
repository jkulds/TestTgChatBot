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

        // Check total time limit
        if (UserTest.Status == UserTestStatus.Finished || 
            (UserTest.StartedAt != null && (DateTime.UtcNow - UserTest.StartedAt.Value).TotalMinutes > 30))
        {
             if (UserTest.Status != UserTestStatus.Finished)
             {
                 await _webTestService.FinishTestAsync(UserTest.Id);
                 // Reload to get updated status
                 UserTest = await _db.UserTests.FindAsync(UserTest.Id);
             }
             
             Score = UserTest?.Score;
             TotalQuestions = UserTest?.Test?.Questions?.Count ?? 0;
             CurrentQuestion = null; // Test finished
             
             // Clear cookie
             Response.Cookies.Delete("CurrentTestId");
             
             return Page();
        }

        var questions = UserTest.Test.Questions.OrderBy(q => q.Id).ToList();
        TotalQuestions = questions.Count;

        // Find first unanswered question
        // Note: this simple logic assumes sequential answering. 
        // If we want random access, we need different logic. 
        // Requirement "1 minute per question" usually implies sequential flow.
        
        var answeredIds = UserTest.UserAnswers.Select(ua => ua.QuestionId).ToHashSet();
        
        // Find the first question that hasn't been answered
        var question = questions.FirstOrDefault(q => !answeredIds.Contains(q.Id));
        
        if (question == null)
        {
            // All answered
            await _webTestService.FinishTestAsync(UserTest.Id);
            // Redirect to self to show result
            return RedirectToPage();
        }

        CurrentQuestion = question;
        CurrentQuestion = question;
        CurrentQuestionIndex = questions.IndexOf(question);

        if (UserTest.StartedAt.HasValue)
        {
            var elapsed = DateTime.UtcNow - UserTest.StartedAt.Value;
            TotalSecondsRemaining = Math.Max(0, (30 * 60) - elapsed.TotalSeconds);
        }
        else
        {
            TotalSecondsRemaining = 30 * 60;
        }

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
