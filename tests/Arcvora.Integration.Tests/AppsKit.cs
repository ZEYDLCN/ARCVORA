using System.Net;

namespace Arcvora.Integration.Tests;

/// <summary>Akış uygulaması testleri için ortak tanımlar ve kısayollar.</summary>
public static class AppsKit
{
    /// <summary>İzin akışı: yönetici onayı; <paramref name="gmGroup"/> verilirse 10 günden uzun izinlerde grup onayı da gerekir.</summary>
    public static object LeaveDef(Guid? gmGroup = null, string prefix = "IZN", bool extraAdminStep = false)
    {
        var s1 = new { id = "s1", name = "Yönetici onayı", approvers = new { type = "manager" } };
        var steps = new List<object> { s1 };
        if (gmGroup is { } g)
            steps.Add(new { id = "s2", name = "Genel müdür", approvers = new { type = "group", id = g }, conditions = new[] { new { field = "days", op = "gt", value = 10 } } });
        if (extraAdminStep)
            steps.Add(new { id = "s3", name = "Master admin", approvers = new { type = "admin" } });
        return new
        {
            schema = 1, prefix, summaryFields = new[] { "start", "end", "kind" },
            fields = new object[]
            {
                new { key = "start", label = "Başlangıç", type = "date", required = true },
                new { key = "end", label = "Bitiş", type = "date", required = true, notBefore = "start" },
                new { key = "days", label = "Gün", type = "number", computed = new { op = "calendar_days", from = "start", to = "end" } },
                new { key = "kind", label = "Tür", type = "select", required = true, options = new[] { "Yıllık", "Mazeret", "Ücretsiz" } },
                new { key = "proxy", label = "Vekil", type = "user" },
                new { key = "note", label = "Açıklama", type = "long_text", maxLength = 200 },
            },
            steps,
        };
    }

    /// <summary>Masraf akışı: tutar + açıklama, yönetici onayı, onay sonrası muhasebe kaydı.</summary>
    public static object ExpenseDef(string prefix = "MSR", string category = "masraf", bool accounting = true, decimal? approvalAbove = null)
    {
        var s1 = new { id = "s1", name = "Yönetici onayı", approvers = new { type = "manager" } };
        var conditional = approvalAbove is { } limit
            ? new { id = "s1", name = "Yönetici onayı", approvers = new { type = "manager" }, conditions = new[] { new { field = "amount", op = "gt", value = limit } } }
            : null;
        return new
        {
            schema = 1, prefix, summaryFields = new[] { "amount", "description" },
            fields = new object[]
            {
                new { key = "amount", label = "Tutar", type = "money", required = true },
                new { key = "description", label = "Açıklama", type = "text" },
                new { key = "owner", label = "İlgili kişi", type = "user" },
            },
            steps = new object[] { (object?)conditional ?? s1 },
            completion = accounting ? new { accounting = new { enabled = true, category } } : null,
        };
    }

    /// <summary>Fişli masraf akışı: tutar + ek alanı (en fazla <paramref name="maxFiles"/> dosya), yönetici onayı.</summary>
    public static object ReceiptDef(int maxFiles = 2, bool required = false, bool accounting = false) => new
    {
        schema = 1, prefix = "MSF", summaryFields = new[] { "amount" },
        fields = new object[]
        {
            new { key = "amount", label = "Tutar", type = "money", required = true },
            new { key = "receipt", label = "Fiş", type = "file", required, maxFiles },
        },
        steps = new object[] { new { id = "s1", name = "Yönetici onayı", approvers = new { type = "manager" } } },
        completion = accounting ? new { accounting = new { enabled = true, category = "masraf" } } : null,
    };

    public static object Leave(string start = "2026-11-10", string end = "2026-11-14", string kind = "Yıllık", Guid? proxy = null) =>
        proxy is null ? new { start, end, kind } : new { start, end, kind, proxy };

    public static Guid Id(this Resp r, string name = "id") => Guid.Parse(r.Str(name));

    public static async Task<Guid> CreateAppAsync(this Api admin, object? def = null, string name = "İzin Talebi")
    {
        var r = await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/apps", new { name, description = "Yıllık izin ve mazeret izni", icon = "calendar", definition = def ?? LeaveDef() });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        return Guid.Parse(r["summary"].GetProperty("id").GetString()!);
    }

    public static async Task PublishAsync(this Api admin, Guid appId)
    {
        var r = await admin.PostAsync($"/api/apps/{appId}/publish");
        Assert.True(r.Is(HttpStatusCode.OK), r.ToString());
    }

    public static async Task GrantAsync(this Api admin, Guid appId, Guid[]? users = null, Guid[]? groups = null)
    {
        var r = await admin.PutAsync($"/api/apps/{appId}/grants", new { userIds = users ?? [], groupIds = groups ?? [] });
        Assert.True(r.Is(HttpStatusCode.OK), r.ToString());
    }

    /// <summary>Taslak oluşturur, yayınlar ve verilen kullanıcılara yetki verir.</summary>
    public static async Task<Guid> PublishedAppAsync(this Api admin, object? def = null, string name = "İzin Talebi", params Guid[] users)
    {
        var id = await admin.CreateAppAsync(def, name);
        await admin.PublishAsync(id);
        if (users.Length > 0) await admin.GrantAsync(id, users);
        return id;
    }

    public static Task<Resp> SubmitAsync(this Api user, Guid appId, object data, string? key = null) =>
        user.PostAsync($"/api/apps/{appId}/requests", new { data }, key);

    public static async Task<Resp> NewGroupAsync(this Api admin, string name, params Guid[] members)
    {
        var g = await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/groups", new { name });
        Assert.True(g.Is(HttpStatusCode.Created), g.ToString());
        var set = await admin.PutAsync($"/api/workspaces/{admin.WorkspaceId}/groups/{g.Str("id")}/members", new { userIds = members });
        Assert.True(set.Is(HttpStatusCode.OK), set.ToString());
        return set;
    }

    public static async Task<Guid> MyPendingTaskAsync(this Api user, Guid requestId)
    {
        var detail = await user.GetAsync($"/api/requests/{requestId}");
        Assert.True(detail.Is(HttpStatusCode.OK), detail.ToString());
        return Guid.Parse(detail["myPendingTaskId"].GetString()!);
    }

    public static string[] EventKinds(this Resp detail) =>
        detail["events"].EnumerateArray().Select(e => e.GetProperty("kind").GetString()!).ToArray();
}
