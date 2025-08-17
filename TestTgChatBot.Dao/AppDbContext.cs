using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using TestTgChatBot.Dao.Models;

namespace TestTgChatBot.Dao;

public class AppDbContext : DbContext
{
    static AppDbContext()
    {
        Batteries.Init();  
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite("Data Source=tests.db;Cache=Shared;Pooling=True");
        options.AddInterceptors(new SqlitePragmaInterceptor()); // NEW
    }

    public DbSet<Test> Tests => Set<Test>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<TestOption> Options => Set<TestOption>();
    public DbSet<UserTest> UserTests => Set<UserTest>();
    public DbSet<UserAnswer> UserAnswers => Set<UserAnswer>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfile>()
            .HasIndex(u => u.TelegramUserId)
            .IsUnique();

        modelBuilder.Entity<Test>()
            .HasIndex(t => new { t.IsActive, t.EndTime });
        
        modelBuilder.Entity<UserTest>()
            .HasIndex(ut => new { ut.UserProfileId, ut.TestId, ut.Status });
        modelBuilder.Entity<UserTest>()
            .HasIndex(ut => new { ut.TestId, ut.Status, ut.StartNotifiedAt });
        modelBuilder.Entity<UserTest>()
            .HasIndex(ut => ut.CreatedAt);

        modelBuilder.Entity<UserAnswer>()
            .HasIndex(ua => ua.UserTestId);
        modelBuilder.Entity<Question>()
            .HasIndex(q => q.TestId);

        modelBuilder.Entity<UserProfile>()
            .Property(u => u.FullName)
            .IsRequired()
            .HasMaxLength(200);

        modelBuilder.Entity<Question>()
            .HasOne(q => q.Test)
            .WithMany(t => t.Questions)
            .HasForeignKey(q => q.TestId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TestOption>()
            .HasOne(o => o.Question)
            .WithMany(q => q.Options)
            .HasForeignKey(o => o.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserAnswer>()
            .HasKey(ua => new { ua.Id });

        base.OnModelCreating(modelBuilder);
    }
} 