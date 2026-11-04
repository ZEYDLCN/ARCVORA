using System.Text.RegularExpressions;
using Arcvora.Core.Impact;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;

namespace Arcvora.Core.Ai;

/// <summary>
/// Anahtar sözcük kurallarına dayanan, YAPAY ZEKÂ OLMAYAN üretici. API anahtarı olmadan geliştirme, demo ve testte
/// uçtan uca akışın çalışması içindir; çıktısı bir başlangıç iskeletidir ve arayüzde "buluşsal" diye etiketlenir.
/// Kabul ölçütü, akış veya endpoint uydurmaz; boşluklar doğrulayıcı bulgularıyla görünür kalır.
/// </summary>
public sealed partial class HeuristicGenerator : IArchitectureGenerator
{
    public const string Label = "heuristic";
    public const string DisclaimerNote =
        "Bu taslak yapay zekâ ile değil, anahtar sözcük kurallarıyla üretildi; bir başlangıç iskeletidir ve dikkatle gözden geçirilmelidir.";

    public string Name => Label;
    public bool IsAi => false;

    private static readonly string[] Vague = ["hızlı", "kolay", "güvenli", "ölçeklenebilir", "performanslı", "esnek", "basit", "modern"];
    private static readonly string[] TechNames = ["asp.net", ".net", "spring", "react", "postgres", "angular", "vue", "node"];
    private static readonly string[] TenantMarkers = ["kiracı", "tenant", "her şirket", "birbirinin", "yalnızca kendi", "sadece kendi", "organizasyon"];
    private static readonly string[] IntegrationMarkers = ["muhasebe", "entegrasyon", "ödeme", "e-posta", "sms", "harici", "dış servis", "webhook", "erp"];
    private static readonly string[] FileMarkers = ["fiş", "dosya", "belge", "yükle", "görsel", "ek "];
    private static readonly string[] ApprovalMarkers = ["onay"];

    public Task<GeneratorOutput> ExtractRequirementsAsync(GenerationContext ctx, CancellationToken ct)
    {
        var analyzed = ctx.Model.Requirements.SelectMany(r => r.SourceRefs).Select(s => s.SourceId).ToHashSet();
        var pending = ctx.Model.Sources.Where(s => !analyzed.Contains(s.Id)).ToList();

        var reqs = new List<Requirement>();
        var questions = new List<OpenQuestion>();
        var n = 0;

        foreach (var src in pending)
        foreach (var raw in Sentences(src.Text))
        {
            var f = Fold(raw);
            var kind = Classify(f);
            if (kind is null) continue;

            var (k, priority) = kind.Value;
            reqs.Add(new Requirement
            {
                Id = $"new-{++n}", Text = Trim(raw), Kind = k, Priority = priority,
                Origin = Origin.UserStatement, ReviewStatus = ReviewStatus.Proposed,
                SourceRefs = [new SourceRef { SourceId = src.Id, Excerpt = raw.Trim() }],
            });

            if (k == RequirementKind.Quality && Vague.Any(f.Contains))
                questions.Add(Question($"new-q{questions.Count + 1}",
                    $"\"{Trim(raw)}\" ölçülebilir değil: kritik işlem, beklenen yük ve hedef değer nedir?", Priority.High, ctx.Model, questions));
        }

        var combined = string.Join(' ', reqs.Select(r => Fold(r.Text)));
        if (TenantMarkers.Any(combined.Contains))
            questions.Add(Question($"new-q{questions.Count + 1}", "Bir kullanıcı birden fazla şirkete/organizasyona üye olabilir mi?", Priority.High, ctx.Model, questions));
        if (ApprovalMarkers.Any(combined.Contains))
            questions.Add(Question($"new-q{questions.Count + 1}", "Onaylanan kayıt değiştirilebilir mi; düzeltme ayrı işlem mi olmalıdır?", Priority.High, ctx.Model, questions));
        if (IntegrationMarkers.Any(combined.Contains))
            questions.Add(Question($"new-q{questions.Count + 1}", "Dış servis çalışmazsa kayıt korunup işlem ertelenebilir mi?", Priority.High, ctx.Model, questions));

        // Her turda en fazla üç öncelikli soru (§8.3)
        var limited = questions.Where(q => q.Text.Length > 0).Take(3).ToList();

        var ops = reqs.Select(r => ModelPatch.Upsert(ObjectType.Requirement, r, "grp-requirements")).ToList();
        ops.AddRange(limited.Select(q => ModelPatch.Upsert(ObjectType.OpenQuestion, q, "grp-questions")));
        var patch = new ModelPatch
        {
            Summary = $"{reqs.Count} gereksinim adayı, {limited.Count} açık soru",
            Operations = ops,
            Groups =
            [
                new PatchGroup { Id = "grp-requirements", Title = "Gereksinim adayları" },
                new PatchGroup { Id = "grp-questions", Title = "Açık sorular" },
            ],
        };
        return Task.FromResult(new GeneratorOutput(patch, [], 0, 0, Label, DisclaimerNote));
    }

