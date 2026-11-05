using System.Security.Cryptography;
using System.Text.Json;
using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Apps;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Demo;
using Arcvora.Infrastructure.Files;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Arcvora.Infrastructure.Services;

public sealed record DemoStatusDto(bool Present, int Users, int Groups, int Apps, int Requests, int Attachments);

public sealed record DemoUserDto(Guid Id, string Email, string DisplayName, string Title, string TemporaryPassword);

public sealed record DemoSeedDto(IReadOnlyList<DemoUserDto> Users, int Groups, int Apps, int Requests, int Attachments);

/// <summary>
/// Deneme/tanıtım verisi: gerçek veriyi yüklemeden önce sistemi denemek ve eğitmek için örnek kullanıcılar, grup, hazır şablonlardan
/// uygulamalar, her durumda talepler ve örnek ek dosyalar. Her şey "demo" işaretlidir; tek tuşla silinir ve gerçek veriye dokunulmaz.
/// Demo uygulamaları ayrı numara önekleri (DIZN, DAVN, DMSR) kullandığı için gerçek talep numaralarıyla karışmaz.
/// Gerçek kişiler/firmalar yoktur; tüm içerik uydurmadır ve e-posta alan adı ayrılmış ".example" uzantısındadır.
/// </summary>
public sealed class DemoDataService(ArcvoraDbContext db, AppAccessService access, IServiceScopeFactory scopes, PasswordHasher hasher,
    IFileStore store, AuditLog audit, TimeProvider clock)
{
    public const string Domain = "demo.arcvora.example";
    private static readonly string[] Prefixes = ["DIZN", "DAVN", "DMSR"];
    private const string PasswordAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    private sealed record Person(string Key, string Name, string Title, string? Manager);

    private static readonly Person[] People =
    [
        new("ayse", "Ayşe Kaya (demo)", "Genel Müdür", null),
        new("mehmet", "Mehmet Demir (demo)", "Satış Müdürü", "ayse"),
        new("elif", "Elif Şahin (demo)", "Muhasebe Uzmanı", "ayse"),
        new("can", "Can Yıldız (demo)", "Satış Temsilcisi", "mehmet"),
        new("zeynep", "Zeynep Arslan (demo)", "Satış Temsilcisi", "mehmet"),
        new("burak", "Burak Öztürk (demo)", "Depo Sorumlusu", "mehmet"),
    ];

    // ----------------------------------------------------------------- durum

    public async Task<DemoStatusDto> StatusAsync(Guid actorId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var appIds = db.Apps.Where(a => a.WorkspaceId == workspaceId && a.IsDemo).Select(a => a.Id);
        var users = await DemoUserIds(workspaceId).CountAsync(ct);
        var groups = await db.Groups.CountAsync(g => g.WorkspaceId == workspaceId && g.IsDemo, ct);
        var apps = await appIds.CountAsync(ct);
        var requests = await db.FlowRequests.CountAsync(r => appIds.Contains(r.AppId), ct);
        var files = await db.Attachments.CountAsync(a => appIds.Contains(a.AppId), ct);
        return new DemoStatusDto(users + groups + apps > 0, users, groups, apps, requests, files);
    }

    private IQueryable<Guid> DemoUserIds(Guid workspaceId) =>
        from m in db.Memberships
        join u in db.Users on m.UserId equals u.Id
        where m.WorkspaceId == workspaceId && u.IsDemo
        select u.Id;

    // ---------------------------------------------------------------- yükleme

    public async Task<DemoSeedDto> SeedAsync(Guid actorId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        if ((await StatusAsync(actorId, workspaceId, ct)).Present) throw Exists();

        var now = clock.GetUtcNow();
        var ids = People.ToDictionary(p => p.Key, _ => Guid.NewGuid());
        var passwords = People.ToDictionary(p => p.Key, _ => NewPassword());
        var group = new Group { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = "Muhasebe (demo)", IsDemo = true, CreatedAt = now };

        foreach (var p in People)
        {
            db.Users.Add(new User
            {
                Id = ids[p.Key], Email = EmailOf(p.Key, workspaceId), DisplayName = p.Name, PasswordHash = hasher.Hash(passwords[p.Key]),
                MustChangePassword = true, ManagedByWorkspaceId = workspaceId, IsDemo = true, CreatedAt = now,
            });
            db.Memberships.Add(new Membership
            {
                WorkspaceId = workspaceId, UserId = ids[p.Key], Role = WorkspaceRole.AppUser, CreatedAt = now,
                ManagerId = p.Manager is null ? null : ids[p.Manager],
            });
        }
        db.Groups.Add(group);
        db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = ids["elif"] });
        audit.Add(workspaceId, null, actorId, "demo.seeded", "workspace", workspaceId.ToString());
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw Exists();                  // eşzamanlı ikinci yükleme: e-posta benzersizliği yakalar
        }

        try
        {
            await BuildAsync(actorId, workspaceId, ids, group.Id, ct);
        }
        catch
        {
            // Yarım demo verisi bırakma: başarısızsa kurulanları geri al.
            db.ChangeTracker.Clear();
            await RemoveCoreAsync(workspaceId, CancellationToken.None);
            throw;
        }

        var status = await StatusAsync(actorId, workspaceId, ct);
        return new DemoSeedDto(People.Select(p => new DemoUserDto(ids[p.Key], EmailOf(p.Key, workspaceId), p.Name, p.Title, passwords[p.Key])).ToList(),
            status.Groups, status.Apps, status.Requests, status.Attachments);
    }

    /// <summary>E-posta tüm sistemde benzersizdir; çalışma alanı kimliğinin başı eklenerek farklı şirketlerin demo hesapları çakışmaz.</summary>
    private static string EmailOf(string key, Guid workspaceId) => $"demo.{key}.{workspaceId.ToString("N")[..8]}@{Domain}";

    private static ConflictException Exists() => new("demo_exists", "Bu çalışma alanında demo verisi zaten var. Önce silin.");

    private async Task BuildAsync(Guid adminId, Guid ws, Dictionary<string, Guid> u, Guid accountingGroup, CancellationToken ct)
    {
        // --- uygulamalar (hazır şablonlardan, demo önekiyle) ---------------------------------------------------------------------
        var appIds = new Dictionary<string, Guid>();
        foreach (var (key, prefix) in new[] { ("leave", "DIZN"), ("advance", "DAVN"), ("expense", "DMSR") })
        {
            var t = FlowTemplates.Get(key);
            await using var scope = scopes.CreateAsyncScope();
            var design = scope.ServiceProvider.GetRequiredService<AppDesignService>();
            var created = await design.CreateAsync(adminId, ws, new CreateAppRequest($"{t.Name} (demo)", t.Description, t.Icon, t.Definition with { Prefix = prefix }), ct);
            var appId = created.Summary.Id;
            await db.Apps.Where(a => a.Id == appId).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDemo, true), ct);
            await design.PublishAsync(adminId, appId, ct);
            await design.SetGrantsAsync(adminId, appId, new SetGrantsRequest(u.Values.ToList(), []), ct);
            appIds[key] = appId;
        }

        // Muhasebe grubu henüz seçilmediyse demo grubu kullanılır (kuyruğu görebilsin diye); temizlenirken geri alınır.
        var settings = await db.AccountingSettings.FirstOrDefaultAsync(s => s.WorkspaceId == ws, ct);
        if (settings is null) db.AccountingSettings.Add(new AccountingSettings { WorkspaceId = ws, AccountingGroupId = accountingGroup, UpdatedAt = clock.GetUtcNow() });
        else if (settings.AccountingGroupId is null) { settings.AccountingGroupId = accountingGroup; settings.UpdatedAt = clock.GetUtcNow(); }
        await db.SaveChangesAsync(ct);

        // --- talepler -------------------------------------------------------------------------------------------------------------
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        string D(int offset) => today.AddDays(offset).ToString("yyyy-MM-dd");
        var seededRequests = new List<(Guid Id, int DaysAgo)>();
        var n = 0;

        async Task<string> FileAsync(Guid user, Guid app, byte[] bytes, string name)
        {
            await using var scope = scopes.CreateAsyncScope();
            await using var ms = new MemoryStream(bytes);
            var up = await scope.ServiceProvider.GetRequiredService<AttachmentService>().UploadAsync(user, app, name, ms, bytes.Length, ct);
            return up.Id.ToString();
        }

        async Task<Guid> SubmitAsync(string who, string app, object data, int daysAgo)
        {
            await using var scope = scopes.CreateAsyncScope();
            var json = JsonSerializer.SerializeToElement(data);
            var dict = json.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var (detail, _) = await scope.ServiceProvider.GetRequiredService<RequestService>().SubmitAsync(u[who], appIds[app], dict, null, ct);
            seededRequests.Add((detail.Summary.Id, daysAgo));
            n++;
            return detail.Summary.Id;
        }

        async Task DecideAsync(string who, Guid request, ApprovalDecision decision, string? comment)
        {
            var taskId = await db.ApprovalTasks.AsNoTracking()
                .Where(t => t.RequestId == request && t.AssigneeId == (who == "admin" ? adminId : u[who]) && t.Status == ApprovalStatus.Pending)
                .Select(t => t.Id).FirstAsync(ct);
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RequestService>().DecideAsync(who == "admin" ? adminId : u[who], taskId, decision, comment, ct);
        }

        // İzin
        await SubmitAsync("can", "leave", new { kind = "Yıllık izin", start = D(9), end = D(11), note = "Aile ziyareti." }, 1);                       // Mehmet bekliyor
        var longLeave = await SubmitAsync("zeynep", "leave", new { kind = "Yıllık izin", start = D(20), end = D(31), note = "Yaz tatili." }, 3);   // 12 gün → önce Mehmet, sonra master admin
        await DecideAsync("mehmet", longLeave, ApprovalDecision.Approve, "Uygun.");
        var shortLeave = await SubmitAsync("burak", "leave", new { kind = "Mazeret izni", start = D(-9), end = D(-8), note = "Tapu işlemi." }, 5);
        await DecideAsync("mehmet", shortLeave, ApprovalDecision.Approve, "Geçmiş olsun, onaylandı.");
        var rejected = await SubmitAsync("can", "leave", new { kind = "Yıllık izin", start = D(-3), end = D(-1) }, 6);
        await DecideAsync("mehmet", rejected, ApprovalDecision.Reject, "Yoğun dönem; ay sonundan sonra tekrar iletin.");

        // Avans
        var advance = await SubmitAsync("zeynep", "advance", new { amount = 5000m, reason = "Müşteri ziyareti seyahat avansı", need_date = D(-2) }, 4);
        await DecideAsync("mehmet", advance, ApprovalDecision.Approve, null);
        var cancelled = await SubmitAsync("burak", "advance", new { amount = 2500m, reason = "Depo ekipmanı", need_date = D(5) }, 2);
        await using (var scope = scopes.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<RequestService>().CancelAsync(u["burak"], cancelled, ct);

        // Masraf (fiş ve fatura ekleriyle)
        var receipt1 = await FileAsync(u["can"], appIds["expense"], SampleFiles.ReceiptPng(1), "taksi-fisi.png");
        var invoice1 = await FileAsync(u["can"], appIds["expense"], SampleFiles.InvoicePdf("Örnek Otel Turizm A.Ş.", "FTR-2026-0042", [("Konaklama (2 gece)", 3200m), ("Kahvaltı", 450.5m)]), "otel-faturasi.pdf");
        await SubmitAsync("can", "expense", new { expense_date = D(-1), amount = 3650.5m, category = "Konaklama", description = "Müşteri toplantısı konaklaması", receipt_no = "FTR-2026-0042", receipt_file = new[] { receipt1, invoice1 } }, 1);

        var receipt2 = await FileAsync(u["zeynep"], appIds["expense"], SampleFiles.ReceiptPng(2), "yemek-fisi.png");
        var expenseDone = await SubmitAsync("zeynep", "expense", new { expense_date = D(-6), amount = 185.5m, category = "Yemek", description = "Müşteri öğle yemeği", receipt_no = "F-77", receipt_file = new[] { receipt2 } }, 7);
        await DecideAsync("mehmet", expenseDone, ApprovalDecision.Approve, "Fiş uygun.");

        var receipt3 = await FileAsync(u["mehmet"], appIds["expense"], SampleFiles.ReceiptPng(3), "otopark-fisi.png");
        await SubmitAsync("mehmet", "expense", new { expense_date = D(-2), amount = 640m, category = "Ulaşım", description = "Havalimanı otopark", receipt_file = new[] { receipt3 } }, 2);     // Ayşe bekliyor

        _ = n;
        await BackdateAsync(seededRequests, ct);
        await db.FlowRequests.Where(r => db.Apps.Any(a => a.Id == r.AppId && a.IsDemo)).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDemo, true), ct);
        await db.Attachments.Where(a => db.Apps.Any(x => x.Id == a.AppId && x.IsDemo)).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDemo, true), ct);
    }

    /// <summary>Talepleri gerçekçi görünsün diye geçmişe kaydırır (talep, görevler, zaman çizelgesi, muhasebe kaydı, ekler).</summary>
    private async Task BackdateAsync(IEnumerable<(Guid Id, int DaysAgo)> requests, CancellationToken ct)
    {
        foreach (var (id, days) in requests.Where(r => r.DaysAgo > 0))
        {
            var shift = TimeSpan.FromDays(days) + TimeSpan.FromMinutes(Math.Abs(id.GetHashCode() % 300));        // aynı gün içinde farklı saatler
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "FlowRequests" SET "CreatedAt" = "CreatedAt" - {shift}, "UpdatedAt" = "UpdatedAt" - {shift}, "CompletedAt" = "CompletedAt" - {shift} WHERE "Id" = {id};
                UPDATE "ApprovalTasks" SET "CreatedAt" = "CreatedAt" - {shift}, "DecidedAt" = "DecidedAt" - {shift} WHERE "RequestId" = {id};
                UPDATE "RequestEvents" SET "At" = "At" - {shift} WHERE "RequestId" = {id};
                UPDATE "AccountingEntries" SET "CreatedAt" = "CreatedAt" - {shift}, "NextAttemptAt" = "NextAttemptAt" - {shift} WHERE "RequestId" = {id};
                UPDATE "Attachments" SET "CreatedAt" = "CreatedAt" - {shift}, "LinkedAt" = "LinkedAt" - {shift} WHERE "RequestId" = {id};
                """, ct);
        }
    }

    // ----------------------------------------------------------------- silme

    public async Task RemoveAsync(Guid actorId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var demoApps = db.Apps.Where(a => a.WorkspaceId == workspaceId && a.IsDemo).Select(a => a.Id);
        var demoUsers = DemoUserIds(workspaceId);

        // Demo kişilere bağlı GERÇEK iş varsa silme: kısmi silme yok, açık bir hata var.
        var realRequests = await db.FlowRequests.CountAsync(r => r.WorkspaceId == workspaceId && !demoApps.Contains(r.AppId) && demoUsers.Contains(r.RequesterId), ct);
        var realTasks = await db.ApprovalTasks.CountAsync(t => t.WorkspaceId == workspaceId && t.Status == ApprovalStatus.Pending && demoUsers.Contains(t.AssigneeId)
            && db.FlowRequests.Any(r => r.Id == t.RequestId && !demoApps.Contains(r.AppId)), ct);
        if (realRequests + realTasks > 0)
            throw new ConflictException("demo_in_use",
                $"Demo kullanıcılarla açılmış {realRequests} gerçek talep veya onay bekleyen {realTasks} gerçek görev var. Önce onları sonuçlandırın.");

        await RemoveCoreAsync(workspaceId, ct);
        audit.Add(workspaceId, null, actorId, "demo.removed", "workspace", workspaceId.ToString());
        await db.SaveChangesAsync(ct);
    }

    private async Task RemoveCoreAsync(Guid ws, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var demoApps = await db.Apps.Where(a => a.WorkspaceId == ws && a.IsDemo).Select(a => a.Id).ToListAsync(ct);
        var demoUsers = await DemoUserIds(ws).ToListAsync(ct);
        var demoGroups = await db.Groups.Where(g => g.WorkspaceId == ws && g.IsDemo).Select(g => g.Id).ToListAsync(ct);
        var fileIds = await db.Attachments.Where(a => demoApps.Contains(a.AppId)).Select(a => a.Id).ToListAsync(ct);

        await db.Apps.Where(a => demoApps.Contains(a.Id)).ExecuteDeleteAsync(ct);          // talep, görev, olay, muhasebe kaydı, ek ve sürümler zincirleme gider
        await db.RequestCounters.Where(c => c.WorkspaceId == ws && Prefixes.Contains(c.Prefix)).ExecuteDeleteAsync(ct);
        await db.AccountingSettings.Where(s => s.WorkspaceId == ws && s.AccountingGroupId != null && demoGroups.Contains(s.AccountingGroupId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AccountingGroupId, (Guid?)null), ct);
        await db.AppGrants.Where(g => (g.Kind == GrantKinds.User && demoUsers.Contains(g.SubjectId)) || (g.Kind == GrantKinds.Group && demoGroups.Contains(g.SubjectId))).ExecuteDeleteAsync(ct);
        await db.Memberships.Where(m => m.WorkspaceId == ws && m.ManagerId != null && demoUsers.Contains(m.ManagerId.Value) && !demoUsers.Contains(m.UserId))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ManagerId, (Guid?)null), ct);        // gerçek çalışanın yöneticisi demo ise bağ çözülür
        await db.Groups.Where(g => demoGroups.Contains(g.Id)).ExecuteDeleteAsync(ct);
        await db.Memberships.Where(m => m.WorkspaceId == ws && demoUsers.Contains(m.UserId)).ExecuteDeleteAsync(ct);
        await db.Users.Where(x => demoUsers.Contains(x.Id) && !db.Memberships.Any(m => m.UserId == x.Id)).ExecuteDeleteAsync(ct);   // başka çalışma alanında üye değilse
        await tx.CommitAsync(ct);

        foreach (var id in fileIds) await store.DeleteAsync(id, CancellationToken.None);      // disk deposunda baytlar; veritabanı deposunda zaten gitti
    }

    private static string NewPassword()
    {
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++) chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        var s = new string(chars);
        return $"{s[..4]}-{s[4..8]}-{s[8..]}";
    }
}
