using System.Net;
using Arcvora.Core.Ai;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcvora.Integration.Tests;

public class JobTests(ApiFactory factory) : IClassFixture<ApiFactory>, IDisposable
{
    private const string Idea = "Çalışanlar fiş yükleyip masraf girecek. Yönetici onaylayacak. Her şirket yalnızca kendi verisini görecek.";

    public void Dispose() => factory.Generator.Reset();

    private async Task Mutate(Guid jobId, Action<Job> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        var job = await db.Jobs.FirstAsync(j => j.Id == jobId);
        change(job);
        await db.SaveChangesAsync();
    }

    private async Task<Resp> Job(Api u, string id) => await u.GetAsync($"/api/jobs/{id}");

    private async Task<int> ProposalCount(Api u, Guid pid) => (await u.GetAsync($"/api/projects/{pid}/proposals")).Body.GetArrayLength();

    [Fact]
    public async Task Worker_crash_mid_job_is_recovered_when_the_lease_expires()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateProjectAsync(Idea);
        var job = await u.PostAsync($"/api/projects/{pid}/analyses");
        var jobId = Guid.Parse(job.Str("id"));

        // Bir worker işi aldı ve öldü: Running, geçmişte biten lease, 1 deneme harcanmış.
        await Mutate(jobId, j =>
        {
            j.Status = JobStatus.Running; j.LockedBy = "dead-worker"; j.Attempts = 1; j.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-5);
        });

        Assert.Equal(1, await factory.RunJobsAsync());
        var after = await Job(u, jobId.ToString());
        Assert.Equal("succeeded", after.Str("status"));
        Assert.Equal(2, after["attempts"].GetInt32());
        Assert.Equal(1, await ProposalCount(u, pid));         // yalnızca bir öneri: çift işleme yok
    }

    [Fact]
    public async Task A_live_lease_is_not_stolen_by_another_worker()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateProjectAsync(Idea);
        var job = await u.PostAsync($"/api/projects/{pid}/analyses");
        var jobId = Guid.Parse(job.Str("id"));
        await Mutate(jobId, j => { j.Status = JobStatus.Running; j.LockedBy = "alive-worker"; j.Attempts = 1; j.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(5); });

        Assert.Equal(0, await factory.RunJobsAsync());
        Assert.Equal("running", (await Job(u, jobId.ToString())).Str("status"));
    }

    [Fact]
    public async Task Job_that_keeps_crashing_fails_after_its_attempts_are_used_up()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateProjectAsync(Idea);
        var job = await u.PostAsync($"/api/projects/{pid}/analyses");
        var jobId = Guid.Parse(job.Str("id"));
        await Mutate(jobId, j => { j.Status = JobStatus.Running; j.LockedBy = "dead"; j.Attempts = j.MaxAttempts; j.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-1); });

        Assert.Equal(0, await factory.RunJobsAsync());
        var after = await Job(u, jobId.ToString());
        Assert.Equal("failed", after.Str("status"));
        Assert.Equal("lease_expired", after.Str("errorCode"));
        Assert.Equal(0, await ProposalCount(u, pid));
    }

    [Fact]
    public async Task Provider_error_fails_the_job_and_leaves_the_accepted_model_untouched()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateProjectAsync(Idea);
        var before = (await u.GetAsync($"/api/projects/{pid}/versions/1"))["info"].GetProperty("contentHash").GetString();
        factory.Generator.Current = new ThrowingGenerator(() => new LlmException("sağlayıcı yetkilendirme hatası"));

        var job = await u.PostAsync($"/api/projects/{pid}/analyses");
        await factory.RunJobsAsync();

        var after = await Job(u, job.Str("id"));
        Assert.Equal("failed", after.Str("status"));
        Assert.Equal("provider_error", after.Str("errorCode"));
        Assert.Equal(1, await u.HeadAsync(pid));
        Assert.Equal(before, (await u.GetAsync($"/api/projects/{pid}/versions/1"))["info"].GetProperty("contentHash").GetString());
        Assert.Equal(0, await ProposalCount(u, pid));
    }

    [Fact]
    public async Task Transient_provider_error_is_retried_with_backoff_then_succeeds()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateProjectAsync(Idea);
        factory.Generator.Current = new ThrowingGenerator(() => new LlmTransientException("429"));
        var job = await u.PostAsync($"/api/projects/{pid}/analyses");
        var jobId = Guid.Parse(job.Str("id"));

        Assert.Equal(1, await factory.RunJobsAsync());                 // ilk deneme: geçici hata -> kuyruğa geri
        var queued = await Job(u, jobId.ToString());
        Assert.Equal("queued", queued.Str("status"));
        Assert.Equal(1, queued["attempts"].GetInt32());
        Assert.Equal(0, await factory.RunJobsAsync());                 // bekleme süresi dolmadan tekrar alınmaz

        factory.Generator.Reset();
        await Mutate(jobId, j => j.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Equal(1, await factory.RunJobsAsync());
        var done = await Job(u, jobId.ToString());
        Assert.Equal("succeeded", done.Str("status"));
        Assert.Equal(2, done["attempts"].GetInt32());
    }

    [Fact]
    public async Task Persistent_transient_errors_stop_after_max_attempts()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateProjectAsync(Idea);
        factory.Generator.Current = new ThrowingGenerator(() => new LlmTransientException("503"));
        var job = await u.PostAsync($"/api/projects/{pid}/analyses");
        var jobId = Guid.Parse(job.Str("id"));

        for (var i = 0; i < 3; i++)
        {
            await factory.RunJobsAsync();
            await Mutate(jobId, j => j.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(-1));
        }
        var after = await Job(u, jobId.ToString());
        Assert.Equal("failed", after.Str("status"));
        Assert.Equal("retries_exhausted", after.Str("errorCode"));
        Assert.Equal(3, after["attempts"].GetInt32());
        Assert.Equal(1, await u.HeadAsync(pid));
    }

    [Fact]
    public async Task Invalid_llm_output_exhausts_the_repair_budget_and_reports_the_stage_and_problems()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var bad = new { summary = "kırık", relationships = new object[] { new { id = "new-1", sourceId = "CMP-001", targetId = "CMP-404" } } };
        factory.Generator.Current = new LlmArchitectureGenerator(new ScriptedLlm(bad, bad, bad, bad));

        var job = await u.PostAsync($"/api/projects/{pid}/change-requests", new { requestText = "Bildirim servisi eklensin" });
        await factory.RunJobsAsync();

        var after = await Job(u, job.Str("id"));
        Assert.Equal("failed", after.Str("status"));
        Assert.Equal("validate", after.Str("errorCode"));
        Assert.Contains("CMP-404", after["problems"][0].GetString());
        Assert.Equal(1, await u.HeadAsync(pid));
        Assert.Equal(1, await ProposalCount(u, pid));            // yalnızca örnekteki bekleyen öneri; başarısız iş öneri üretmedi
    }

    [Fact]
    public async Task Queued_job_can_be_cancelled_and_is_never_run()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateProjectAsync(Idea);
        var job = await u.PostAsync($"/api/projects/{pid}/analyses");
        var cancel = await u.PostAsync($"/api/jobs/{job.Str("id")}/cancel");
        Assert.Equal("cancelled", cancel.Str("status"));

        Assert.Equal(0, await factory.RunJobsAsync());
        Assert.Equal(0, await ProposalCount(u, pid));
    }

    [Fact]
    public async Task Cancelling_a_running_job_stops_it_and_its_result_is_never_applied()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateProjectAsync(Idea);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Generator.Current = new GateGenerator(started, release);

        var job = await u.PostAsync($"/api/projects/{pid}/analyses");
        var run = Task.Run(() => factory.RunJobsAsync());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("running", (await Job(u, job.Str("id"))).Str("status"));

        var cancel = await u.PostAsync($"/api/jobs/{job.Str("id")}/cancel");
        Assert.True(cancel["cancelRequested"].GetBoolean());

        // Yürüyen çağrı durdurulsa da durdurulmasa da sonuç projeye uygulanmaz.
        release.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(15));

        var after = await Job(u, job.Str("id"));
        Assert.Equal("cancelled", after.Str("status"));
        Assert.Equal(0, await ProposalCount(u, pid));
        Assert.Equal(1, await u.HeadAsync(pid));
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_same_job()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateProjectAsync(Idea);
        var key = Guid.NewGuid().ToString();
        var first = await u.PostAsync($"/api/projects/{pid}/analyses", null, key);
        var second = await u.PostAsync($"/api/projects/{pid}/analyses", null, key);
        Assert.True(first.Is(HttpStatusCode.Accepted), first.ToString());
        Assert.True(second.Is(HttpStatusCode.OK), second.ToString());
        Assert.Equal(first.Str("id"), second.Str("id"));
        Assert.Equal(1, (await u.GetAsync($"/api/projects/{pid}/jobs")).Body.GetArrayLength());

        // Eşzamanlı tekrar gönderim de tek iş üretir
        var key2 = Guid.NewGuid().ToString();
        var pid2 = await u.CreateProjectAsync(Idea);
        var burst = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => u.PostAsync($"/api/projects/{pid2}/analyses", null, key2)));
        Assert.Single(burst.Select(b => b.Str("id")).Distinct());
    }

    [Fact]
    public async Task Job_preconditions_are_validated_before_enqueueing()
    {
        var u = await Api.RegisterAsync(factory);
        var empty = await u.CreateProjectAsync();                                 // fikir metni yok
        var noSource = await u.PostAsync($"/api/projects/{empty}/analyses");
        Assert.True(noSource.Is(HttpStatusCode.UnprocessableEntity), noSource.ToString());
        Assert.Equal("no_pending_sources", noSource.Str("code"));

        var noReq = await u.PostAsync($"/api/projects/{empty}/architecture-proposals");
        Assert.Equal("no_requirements", noReq.Str("code"));

        var blank = await u.PostAsync($"/api/projects/{empty}/change-requests", new { requestText = "  " });
        Assert.Equal("empty_request", blank.Str("code"));
        var huge = await u.PostAsync($"/api/projects/{empty}/change-requests", new { requestText = new string('x', 9000) });
        Assert.Equal("request_too_long", huge.Str("code"));
    }

    [Fact]
    public async Task Number_of_active_jobs_per_project_is_bounded()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        for (var i = 0; i < 3; i++)
            Assert.True((await u.PostAsync($"/api/projects/{pid}/change-requests", new { requestText = $"Talep numarası {i}" })).Is(HttpStatusCode.Accepted));
        var over = await u.PostAsync($"/api/projects/{pid}/change-requests", new { requestText = "Fazladan talep" });
        Assert.True(over.Is(HttpStatusCode.Conflict), over.ToString());
        Assert.Equal("too_many_jobs", over.Str("code"));
        await factory.RunJobsAsync();                                           // kuyruğu boşalt
    }

    [Fact]
    public async Task Stage_is_visible_while_running_and_cleared_when_done()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateProjectAsync(Idea);
        var job = await u.PostAsync($"/api/projects/{pid}/analyses");
        await factory.RunJobsAsync();
        var done = await Job(u, job.Str("id"));
        Assert.Equal("succeeded", done.Str("status"));
        Assert.False(done.Body.TryGetProperty("stage", out var stage) && stage.ValueKind == System.Text.Json.JsonValueKind.String);
        Assert.False(string.IsNullOrEmpty(done.Str("proposalId")));
    }
}