    public Task<GeneratorOutput> ProposeArchitectureAsync(GenerationContext ctx, CancellationToken ct)
    {
        // Kural tabanlı üretici her çağrıda aynı iskeleti YENİ kimliklerle üretir; kabul edilmiş bir mimari varken bu,
        // bileşenleri ve kararları ikiye katlar. Mevcut mimariye ekleme yapmak için "Yeni talep ekle" (değişiklik analizi) kullanılır.
        if (ctx.Model.Components.Count > 0)
            return Task.FromResult(new GeneratorOutput(new ModelPatch(), [], 0, 0, Label,
                "Modelde zaten kabul edilmiş bir mimari var; kural tabanlı üretici onu yeniden önermez. Yeni bir istek için “Yeni talep ekle” ile etki analizi başlatın."));

        var reqs = ctx.Model.Requirements.Where(r => r.ReviewStatus != ReviewStatus.Rejected).ToList();
        var ops = new List<PatchOperation>();
        var groups = new List<PatchGroup>();
        var existing = new ModelIndex(ctx.Model);
        var n = 0;
        string Id() => $"new-{++n}";
        var profileName = ctx.Profile.Name;

        List<string> Matching(string[] markers) =>
            reqs.Where(r => markers.Any(Fold(r.Text).Contains)).Select(r => r.Id).ToList();

        void Group(string id, string title, string description = "") =>
            groups.Add(new PatchGroup { Id = id, Title = title, Description = description });

        // --- çekirdek
        var web = Id(); var api = Id(); var db = Id();
        const string core = "grp-core";
        Group(core, "Çekirdek bileşenler ve ana yaklaşım");
        ops.Add(ModelPatch.Upsert(ObjectType.Component, new ArchComponent { Id = web, Name = "Web arayüzü", Kind = ComponentKind.Client, Responsibility = "Kullanıcı ekranları." }, core));
        ops.Add(ModelPatch.Upsert(ObjectType.Component, new ArchComponent
        {
            Id = api, Name = "Uygulama API", Kind = ComponentKind.Api, Responsibility = "HTTP girişi, kimlik doğrulama ve iş kuralları.",
            Technology = ctx.Profile.Id == ProfileCatalogIds.AspNetCore ? "ASP.NET Core Web API" : null,
        }, core));
        ops.Add(ModelPatch.Upsert(ObjectType.Component, new ArchComponent
        {
            Id = db, Name = "Uygulama veritabanı", Kind = ComponentKind.Database, Responsibility = "İşlemsel veri deposu.",
            Technology = ctx.Profile.Id == ProfileCatalogIds.AspNetCore ? "PostgreSQL" : null,
        }, core));
        ops.Add(ModelPatch.Upsert(ObjectType.Relationship, new Relationship { Id = Id(), SourceId = web, TargetId = api, Communication = CommunicationKind.Sync, Description = "HTTPS / JSON" }, core));
        ops.Add(ModelPatch.Upsert(ObjectType.Relationship, new Relationship { Id = Id(), SourceId = api, TargetId = db, Communication = CommunicationKind.Data, Description = "Uygulama verisi" }, core));

        var teamReqs = reqs.Where(r => r.Kind is RequirementKind.ExistingDecision or RequirementKind.Constraint
                                       && !TenantMarkers.Any(Fold(r.Text).Contains)).Select(r => r.Id).ToList();
        var monolith = Id();
        ops.Add(ModelPatch.Upsert(ObjectType.Decision, new Decision
        {
            Id = monolith, Title = "Modüler monolit ve gerektiğinde ayrı arka plan işleyicisi",
            Choice = $"Tek dağıtım birimi içinde modüller; uzun süren işler için ayrı işleyici ({profileName}).",
            Alternatives = [new Alternative { Name = "Mikroservisler", Tradeoff = "Dağıtım ve işlem koordinasyonu yükü; küçük ekip için kanıtlanmadan gerekçelendirilemez." }],
            Rationale = teamReqs.Count > 0 ? "Belirtilen ekip/teknoloji kısıtları altında dağıtımı sade tutmak." : "Ekip ve operasyon kapasitesi belirtilmedi; sade başlangıç varsayımı.",
            Consequences = "Modül sınırları kod disiplini ile korunur; ileride ayrıştırma mümkündür ama maliyetsiz değildir.",
        }, core));
        foreach (var r in teamReqs) ops.Add(Link(Id(), monolith, r, TraceKind.Addresses, core));
        ops.Add(Link(Id(), api, monolith, TraceKind.Implements, core));
        ops.Add(Link(Id(), db, monolith, TraceKind.Implements, core));
        if (teamReqs.Count == 0)
        {
            var asm = Id();
            ops.Add(ModelPatch.Upsert(ObjectType.Assumption, new Assumption
            {
                Id = asm, Text = "Ekip küçük ve operasyon kapasitesi sınırlı", Rationale = "Gereksinimlerde ekip büyüklüğü belirtilmedi.",
                Origin = Origin.Assumption,
            }, core));
            ops.Add(Link(Id(), monolith, asm, TraceKind.Assumes, core));
        }

        // --- kiracı izolasyonu
        var tenantReqs = Matching(TenantMarkers);
        if (tenantReqs.Count > 0)
        {
            const string g = "grp-tenant";
            Group(g, "Kiracı izolasyonu");
            var identity = Id(); var company = Id(); var user = Id(); var dec = Id();
            ops.Add(ModelPatch.Upsert(ObjectType.Component, new ArchComponent { Id = identity, Name = "Kimlik ve kiracı modülü", Kind = ComponentKind.Module, Responsibility = "Kullanıcı, kuruluş ve kiracı bağlamı." }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Relationship, new Relationship { Id = Id(), SourceId = api, TargetId = identity, Communication = CommunicationKind.Sync, Description = "Kiracı bağlamı çözümleme" }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Entity, new DataEntity
            {
                Id = company, Name = "Şirket", OwnerComponentId = identity,
                Fields = [new EntityField { Name = "id", Type = "uuid", IsKey = true }, new EntityField { Name = "ad" }],
                Relations = [new EntityRelation { TargetEntityId = user, Cardinality = Cardinality.OneToMany, Label = "üyeleri" }],
            }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Entity, new DataEntity
            {
                Id = user, Name = "Kullanıcı", OwnerComponentId = identity,
                Fields = [new EntityField { Name = "id", Type = "uuid", IsKey = true }, new EntityField { Name = "kiracı_id", Type = "uuid" }, new EntityField { Name = "e_posta" }],
            }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Decision, new Decision
            {
                Id = dec, Title = "Kiracı kimliği her istekte doğrulanır, veri erişimi kiracı kapsamıyla sınırlanır",
                Choice = "Ortak veritabanı, her kayıtta kiracı kimliği, sorgularda zorunlu kiracı filtresi.",
                Alternatives =
                [
                    new Alternative { Name = "Kiracı başına şema", Tradeoff = "Güçlü izolasyon; işletim ve geçiş maliyeti yüksek." },
                    new Alternative { Name = "Kiracı başına veritabanı", Tradeoff = "En güçlü izolasyon; küçük ekip için pahalı." },
                ],
                Rationale = "Bir sorgunun kapsam dışına çıkması veri sızıntısıdır.",
                Consequences = "Her sorgu kapsamdan geçmeli; kiracılar arası erişim testleri zorunlu.",
            }, g));
            foreach (var r in tenantReqs) ops.Add(Link(Id(), dec, r, TraceKind.Addresses, g));
            ops.Add(Link(Id(), identity, dec, TraceKind.Implements, g));
            ops.Add(Link(Id(), company, dec, TraceKind.Implements, g));
        }

        // --- dış sistem entegrasyonu
        var integrationReqs = Matching(IntegrationMarkers);
        if (integrationReqs.Count > 0)
        {
            const string g = "grp-integration";
            Group(g, "Dış sistem entegrasyonu");
            var worker = Id(); var external = Id(); var dispatch = Id(); var dec = Id(); var rel = Id(); var asm = Id();
            ops.Add(ModelPatch.Upsert(ObjectType.Component, new ArchComponent { Id = worker, Name = "Entegrasyon işleyicisi", Kind = ComponentKind.Worker, Responsibility = "Bekleyen dış gönderimleri alır ve gönderir.", Technology = ctx.Profile.Id == ProfileCatalogIds.AspNetCore ? "ASP.NET Core Worker Service" : null }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Component, new ArchComponent { Id = external, Name = "Dış sistem", Kind = ComponentKind.ExternalService, Responsibility = "Entegre edilecek harici servis." }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Relationship, new Relationship { Id = Id(), SourceId = worker, TargetId = db, Communication = CommunicationKind.Data, Description = "Bekleyen gönderimleri okur" }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Relationship, new Relationship
            {
                Id = rel, SourceId = worker, TargetId = external, Communication = CommunicationKind.Sync, Description = "Kaydı gönderir",
                FailureBehavior = "Başarısızlıkta gönderim niyeti korunur; sınırlı sayıda ve artan aralıkla yeniden denenir.",
            }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Entity, new DataEntity
            {
                Id = dispatch, Name = "Dış gönderim", OwnerComponentId = worker,
                Fields = [new EntityField { Name = "id", Type = "uuid", IsKey = true }, new EntityField { Name = "idempotency_anahtarı" }, new EntityField { Name = "durum" }, new EntityField { Name = "deneme_sayısı", Type = "int" }],
            }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Decision, new Decision
            {
                Id = dec, Title = "Dış sistem gönderimini kalıcı iş kaydıyla yürüt",
                Choice = "İşlem kaydı ile gönderim niyeti aynı veritabanı işleminde yazılır; işleyici bekleyen işi alıp idempotency anahtarıyla gönderir.",
                Alternatives = [new Alternative { Name = "İstek içinde doğrudan çağrı", Tradeoff = "Dış kesintide işlem veya kayıt kaybı riski." }],
                Rationale = "Dış servis kesintisinden sonra işlemi sürdürebilmek.",
                Consequences = "Gönderim eşzamanlı değildir; kullanıcıya bekleyen durum gösterilmelidir.",
            }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Assumption, new Assumption
            {
                Id = asm, Text = "Dış sistem idempotency anahtarını destekliyor", Rationale = "Dış API doğrulanmadı.", Origin = Origin.Assumption,
            }, g));
            foreach (var r in integrationReqs) ops.Add(Link(Id(), dec, r, TraceKind.Addresses, g));
            ops.Add(Link(Id(), worker, dec, TraceKind.Implements, g));
            ops.Add(Link(Id(), dispatch, dec, TraceKind.Implements, g));
            ops.Add(Link(Id(), rel, dec, TraceKind.Implements, g));
            ops.Add(Link(Id(), dec, asm, TraceKind.Assumes, g));
        }

        // --- dosyalar
        var fileReqs = Matching(FileMarkers);
        if (fileReqs.Count > 0)
        {
            const string g = "grp-files";
            Group(g, "Dosya saklama");
            var storage = Id(); var dec = Id();
            ops.Add(ModelPatch.Upsert(ObjectType.Component, new ArchComponent { Id = storage, Name = "Dosya depolama", Kind = ComponentKind.Storage, Responsibility = "Yüklenen dosyaların saklanması.", Technology = "Nesne depolama" }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Relationship, new Relationship { Id = Id(), SourceId = api, TargetId = storage, Communication = CommunicationKind.Data, Description = "Dosyalar" }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Decision, new Decision
            {
                Id = dec, Title = "Dosyalar nesne depolamada, veritabanında yalnızca referans",
                Choice = "Yüklenen dosya nesne depolamaya yazılır; kayıt yalnızca referansı tutar.",
                Rationale = "Büyük dosyaları ilişkisel veriden ayırmak.",
                Consequences = "Dosya erişimi de kiracı/yetki kapsamıyla sınırlanmalıdır.",
            }, g));
            foreach (var r in fileReqs) ops.Add(Link(Id(), dec, r, TraceKind.Addresses, g));
            ops.Add(Link(Id(), storage, dec, TraceKind.Implements, g));
        }

        // --- onay
        var approvalReqs = Matching(ApprovalMarkers);
        if (approvalReqs.Count > 0)
        {
            const string g = "grp-approval";
            Group(g, "Onay ve geçmiş");
            var module = Id(); var ev = Id(); var dec = Id();
            ops.Add(ModelPatch.Upsert(ObjectType.Component, new ArchComponent { Id = module, Name = "Onay modülü", Kind = ComponentKind.Module, Responsibility = "Onay/ret kararları ve onay geçmişi." }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Relationship, new Relationship { Id = Id(), SourceId = api, TargetId = module, Communication = CommunicationKind.Sync, Description = "Onay komutları" }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Relationship, new Relationship { Id = Id(), SourceId = module, TargetId = db, Communication = CommunicationKind.Data, Description = "Onay olayları" }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Entity, new DataEntity
            {
                Id = ev, Name = "Onay olayı", OwnerComponentId = module,
                Fields = [new EntityField { Name = "id", Type = "uuid", IsKey = true }, new EntityField { Name = "aktör_id", Type = "uuid" }, new EntityField { Name = "karar" }, new EntityField { Name = "zaman", Type = "timestamp" }],
                Constraints = ["yalnızca eklenir (append-only)"],
            }, g));
            ops.Add(ModelPatch.Upsert(ObjectType.Decision, new Decision
            {
                Id = dec, Title = "Onay olayları aktör ve zaman bilgisiyle yalnızca eklenerek kaydedilir",
                Choice = "Onay/ret olayları değiştirilemez kayıt olarak tutulur.",
                Rationale = "Onay geçmişinin sorgulanabilir olması.",
                Consequences = "Düzeltme ayrı bir olay olur.",
            }, g));
            foreach (var r in approvalReqs) ops.Add(Link(Id(), dec, r, TraceKind.Addresses, g));
            ops.Add(Link(Id(), module, dec, TraceKind.Implements, g));
            ops.Add(Link(Id(), ev, dec, TraceKind.Implements, g));
        }

        var patch = new ModelPatch
        {
            Summary = $"{ops.Count(o => o.Type is ObjectType.Component)} bileşen, {ops.Count(o => o.Type is ObjectType.Decision)} karar (buluşsal iskelet)",
            Operations = ops, Groups = groups,
        };
        _ = existing;
        return Task.FromResult(new GeneratorOutput(patch, [], 0, 0, Label, DisclaimerNote));
    }

