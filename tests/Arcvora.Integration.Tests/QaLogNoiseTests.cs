using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Arcvora.Integration.Tests;

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public sealed record Entry(string Category, LogLevel Level, string Message);
    public ConcurrentQueue<Entry> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, Entries);
    public void Dispose() { }

    private sealed class Capture(string category, ConcurrentQueue<Entry> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(new Entry(category, logLevel, formatter(state, exception)));
    }
}

public sealed class LogCaptureFactory : ApiFactory
{
    public CapturingLoggerProvider Logs { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(l => l.AddProvider(Logs));
    }
}

/// <summary>
/// QA: beklenen çakışmalar (aynı e-posta/tatil/liste adı = kullanıcı hatası, 409) hata günlüğünü doldurmamalı. Her biri yığın izli bir
/// "fail:" satırı üretirse gerçek sorunlar bu gürültüde kaybolur. Beklenmeyen hatalar ise 500'de ayrıca günlüğe yazılır.
/// </summary>
public class QaLogNoiseTests(LogCaptureFactory factory) : IClassFixture<LogCaptureFactory>
{
    [Fact]
    public async Task Handled_conflicts_are_answered_with_409_and_leave_no_error_entries_in_the_log()
    {
        var admin = await Api.RegisterAsync(factory);
        var ws = $"/api/workspaces/{admin.WorkspaceId}";
        var email = $"ayni-{Guid.NewGuid():N}@example.com";

        Assert.True((await admin.CreateUserAsync(email)).Is(HttpStatusCode.Created));
        Assert.True((await admin.CreateUserAsync(email)).Is(HttpStatusCode.Conflict));                          // aynı e-posta

        Assert.True((await admin.PostAsync($"{ws}/holidays", new { date = "2027-05-19", name = "Gençlik Bayramı" })).Is(HttpStatusCode.Created));
        Assert.True((await admin.PostAsync($"{ws}/holidays", new { date = "2027-05-19", name = "Yine" })).Is(HttpStatusCode.Conflict));      // aynı gün

        Assert.True((await admin.PostAsync($"{ws}/lists", new { name = "Birimler", items = new[] { "A" } })).Is(HttpStatusCode.Created));
        Assert.True((await admin.PostAsync($"{ws}/lists", new { name = "birimler", items = new[] { "B" } })).Is(HttpStatusCode.Conflict));  // aynı liste adı

        var duplicateRegister = await new Api(factory.CreateClient()).PostAsync("/api/auth/register", new { email = admin.Email, password = "correct-horse-battery", displayName = "Kopya" });
        Assert.True(duplicateRegister.Is(HttpStatusCode.Conflict), duplicateRegister.ToString());                // aynı e-postayla kayıt

        var errors = factory.Logs.Entries.Where(e => e.Level >= LogLevel.Error).Select(e => $"{e.Category}: {e.Message}").ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Length > 200 ? e[..200] : e)));
    }
}
