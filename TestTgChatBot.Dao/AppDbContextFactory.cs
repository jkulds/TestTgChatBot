using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SQLitePCL;

namespace TestTgChatBot.Dao
{
    /// <summary>
    /// Эта фабрика будет вызываться dotnet-ef, чтобы получить DbContextOptions<AppDbContext>.
    /// </summary>
    public class AppDbContextFactory 
        : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            // Инициализируем SQLite-провайдер
            Batteries.Init();

            var builder = new DbContextOptionsBuilder<AppDbContext>();
            builder.UseSqlite("Data Source=tests.db");

            return new AppDbContext(builder.Options);
        }
    }
}