    public Task<GeneratorOutput> AnalyzeChangeAsync(GenerationContext ctx, CancellationToken ct)
    {
        var request = (ctx.RequestText ?? "").Trim();
        var stems = Stems(request);
        var ops = new List<PatchOperation>();
        var groups = new List<PatchGroup>();

        // Talebi gereksinim adayı olarak kaydet (kaynak: talebin eklendiği Source).
        var source = ctx.Model.Sources.LastOrDefault(s => Normalize(s.Text) == Normalize(request));
        if (request.Length > 0)
        {
            ops.Add(ModelPatch.Upsert(ObjectType.Requirement, new Requirement
            {
                Id = "new-1", Text = Trim(request), Kind = RequirementKind.Functional, Priority = Priority.High,
                Origin = Origin.UserStatement, ReviewStatus = ReviewStatus.Proposed,
                SourceRefs = source is null ? [] : [new SourceRef { SourceId = source.Id, Excerpt = request }],
            }, "grp-request"));
            groups.Add(new PatchGroup
            {
                Id = "grp-request", Title = "Talep gereksinim olarak kaydedilir",
                Description = "Buluşsal sağlayıcı tasarım değişikliği üretmez; yalnızca talebi kaydeder ve olası etkileri işaretler.",
            });
        }

        var scored = new ModelIndex(ctx.Model).All
            .Where(o => o.Type is ObjectType.Requirement or ObjectType.Decision or ObjectType.Component or ObjectType.Entity
                        or ObjectType.Endpoint or ObjectType.Flow)
            .Select(o => (Obj: o, Hits: Stems(o.Title + " " + Describe(o)).Intersect(stems).ToList()))
            .Where(x => x.Hits.Count > 0)
            .OrderByDescending(x => x.Hits.Count).ThenBy(x => x.Obj.Id, StringComparer.Ordinal)
            .Take(8)
            .Select(x => new PossibleImpact(x.Obj.Id, $"Talepteki ifadelerle anahtar sözcük eşleşmesi: {string.Join(", ", x.Hits.Take(4))} (insan kontrolü gerekir)."))
            .ToList();

        var patch = new ModelPatch { Summary = "Talep kaydı ve olası etki işaretleri (buluşsal)", Operations = ops, Groups = groups };
        return Task.FromResult(new GeneratorOutput(patch, scored, 0, 0, Label,
            "Buluşsal sağlayıcı tasarım değişikliği önermez. Mimari değişiklik için LLM sağlayıcısını yapılandırın veya yapılandırılmış düzenleyiciyi kullanın."));
    }

