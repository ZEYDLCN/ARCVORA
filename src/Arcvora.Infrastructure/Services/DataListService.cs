using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Arcvora.Infrastructure.Services;

public sealed record ListSummaryDto(Guid Id, string Name, string? Description, int ItemCount, int ActiveCount, DateTimeOffset UpdatedAt);
public sealed record ListItemDto(Guid Id, string Value, bool Active);
public sealed record ListDetailDto(Guid Id, string Name, string? Description, IReadOnlyList<ListItemDto> Items);
public sealed record CreateListRequest(string? Name, string? Description, IReadOnlyList<string>? Items);
public sealed record UpdateListRequest(string? Name, string? Description);
public sealed record ItemInput(Guid? Id, string? Value, bool? Active);
public sealed record SetItemsRequest(IReadOnlyList<ItemInput>? Items);

/// <summary>
/// Yönetilen listeler (ana veri). Seçim alanları bir listeye bağlanır; talep gönderilirken değer listenin O ANKİ aktif öğelerine göre doğrulanır
/// ve metin olarak talebe yazılır (liste sonradan değişse de eski talepler değişmez). Bir uygulamanın taslağı veya yayındaki sürümü kullanıyorsa liste silinemez.
/// </summary>
public sealed class DataListService(ArcvoraDbContext db, AppAccessService access, AuditLog audit, TimeProvider clock)
{
    public const int MaxItems = 500;
    public const int MaxValueLength = 100;

    // ================================================================== okuma

    public async Task<IReadOnlyList<ListSummaryDto>> ListAsync(Guid actorId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var rows = await db.DataLists.AsNoTracking().Where(l => l.WorkspaceId == workspaceId).OrderBy(l => l.Name).ToListAsync(ct);
        var counts = await db.DataListItems.AsNoTracking().Where(i => db.DataLists.Any(l => l.Id == i.ListId && l.WorkspaceId == workspaceId))
            .GroupBy(i => i.ListId).Select(g => new { g.Key, N = g.Count(), Active = g.Count(i => i.Active) }).ToDictionaryAsync(x => x.Key, ct);
        return rows.Select(l => new ListSummaryDto(l.Id, l.Name, l.Description, counts.GetValueOrDefault(l.Id)?.N ?? 0, counts.GetValueOrDefault(l.Id)?.Active ?? 0, l.UpdatedAt)).ToList();
    }

    public async Task<ListDetailDto> GetAsync(Guid actorId, Guid workspaceId, Guid listId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var list = await FindAsync(workspaceId, listId, false, ct);
        return await DetailAsync(list, ct);
    }

    /// <summary>Formlar için: verilen listelerin AKTİF öğeleri (sıralı). Çalışma alanında olmayan liste anahtar olarak yer almaz.</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ActiveItemsAsync(Guid workspaceId, IReadOnlyCollection<Guid> listIds, CancellationToken ct)
    {
        if (listIds.Count == 0) return new Dictionary<Guid, IReadOnlyList<string>>();
        var known = await db.DataLists.AsNoTracking().Where(l => l.WorkspaceId == workspaceId && listIds.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct);
        var items = await db.DataListItems.AsNoTracking().Where(i => known.Contains(i.ListId) && i.Active).OrderBy(i => i.Sort).Select(i => new { i.ListId, i.Value }).ToListAsync(ct);
        return known.ToDictionary(id => id, id => (IReadOnlyList<string>)items.Where(i => i.ListId == id).Select(i => i.Value).ToList());
    }

    // ============================================================== oluştur / düzenle

    public async Task<ListDetailDto> CreateAsync(Guid actorId, Guid workspaceId, CreateListRequest req, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var name = CleanName(req.Name);
        var description = CleanDescription(req.Description);
        var values = new List<string>();
        foreach (var raw in req.Items ?? [])
        {
            var v = CleanValue(raw);
            if (!values.Any(x => Fold(x) == Fold(v))) values.Add(v);                       // oluşturmada tekrarlar sessizce atılır
        }
        if (values.Count > MaxItems) throw new RequestValidationException("too_many_items", $"Bir listede en fazla {MaxItems} öğe olabilir.");

        var now = clock.GetUtcNow();
        var list = new DataList { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = name, NameKey = Fold(name), Description = description, CreatedAt = now, UpdatedAt = now };
        db.DataLists.Add(list);
        for (var i = 0; i < values.Count; i++) db.DataListItems.Add(new DataListItem { Id = Guid.NewGuid(), ListId = list.Id, Value = values[i], Active = true, Sort = i });
        audit.Add(workspaceId, null, actorId, "list.created", "list", list.Id.ToString(), new { name, items = values.Count });
        await SaveAsync(ct);
        return await DetailAsync(list, ct);
    }

    public async Task<ListDetailDto> UpdateAsync(Guid actorId, Guid workspaceId, Guid listId, UpdateListRequest req, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var list = await FindAsync(workspaceId, listId, true, ct);
        if (req.Name is not null) { list.Name = CleanName(req.Name); list.NameKey = Fold(list.Name); }
        if (req.Description is not null) list.Description = CleanDescription(req.Description);
        list.UpdatedAt = clock.GetUtcNow();
        audit.Add(workspaceId, null, actorId, "list.updated", "list", list.Id.ToString(), new { list.Name });
        await SaveAsync(ct);
        return await DetailAsync(list, ct);
    }

