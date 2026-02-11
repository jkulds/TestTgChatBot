using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using TestTgChatBot.Business.Services;
using TestTgChatBot.Dao;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

var configuration = builder.Configuration;

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient("telegram")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 100,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var token = configuration["Telegram:BotToken"]
                ?? throw new InvalidOperationException("Missing Telegram:Token in configuration");

    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("telegram");

    var options = new TelegramBotClientOptions(token);
    return new TelegramBotClient(options, http);
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/Denied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.Cookie.Name = "QuestAdminAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddTransient<IExcelImportService, ExcelImportService>();
builder.Services.AddScoped<IExcelExportService, ExcelExportService>();
builder.Services.AddHostedService<TelegramBackgroundService>();

builder.WebHost.ConfigureKestrel(options => { options.ListenLocalhost(5000); });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapAreaControllerRoute(
    name: "admin",
    areaName: "Admin",
    pattern: "Admin/{controller=Tests}/{action=Index}/{id?}");

app.MapGet("/", async x =>
{
    x.Response.Redirect("Auth/Login");
    await Task.CompletedTask;
});

app.MapDefaultControllerRoute();

app.Run();