    public Task<GeneratorOutput?> RepairAsync(GenerationTask task, GenerationContext ctx, GeneratorOutput previous,
        IReadOnlyList<string> problems, CancellationToken ct) => Task.FromResult<GeneratorOutput?>(null);

    // ------------------------------------------------------------------ yardımcılar

    private static PatchOperation Link(string id, string from, string to, TraceKind kind, string group) =>
        ModelPatch.Upsert(ObjectType.TraceLink, new TraceLink { Id = id, FromId = from, ToId = to, Kind = kind }, group);

    private static OpenQuestion Question(string id, string text, Priority impact, ArchitectureModel model, List<OpenQuestion> pending)
    {
        var duplicate = model.OpenQuestions.Any(q => Normalize(q.Text) == Normalize(text)) || pending.Any(q => Normalize(q.Text) == Normalize(text));
        return duplicate ? new OpenQuestion { Id = id, Text = "" } : new OpenQuestion { Id = id, Text = text, Impact = impact };
    }

    private static (RequirementKind, Priority)? Classify(string f)
    {
        var isContext = ContextRegex().IsMatch(f);
        if (isContext) return null;

        if (TechNames.Any(f.Contains) && f.Contains("kullan")) return (RequirementKind.ExistingDecision, Priority.Medium);
        if (TenantMarkers.Any(f.Contains)) return (RequirementKind.Constraint, Priority.Critical);
        if (new[] { "ekibiz", "ekip ", "bütçe", "hafta", "ay içinde", "kısıt" }.Any(f.Contains)) return (RequirementKind.Constraint, Priority.Medium);
        if (new[] { "hızlı", "güvenli", "ölçeklen", "performans", "erişilebilir", "kesinti", "yedek", "kolay", "dayanıklı", "kaybol" }.Any(f.Contains))
            return (RequirementKind.Quality, Priority.High);
        if (new[] { "ecek", "acak", "ebilsin", "abilsin", "malı", "meli", "yapabil", "girebil", "görebil", "yükle", "onayla", "gönder" }.Any(f.Contains))
            return (RequirementKind.Functional, Priority.High);
        return null;
    }