    /// <summary>
    /// Öğeleri toplu belirler: gönderilen sıra yeni sıradır; kimlikli öğe güncellenir (değer/aktiflik), kimliksiz yeni eklenir,
    /// gönderilmeyen mevcut öğeler silinir. Hata varsa hiçbir şey değişmez.
    /// </summary>
    public async Task<ListDetailDto> SetItemsAsync(Guid actorId, Guid workspaceId, Guid listId, SetItemsRequest req, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var list = await FindAsync(workspaceId, listId, true, ct);
        var input = req.Items ?? throw new RequestValidationException("invalid_request", "items gerekli.");
        if (input.Count > MaxItems) throw new RequestValidationException("too_many_items", $"Bir listede en fazla {MaxItems} öğe olabilir.");

        var values = input.Select(i => CleanValue(i.Value)).ToList();
        var dup = values.GroupBy(Fold).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new RequestValidationException("duplicate_item", $"“{dup.First()}” birden fazla kez var; öğeler benzersiz olmalı.");

        var existing = await db.DataListItems.Where(i => i.ListId == listId).ToDictionaryAsync(i => i.Id, ct);
        var keep = new HashSet<Guid>();
        for (var i = 0; i < input.Count; i++)
        {
            if (input[i].Id is { } id)
            {
                if (!existing.TryGetValue(id, out var item)) throw new RequestValidationException("unknown_item", "Öğe bu listede yok.");
                if (!keep.Add(id)) throw new RequestValidationException("duplicate_item", "Aynı öğe birden fazla kez gönderildi.");
                item.Value = values[i];
                item.Sort = i;
                if (input[i].Active is { } a) item.Active = a;
            }
            else db.DataListItems.Add(new DataListItem { Id = Guid.NewGuid(), ListId = listId, Value = values[i], Active = input[i].Active ?? true, Sort = i });
        }
        foreach (var gone in existing.Values.Where(e => !keep.Contains(e.Id))) db.DataListItems.Remove(gone);
        list.UpdatedAt = clock.GetUtcNow();
        audit.Add(workspaceId, null, actorId, "list.items_set", "list", listId.ToString(), new { items = input.Count });
        await SaveAsync(ct);
        return await DetailAsync(list, ct);
    }

    public async Task DeleteAsync(Guid actorId, Guid workspaceId, Guid listId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var list = await FindAsync(workspaceId, listId, true, ct);

        // Taslağı veya yayındaki sürümü bu listeyi kullanan uygulamalar (arşivlenmiş olsa da yayın sürümü listeye bağlıdır).
        var apps = await db.Apps.AsNoTracking().Where(a => a.WorkspaceId == workspaceId).ToListAsync(ct);
        var versionIds = apps.Where(a => a.PublishedVersionId != null).Select(a => a.PublishedVersionId!.Value).ToList();
        var published = await db.AppVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.DefinitionJson, ct);
        var users = apps.Where(a => FormValidator.ListIds(AppDesignService.Deserialize(a.DraftJson)).Contains(listId)
                                    || (a.PublishedVersionId is { } vid && published.TryGetValue(vid, out var json) && FormValidator.ListIds(AppDesignService.Deserialize(json)).Contains(listId)))
            .Select(a => a.Name).ToList();
        if (users.Count > 0) throw new ConflictException("list_in_use", $"Liste şu uygulamalarda kullanılıyor: {string.Join(", ", users)}. Önce uygulamalardan ayırın.");

        db.DataLists.Remove(list);
        audit.Add(workspaceId, null, actorId, "list.deleted", "list", listId.ToString(), new { list.Name });
        await db.SaveChangesAsync(ct);
    }

    // ================================================================== yardımcılar

    private async Task<DataList> FindAsync(Guid workspaceId, Guid listId, bool track, CancellationToken ct)
    {
        var q = track ? db.DataLists : db.DataLists.AsNoTracking();
        return await q.FirstOrDefaultAsync(l => l.Id == listId && l.WorkspaceId == workspaceId, ct) ?? throw new NotFoundException("Liste bulunamadı.");
    }

    private async Task<ListDetailDto> DetailAsync(DataList list, CancellationToken ct)
    {
        var items = await db.DataListItems.AsNoTracking().Where(i => i.ListId == list.Id).OrderBy(i => i.Sort).Select(i => new ListItemDto(i.Id, i.Value, i.Active)).ToListAsync(ct);
        return new ListDetailDto(list.Id, list.Name, list.Description, items);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("list_name_taken", "Bu adla bir liste zaten var.");
        }
    }

    private static string CleanName(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length is 0 or > 100 || s.Any(char.IsControl)) throw new RequestValidationException("invalid_name", "Liste adı 1–100 karakter olmalı.");
        return s;
    }

    private static string? CleanDescription(string? raw)
    {
        var s = raw?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (s.Length > 500 || s.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) throw new RequestValidationException("invalid_description", "Açıklama en fazla 500 karakter olabilir.");
        return s;
    }

    private static string CleanValue(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length is 0 or > MaxValueLength || s.Any(char.IsControl)) throw new RequestValidationException("invalid_item", $"Öğe 1–{MaxValueLength} karakter olmalı ve denetim karakteri içermemeli.");
        return s;
    }

    /// <summary>Türkçe duyarsız karşılaştırma anahtarı (İ/ı doğru eşlenir); kültür verisi gerektirmez.</summary>
    public static string Fold(string s) => s.Trim().Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant();
}
