using System.Net;
using System.Text.Json;

namespace Arcvora.Integration.Tests;

/// <summary>G6: ana veri / dinamik listeler — yönetim, formlarda kullanım, yayın kontrolü, silme koruması ve yetkiler.</summary>
public class DataListTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Url(Api a, string rest = "") => $"/api/workspaces/{a.WorkspaceId}/lists{rest}";

    private static async Task<string> NewList(Api admin, string name, params string[] items)
    {
        var r = await admin.PostAsync(Url(admin), new { name, items });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        return r.Str("id");
    }

    /// <summary>"dept" alanı verilen listeye bağlı tek adımlı uygulama.</summary>
    private static object ListDef(string listId, bool multi = false) => new
    {
        schema = 1, prefix = "LST", summaryFields = new[] { "dept" },
        fields = new object[] { new { key = "dept", label = "Birim", type = multi ? "multi_select" : "select", required = true, listId = Guid.Parse(listId) } },
        steps = new object[] { new { id = "s1", name = "Yönetici onayı", approvers = new { type = "manager" } } },
    };

    private async Task<(Api Admin, Api Boss, Api Emp)> PeopleAsync()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Müdür");
        var emp = await admin.NewUserAsync(factory, displayName: "Çalışan", managerId: boss.UserId);
        return (admin, boss, emp);
    }

    // =================================================================== yönetim

    [Fact]
    public async Task A_list_is_created_with_items_trimmed_in_order_and_listed_with_counts()
    {
        var (admin, _, _) = await PeopleAsync();
        var r = await admin.PostAsync(Url(admin), new { name = "  Birimler ", description = "Şirket birimleri", items = new[] { " Satış ", "Depo", "Muhasebe", "Satış" } });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        Assert.Equal("Birimler", r.Str("name"));
        Assert.Equal(["Satış", "Depo", "Muhasebe"], r["items"].EnumerateArray().Select(i => i.GetProperty("value").GetString()!).ToArray());       // tekrar atılır, sıra korunur
        Assert.All(r["items"].EnumerateArray(), i => Assert.True(i.GetProperty("active").GetBoolean()));

        var list = await admin.GetAsync(Url(admin));
        var row = list.Body.EnumerateArray().Single();
        Assert.Equal("Birimler", row.GetProperty("name").GetString());
        Assert.Equal(3, row.GetProperty("itemCount").GetInt32());
        Assert.Equal(3, row.GetProperty("activeCount").GetInt32());
        Assert.Equal(3, (await admin.GetAsync(Url(admin, "/" + r.Str("id"))))["items"].GetArrayLength());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_name_is_refused(string name)
    {
        var (admin, _, _) = await PeopleAsync();
        var r = await admin.PostAsync(Url(admin), new { name });
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        Assert.Equal("invalid_name", r.Str("code"));
    }

    [Fact]
    public async Task Names_are_unique_per_workspace_case_insensitively_and_other_workspaces_are_independent()
    {
        var (admin, _, _) = await PeopleAsync();
        await NewList(admin, "Birimler");
        var dup = await admin.PostAsync(Url(admin), new { name = " birimler " });
        Assert.True(dup.Is(HttpStatusCode.Conflict), dup.ToString());
        Assert.Equal("list_name_taken", dup.Str("code"));
        Assert.True((await admin.PostAsync(Url(admin), new { name = new string('x', 101) })).Is(HttpStatusCode.UnprocessableEntity));
        var other = await Api.RegisterAsync(factory);
        Assert.True((await other.PostAsync(Url(other), new { name = "Birimler" })).Is(HttpStatusCode.Created));
    }

    [Fact]
    public async Task Item_values_are_validated()
    {
        var (admin, _, _) = await PeopleAsync();
        var id = await NewList(admin, "Birimler");
        Assert.Equal("invalid_item", (await admin.PutAsync(Url(admin, $"/{id}/items"), new { items = new[] { new { value = "   " } } })).Str("code"));
        Assert.Equal("invalid_item", (await admin.PutAsync(Url(admin, $"/{id}/items"), new { items = new[] { new { value = new string('x', 101) } } })).Str("code"));
        Assert.Equal("too_many_items", (await admin.PutAsync(Url(admin, $"/{id}/items"), new { items = Enumerable.Range(0, 501).Select(i => new { value = "Öğe " + i }).ToArray() })).Str("code"));
        var control = await admin.PutAsync(Url(admin, $"/{id}/items"), new { items = new[] { new { value = "a\u0000b" } } });
        Assert.True(control.Is(HttpStatusCode.UnprocessableEntity), control.ToString());
        Assert.Equal(0, (await admin.GetAsync(Url(admin, "/" + id)))["items"].GetArrayLength());                      // hatalı istek hiçbir şeyi değiştirmedi
        var max = await admin.PutAsync(Url(admin, $"/{id}/items"), new { items = Enumerable.Range(0, 500).Select(i => new { value = "Öğe " + i }).ToArray() });
        Assert.True(max.Is(HttpStatusCode.OK), max.ToString());
        Assert.Equal(500, max["items"].GetArrayLength());
    }

    [Fact]
    public async Task Setting_items_keeps_ids_of_existing_values_and_supports_deactivate_reorder_and_removal()
    {
        var (admin, _, _) = await PeopleAsync();
        var id = await NewList(admin, "Birimler", "Satış", "Depo", "Muhasebe");
        var before = (await admin.GetAsync(Url(admin, "/" + id)))["items"].EnumerateArray().ToDictionary(i => i.GetProperty("value").GetString()!, i => i.GetProperty("id").GetString()!);

        var after = await admin.PutAsync(Url(admin, $"/{id}/items"), new
        {
            items = new object[]
            {
                new { id = before["Muhasebe"], value = "Muhasebe" },                         // öne alındı
                new { id = before["Satış"], value = "Satış ve Pazarlama" },                  // yeniden adlandırıldı (kimlik aynı)
                new { id = before["Depo"], value = "Depo", active = false },                 // pasif
                new { value = "Üretim" },                                                    // yeni
            },
        });
        Assert.True(after.Is(HttpStatusCode.OK), after.ToString());
        var items = after["items"].EnumerateArray().ToList();
        Assert.Equal(["Muhasebe", "Satış ve Pazarlama", "Depo", "Üretim"], items.Select(i => i.GetProperty("value").GetString()!).ToArray());
        Assert.Equal(before["Satış"], items[1].GetProperty("id").GetString());
        Assert.False(items[2].GetProperty("active").GetBoolean());
        Assert.Equal(3, (await admin.GetAsync(Url(admin))).Body[0].GetProperty("activeCount").GetInt32());

        var removed = await admin.PutAsync(Url(admin, $"/{id}/items"), new { items = new[] { new { id = before["Muhasebe"], value = "Muhasebe" } } });
        Assert.Equal(1, removed["items"].GetArrayLength());                                  // listede olmayanlar silinir
    }

    [Fact]
    public async Task Duplicate_values_after_trimming_or_case_folding_are_refused_when_ids_differ()
    {
        var (admin, _, _) = await PeopleAsync();
        var id = await NewList(admin, "Birimler");
        var r = await admin.PutAsync(Url(admin, $"/{id}/items"), new { items = new[] { new { value = "Satış" }, new { value = " SATIŞ " } } });
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        Assert.Equal("duplicate_item", r.Str("code"));
    }

    [Fact]
    public async Task Renaming_and_describing_a_list_works_and_keeps_names_unique()
    {
        var (admin, _, _) = await PeopleAsync();
        var a = await NewList(admin, "Birimler");
        await NewList(admin, "Şehirler");
        var ok = await admin.PatchAsync(Url(admin, "/" + a), new { name = "Departmanlar", description = "Yeni" });
        Assert.True(ok.Is(HttpStatusCode.OK), ok.ToString());
        Assert.Equal("Departmanlar", ok.Str("name"));
        Assert.Equal("list_name_taken", (await admin.PatchAsync(Url(admin, "/" + a), new { name = "şehirler" })).Str("code"));
        Assert.True((await admin.PatchAsync(Url(admin, "/" + a), new { name = "Departmanlar" })).Is(HttpStatusCode.OK));      // kendi adı çakışma sayılmaz
    }

    // ================================================================== yetkiler

    [Fact]
    public async Task Only_master_admins_manage_lists_and_workspaces_are_isolated()
    {
        var (admin, boss, emp) = await PeopleAsync();
        var id = await NewList(admin, "Birimler", "Satış");
        var outsider = await Api.RegisterAsync(factory);
        foreach (var who in new[] { boss, emp })
        {
            Assert.True((await who.GetAsync(Url(admin))).Is(HttpStatusCode.Forbidden));
            Assert.True((await who.PostAsync(Url(admin), new { name = "x" })).Is(HttpStatusCode.Forbidden));
            Assert.True((await who.PutAsync(Url(admin, $"/{id}/items"), new { items = Array.Empty<object>() })).Is(HttpStatusCode.Forbidden));
            Assert.True((await who.DeleteAsync(Url(admin, "/" + id))).Is(HttpStatusCode.Forbidden));
        }
        Assert.True((await outsider.GetAsync(Url(admin))).Is(HttpStatusCode.NotFound));
        Assert.True((await outsider.GetAsync(Url(admin, "/" + id))).Is(HttpStatusCode.NotFound));
        Assert.True((await outsider.GetAsync(Url(outsider, "/" + id))).Is(HttpStatusCode.NotFound));       // başka çalışma alanından kimlikle erişilemez
        Assert.True((await outsider.PutAsync(Url(outsider, $"/{id}/items"), new { items = Array.Empty<object>() })).Is(HttpStatusCode.NotFound));
        Assert.True((await outsider.DeleteAsync(Url(outsider, "/" + id))).Is(HttpStatusCode.NotFound));
        using var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Url(admin))).StatusCode);
    }

    // ===================================================== uygulama yayını ve formlar

    [Fact]
    public async Task A_form_with_a_list_field_offers_the_active_items_in_order()
    {
        var (admin, _, emp) = await PeopleAsync();
        var id = await NewList(admin, "Birimler", "Satış", "Depo", "Muhasebe");
        var items = (await admin.GetAsync(Url(admin, "/" + id)))["items"].EnumerateArray().ToList();
        await admin.PutAsync(Url(admin, $"/{id}/items"), new { items = new object[] { new { id = items[0].GetProperty("id").GetString(), value = "Satış" }, new { id = items[1].GetProperty("id").GetString(), value = "Depo", active = false }, new { id = items[2].GetProperty("id").GetString(), value = "Muhasebe" } } });
        var app = await admin.PublishedAppAsync(ListDef(id), "Birim talebi", emp.UserId);

        var form = await emp.GetAsync($"/api/apps/{app}/form");
        Assert.True(form.Is(HttpStatusCode.OK), form.ToString());
        var offered = form["lists"].GetProperty(id).EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Equal(["Satış", "Muhasebe"], offered);                                         // pasif öğe sunulmaz
        Assert.Equal(id, form["definition"].GetProperty("fields")[0].GetProperty("listId").GetString());
    }

    [Fact]
    public async Task Submitting_validates_against_the_current_active_items()
    {
        var (admin, boss, emp) = await PeopleAsync();
        var id = await NewList(admin, "Birimler", "Satış", "Depo");
        var app = await admin.PublishedAppAsync(ListDef(id), "Birim talebi", emp.UserId);

        var ok = await emp.SubmitAsync(app, new { dept = "Satış" });
        Assert.True(ok.Is(HttpStatusCode.Created), ok.ToString());
        Assert.Equal("Satış", ok["data"].GetProperty("dept").GetString());
        var bad = await emp.SubmitAsync(app, new { dept = "Finans" });
        Assert.True(bad.Is(HttpStatusCode.UnprocessableEntity), bad.ToString());
        Assert.Contains(bad["details"].EnumerateArray(), i => i.GetProperty("code").GetString() == "invalid_option");

        // Liste sonradan değişir: yeni öğe hemen kullanılabilir, çıkarılan/pasif öğe kullanılamaz; eski talep etkilenmez.
        var current = (await admin.GetAsync(Url(admin, "/" + id)))["items"].EnumerateArray().ToList();
        await admin.PutAsync(Url(admin, $"/{id}/items"), new { items = new object[] { new { id = current[0].GetProperty("id").GetString(), value = "Satış", active = false }, new { value = "Finans" } } });
        Assert.True((await emp.SubmitAsync(app, new { dept = "Finans" })).Is(HttpStatusCode.Created));
        Assert.True((await emp.SubmitAsync(app, new { dept = "Satış" })).Is(HttpStatusCode.UnprocessableEntity));
        var old = await emp.GetAsync($"/api/requests/{ok["summary"].GetProperty("id").GetString()}");
        Assert.Equal("Satış", old["data"].GetProperty("dept").GetString());
        Assert.NotNull(boss);
    }

    [Fact]
    public async Task Multi_select_fields_take_several_values_from_the_list()
    {
        var (admin, _, emp) = await PeopleAsync();
        var id = await NewList(admin, "Birimler", "Satış", "Depo", "Muhasebe");
        var app = await admin.PublishedAppAsync(ListDef(id, multi: true), "Çoklu", emp.UserId);
        var r = await emp.SubmitAsync(app, new { dept = new[] { "Satış", "Muhasebe", "Satış" } });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        Assert.Equal(["Satış", "Muhasebe"], r["data"].GetProperty("dept").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.True((await emp.SubmitAsync(app, new { dept = new[] { "Satış", "Yok" } })).Is(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task Publishing_refuses_a_list_that_does_not_exist_or_belongs_to_another_workspace()
    {
        var (admin, _, _) = await PeopleAsync();
        var other = await Api.RegisterAsync(factory);
        var foreign = await NewList(other, "Başkasının listesi", "x");

        foreach (var bad in new[] { Guid.NewGuid().ToString(), foreign })
        {
            var created = await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/apps", new { name = "Hatalı", definition = ListDef(bad) });
            Assert.True(created.Is(HttpStatusCode.Created), created.ToString());
            Assert.Contains(created["issues"].EnumerateArray(), i => i.GetProperty("code").GetString() == "unknown_list");
            var pub = await admin.PostAsync($"/api/apps/{created["summary"].GetProperty("id").GetString()}/publish");
            Assert.True(pub.Is(HttpStatusCode.UnprocessableEntity), pub.ToString());
        }
    }

    [Fact]
    public async Task A_list_is_deleted_only_when_no_app_uses_it_and_deleting_cleans_its_items()
    {
        var (admin, _, emp) = await PeopleAsync();
        var used = await NewList(admin, "Kullanılan", "A");
        var unused = await NewList(admin, "Kullanılmayan", "A", "B");
        var app = await admin.PublishedAppAsync(ListDef(used), "Uygulama", emp.UserId);

        var blocked = await admin.DeleteAsync(Url(admin, "/" + used));
        Assert.True(blocked.Is(HttpStatusCode.Conflict), blocked.ToString());
        Assert.Equal("list_in_use", blocked.Str("code"));
        Assert.Contains("Uygulama", blocked.ToString());                                       // hangi uygulamanın kullandığı söylenir
        Assert.True((await admin.DeleteAsync(Url(admin, "/" + unused))).Is(HttpStatusCode.NoContent));
        Assert.True((await admin.GetAsync(Url(admin, "/" + unused))).Is(HttpStatusCode.NotFound));
        Assert.True((await admin.DeleteAsync(Url(admin, "/" + unused))).Is(HttpStatusCode.NotFound));

        // Uygulama arşivlenmiş olsa da yayınlı sürümü listeye bağlıdır; taslakta kaldırılsa bile yayındaki sürüm korunur.
        await admin.PostAsync($"/api/apps/{app}/archive");
        Assert.True((await admin.DeleteAsync(Url(admin, "/" + used))).Is(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task A_deleted_or_emptied_list_makes_submissions_fail_clearly_instead_of_accepting_anything()
    {
        var (admin, _, emp) = await PeopleAsync();
        var id = await NewList(admin, "Birimler", "Satış");
        var app = await admin.PublishedAppAsync(ListDef(id), "Birim talebi", emp.UserId);
        await admin.PutAsync(Url(admin, $"/{id}/items"), new { items = Array.Empty<object>() });
        var r = await emp.SubmitAsync(app, new { dept = "Satış" });
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        var form = await emp.GetAsync($"/api/apps/{app}/form");
        Assert.Equal(0, form["lists"].GetProperty(id).GetArrayLength());                      // form boş liste sunar; kullanıcı sebebi görür
    }

    [Fact]
    public async Task Lists_are_recorded_in_the_audit_log()
    {
        var (admin, _, _) = await PeopleAsync();
        var id = await NewList(admin, "Birimler", "A");
        await admin.PutAsync(Url(admin, $"/{id}/items"), new { items = new[] { new { value = "B" } } });
        await admin.DeleteAsync(Url(admin, "/" + id));
        foreach (var a in new[] { "list.created", "list.items_set", "list.deleted" })
            Assert.Equal(1, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/audit?action={a}"))["total"].GetInt32());
    }
}