    [GeneratedRegex(@"(saas|uygulama|sistem|platform).*(geliştireceğiz|yapacağız|kuracağız|tasarlayacağız)")]
    private static partial Regex ContextRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+|\r?\n+")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"^\s*(?:[-*•]+|\d+[.)])\s*")]
    private static partial Regex Bullet();

    private static IEnumerable<string> Sentences(string text) =>
        SentenceSplit().Split(text).Select(s => Bullet().Replace(s, "").Trim()).Where(s => s.Length >= 12);

    private static readonly char[] StemSeparators = [' ', ',', '.', ';', ':', '(', ')', '"', '\'', '/', '-'];

    private static HashSet<string> Stems(string text) =>
        Fold(text).Split(StemSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 5).Select(w => w[..5]).ToHashSet();

    private static string Describe(IndexedObject o) => o.Value switch
    {
        Decision d => d.Choice + " " + d.Rationale,
        ArchComponent c => c.Responsibility,
        ApiEndpoint e => e.Summary,
        Flow f => f.Description,
        _ => "",
    };

    private static string Fold(string s) => s.Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant();
    private static string Normalize(string s) => string.Join(' ', Fold(s).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string Trim(string s) => s.Trim().TrimEnd('.', '!', '?', ' ');
}

internal static class ProfileCatalogIds
{
    public const string AspNetCore = "aspnetcore";
}
