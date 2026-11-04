using Arcvora.Core.Impact;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;

namespace Arcvora.Core.Samples;

/// <summary>
/// Elle hazırlanmış örnek proje (§9): şirket masraf yönetimi. Bu bir AI çıktısı değildir; görünümlerin,
/// devir belgesinin ve değişiklik etki motorunun deterministik referans girdisidir.
/// Örnek çözümler dış muhasebe API'sinin idempotency desteği gibi koşullar doğrulanmadan eksiksiz sayılmaz.
/// </summary>
public static class ExpenseSample
{
    public const string ProjectName = "Masraf Yönetimi (örnek)";
    public const string ChangeRequestText = "Artık şirketler kendi iki aşamalı onay sürecini tanımlayabilsin.";

    private const string InputText =
        "Şirketlerin çalışan masraflarını yönettiği bir SaaS geliştireceğiz. Çalışanlar fiş yükleyip masraf girecek. " +
        "Yönetici onaylayacak. Onaylanan masraf muhasebe sistemine gönderilecek. Her şirket yalnızca kendi verisini görecek. " +
        "ASP.NET Core kullanacağız. Küçük bir ekibiz.";

    private static Requirement Req(string id, string text, RequirementKind kind, Priority pri, Origin origin, string? excerpt,
        params (string Id, string Text)[] criteria) => new()
    {
        Id = id, Text = text, Kind = kind, Priority = pri, Origin = origin, ReviewStatus = ReviewStatus.Confirmed,
        SourceRefs = excerpt is null ? [] : [new SourceRef { SourceId = "SRC-001", Excerpt = excerpt }],
        AcceptanceCriteria = criteria.Select(c => new AcceptanceCriterion { Id = c.Id, Text = c.Text }).ToList(),
    };

    private static ArchComponent Cmp(string id, string name, ComponentKind kind, string resp, string? tech) => new()
    {
        Id = id, Name = name, Kind = kind, Responsibility = resp, Technology = tech, ReviewStatus = ReviewStatus.Confirmed,
    };

    private static Relationship Rel(string id, string s, string t, CommunicationKind c, string desc, string? failure = null) => new()
    {
        Id = id, SourceId = s, TargetId = t, Communication = c, Description = desc, FailureBehavior = failure,
        ReviewStatus = ReviewStatus.Confirmed,
    };

    private static TraceLink Trace(string id, string from, string to, TraceKind kind) => new()
    {
        Id = id, FromId = from, ToId = to, Kind = kind, ReviewStatus = ReviewStatus.Confirmed,
    };

    private static EntityField F(string name, string type = "string", bool key = false, bool nullable = false, string? notes = null) =>
        new() { Name = name, Type = type, IsKey = key, IsNullable = nullable, Notes = notes };

    public static ArchitectureModel Model()
    {
        var model = new ArchitectureModel
        {
            Project = new ProjectInfo
            {
                Name = ProjectName,
                Description = "Şirketlerin çalışan masraflarını yönettiği çok kiracılı bir SaaS. Çalışan masraf girer, yönetici onaylar, onaylanan masraf muhasebe sistemine gönderilir.",
                TechProfileId = "aspnetcore",
            },
            Sources = [new Source { Id = "SRC-001", Title = "İlk proje notu", Text = InputText }],
            Actors =
            [
                new Actor { Id = "ACT-001", Name = "Çalışan", Description = "Masraf girer ve fiş yükler.", ReviewStatus = ReviewStatus.Confirmed },
                new Actor { Id = "ACT-002", Name = "Yönetici", Description = "Masrafı onaylar veya reddeder.", ReviewStatus = ReviewStatus.Confirmed },
            ],
            Requirements =
            [
                Req("REQ-001", "Şirketler birbirinin kayıtlarını görememeli", RequirementKind.Constraint, Priority.Critical,
                    Origin.UserStatement, "Her şirket yalnızca kendi verisini görecek.",
                    ("AC-001", "Başka kiracıya ait kayda doğrudan ID ile erişim reddedilir")),
                Req("REQ-002", "Muhasebe kesintisinde onaylanmış masraf kaybolmamalı", RequirementKind.Quality, Priority.High,
                    Origin.SystemProposal, "Onaylanan masraf muhasebe sistemine gönderilecek.",
                    ("AC-002", "Kesinti sonrası bekleyen gönderim işlenebilir")),
                Req("REQ-003", "Tekrarlanan gönderim çift masraf oluşturmamalı", RequirementKind.Quality, Priority.High,
                    Origin.SystemProposal, null,
                    ("AC-003", "Aynı işlem tekrarlandığında iş etkisi yinelenmez")),
                Req("REQ-004", "Onay geçmişi görülebilmeli", RequirementKind.Functional, Priority.Medium,
                    Origin.SystemProposal, "Yönetici onaylayacak.",
                    ("AC-004", "Geçmiş onay adımları sorgulanabilir")),
                Req("REQ-005", "Çalışanlar fiş yükleyip masraf girebilmeli", RequirementKind.Functional, Priority.High,
                    Origin.UserStatement, "Çalışanlar fiş yükleyip masraf girecek.",
                    ("AC-005", "Çalışan oluşturduğu masrafa fiş ekleyebilir")),
                Req("REQ-006", "Yönetici masrafı onaylayabilmeli veya reddedebilmeli", RequirementKind.Functional, Priority.High,
                    Origin.UserStatement, "Yönetici onaylayacak.",
                    ("AC-006", "Yönetici onay veya ret kararını kaydedebilir")),
                Req("REQ-007", "Onaylanan masraf muhasebe sistemine gönderilmeli", RequirementKind.Functional, Priority.High,
                    Origin.UserStatement, "Onaylanan masraf muhasebe sistemine gönderilecek.",
                    ("AC-007", "Onaylı masraf muhasebe servisine iletilir ve gönderim durumu görülebilir")),
                Req("REQ-008", "Çözüm ASP.NET Core ile geliştirilecek", RequirementKind.ExistingDecision, Priority.Medium,
                    Origin.UserStatement, "ASP.NET Core kullanacağız."),
                Req("REQ-009", "Küçük bir ekip tarafından işletilebilir olmalı", RequirementKind.Constraint, Priority.Medium,
                    Origin.UserStatement, "Küçük bir ekibiz."),
            ],
            Assumptions =
            [
                new Assumption
                {
                    Id = "ASM-001", Text = "Muhasebe servisi idempotency anahtarını destekliyor",
                    Rationale = "Tekrarlanan gönderimde çift kayıt oluşmaması buna dayanıyor; dış API doğrulanmadı.",
                    Status = AssumptionStatus.Unverified, ReviewStatus = ReviewStatus.Confirmed,
                },
                new Assumption
                {
                    Id = "ASM-002", Text = "Bir kullanıcı yalnızca tek bir şirkete üyedir",
                    Rationale = "Kiracı kimliği kullanıcı kaydında tutulabilir; çoklu üyelik cevabı bekleniyor.",
                    Status = AssumptionStatus.Unverified, ReviewStatus = ReviewStatus.Confirmed,
                },
            ],
            OpenQuestions =
            [
                new OpenQuestion { Id = "OQ-001", Text = "Bir kullanıcı birden fazla şirkete üye olabilir mi?", Impact = Priority.High, ReviewStatus = ReviewStatus.Confirmed },
                new OpenQuestion { Id = "OQ-002", Text = "Onaylanan masraf değiştirilebilir mi; düzeltme ayrı işlem mi olmalıdır?", Impact = Priority.High, Status = QuestionStatus.Deferred, ReviewStatus = ReviewStatus.Confirmed },
                new OpenQuestion
                {
                    Id = "OQ-003", Text = "Muhasebe servisi çalışmazsa masraf kaydı korunup gönderim ertelenebilir mi?", Impact = Priority.High,
                    Status = QuestionStatus.Answered, Answer = "Evet. Kayıt korunur, gönderim sonra yeniden denenir.", ReviewStatus = ReviewStatus.Confirmed,
                },
            ],
            Decisions =
            [
                new Decision
                {
                    Id = "DEC-001", Title = "Modüler monolit ve ayrı bir arka plan işleyicisi",
                    Choice = "Tek dağıtım birimi içinde modüller (masraf, onay, kimlik) ve uzun işler için ayrı worker süreci.",
                    Alternatives =
                    [
                        new Alternative { Name = "Mikroservisler", Tradeoff = "Küçük ekip için dağıtım ve işlem koordinasyonu yükü yüksek." },
                        new Alternative { Name = "Tek süreç, işleyicisiz", Tradeoff = "Muhasebe kesintisi kullanıcı isteğini etkiler." },
                    ],
                    Rationale = "Küçük ekip ve ASP.NET Core kısıtı; dağıtımı sade tutmak.",
                    Consequences = "Modül sınırları kod disiplini ile korunmalı; ileride ayrıştırma mümkün ama bedava değil.",
                    ReviewStatus = ReviewStatus.Confirmed,
                },
                new Decision
                {
                    Id = "DEC-002", Title = "Kiracı kimliği her istekte doğrulanır, veri erişimi kiracı kapsamıyla sınırlanır",
                    Choice = "Ortak veritabanı, her kayıtta kiracı kimliği, sorgularda zorunlu kiracı filtresi.",
                    Alternatives =
                    [
                        new Alternative { Name = "Kiracı başına şema", Tradeoff = "İzolasyon güçlü, işletim ve geçiş maliyeti yüksek." },
                        new Alternative { Name = "Kiracı başına veritabanı", Tradeoff = "En güçlü izolasyon, küçük ekip için pahalı." },
                    ],
                    Rationale = "REQ-001 kritik: bir sorgunun kapsam dışına çıkması veri sızıntısıdır.",
                    Consequences = "Her sorgu kapsamdan geçmeli; kiracılar arası erişim testleri zorunlu.",
                    ReviewStatus = ReviewStatus.Confirmed,
                },
                new Decision
                {
                    Id = "DEC-003", Title = "Muhasebe gönderimini kalıcı iş kaydıyla yürüt",
                    Choice = "Onay ile gönderim niyeti aynı veritabanı işleminde yazılır; işleyici bekleyen işi alıp gönderir.",
                    Alternatives = [new Alternative { Name = "Onay isteğinde doğrudan çağrı", Tradeoff = "Muhasebe kesintisinde onay veya kayıt kaybı riski." }],
                    Rationale = "Dış servis kesintisinden sonra işlemi sürdürebilmek (OQ-003 cevabı).",
                    Consequences = "Gönderim eşzamanlı değil; kullanıcıya 'gönderim bekliyor' durumu gösterilmeli.",
                    ReviewStatus = ReviewStatus.Confirmed,
                },
                new Decision
                {
                    Id = "DEC-004", Title = "Entegrasyonda idempotency anahtarı kullan",
                    Choice = "Her gönderim niyetine kararlı bir anahtar; muhasebe çağrısı bu anahtarla yapılır.",
                    Rationale = "Yeniden deneme çift masraf oluşturmamalı (REQ-003).",
                    Consequences = "Muhasebe API'si anahtarı desteklemiyorsa uygulama tarafında tekrar işleme kontrolü gerekir (ASM-001).",
                    ReviewStatus = ReviewStatus.Confirmed,
                },
                new Decision
                {
                    Id = "DEC-005", Title = "Onay olayları aktör ve zaman bilgisiyle yalnızca eklenerek kaydedilir",
                    Choice = "Onay/ret olayları değiştirilemez kayıt olarak tutulur.",
                    Rationale = "Onay geçmişi sorgulanabilir olmalı (REQ-004).",
                    Consequences = "Düzeltme ayrı bir olay olur; onaylı masrafın değiştirilebilirliği OQ-002'ye bağlı.",
                    ReviewStatus = ReviewStatus.Confirmed,
                },
                new Decision
                {
                    Id = "DEC-006", Title = "Fiş dosyaları nesne depolamada, veritabanında yalnızca referans",
                    Choice = "Yüklenen fiş nesne depolamaya yazılır; masraf kaydı referansı tutar.",
                    Rationale = "Büyük dosyaları ilişkisel veriden ayırmak.",
                    Consequences = "Dosya erişimi de kiracı kapsamıyla sınırlanmalı.",
                    ReviewStatus = ReviewStatus.Confirmed,
                },
            ],
            Components =
            [
                Cmp("CMP-001", "Web uygulaması", ComponentKind.Client, "Masraf girişi ve onay ekranları.", "React, TypeScript"),
                Cmp("CMP-002", "Masraf API", ComponentKind.Api, "HTTP girişi, kimlik doğrulama ve kiracı bağlamı.", "ASP.NET Core Web API"),
                Cmp("CMP-003", "Masraf modülü", ComponentKind.Module, "Masraf ve fiş yaşam döngüsü.", "ASP.NET Core"),
                Cmp("CMP-004", "Onay modülü", ComponentKind.Module, "Onay/ret kararları ve onay geçmişi.", "ASP.NET Core"),
                Cmp("CMP-005", "Uygulama verisi", ComponentKind.Database, "İşlemsel veri deposu.", "PostgreSQL"),
                Cmp("CMP-006", "Entegrasyon işleyicisi", ComponentKind.Worker, "Bekleyen muhasebe gönderimlerini alır ve gönderir.", "ASP.NET Core Worker Service"),
                Cmp("CMP-007", "Muhasebe servisi", ComponentKind.ExternalService, "Dış muhasebe sistemi.", null),
                Cmp("CMP-008", "Fiş depolama", ComponentKind.Storage, "Fiş dosyalarının saklanması.", "Nesne depolama"),
                Cmp("CMP-009", "Kimlik ve kiracı modülü", ComponentKind.Module, "Kullanıcı, şirket ve kiracı bağlamı.", "ASP.NET Core"),
            ],
            Relationships =
            [
                Rel("REL-001", "CMP-001", "CMP-002", CommunicationKind.Sync, "HTTPS / JSON"),
                Rel("REL-002", "CMP-002", "CMP-003", CommunicationKind.Sync, "Masraf komutları"),
                Rel("REL-003", "CMP-002", "CMP-004", CommunicationKind.Sync, "Onay komutları"),
                Rel("REL-004", "CMP-003", "CMP-005", CommunicationKind.Data, "Masraf verisi"),
                Rel("REL-005", "CMP-004", "CMP-005", CommunicationKind.Data, "Onay olayları ve gönderim niyeti"),
                Rel("REL-006", "CMP-006", "CMP-005", CommunicationKind.Data, "Bekleyen gönderimleri okur"),
                Rel("REL-007", "CMP-006", "CMP-007", CommunicationKind.Sync, "Onaylı masrafı gönderir",
                    "Başarısızlıkta gönderim niyeti korunur; sınırlı sayıda ve artan aralıkla yeniden denenir."),
                Rel("REL-008", "CMP-003", "CMP-008", CommunicationKind.Data, "Fiş dosyaları"),
                Rel("REL-009", "CMP-002", "CMP-009", CommunicationKind.Sync, "Kiracı bağlamı çözümleme"),
            ],
            Entities =
            [
                new DataEntity
                {
                    Id = "ENT-001", Name = "Şirket", OwnerComponentId = "CMP-009", ReviewStatus = ReviewStatus.Confirmed,
                    Fields = [F("id", "uuid", key: true), F("ad")],
                    Relations =
                    [
                        new EntityRelation { TargetEntityId = "ENT-002", Cardinality = Cardinality.OneToMany, Label = "üyeleri" },
                        new EntityRelation { TargetEntityId = "ENT-003", Cardinality = Cardinality.OneToMany, Label = "masrafları" },
                    ],
                },
                new DataEntity
                {
                    Id = "ENT-002", Name = "Kullanıcı", OwnerComponentId = "CMP-009", ReviewStatus = ReviewStatus.Confirmed,
                    Fields = [F("id", "uuid", key: true), F("kiracı_id", "uuid"), F("e_posta"), F("rol")],
                    Relations = [new EntityRelation { TargetEntityId = "ENT-003", Cardinality = Cardinality.OneToMany, Label = "oluşturur" }],
                },
                new DataEntity
                {
                    Id = "ENT-003", Name = "Masraf", OwnerComponentId = "CMP-003", ReviewStatus = ReviewStatus.Confirmed,
                    Fields = [F("id", "uuid", key: true), F("kiracı_id", "uuid"), F("tutar", "decimal"), F("para_birimi"), F("durum"), F("açıklama", nullable: true)],
                    Relations =
                    [
                        new EntityRelation { TargetEntityId = "ENT-004", Cardinality = Cardinality.OneToMany, Label = "fişleri" },
                        new EntityRelation { TargetEntityId = "ENT-005", Cardinality = Cardinality.OneToMany, Label = "onay olayları" },
                        new EntityRelation { TargetEntityId = "ENT-006", Cardinality = Cardinality.OneToMany, Label = "gönderimleri" },
                    ],
                    Constraints = ["tutar > 0"],
                },
                new DataEntity
                {
                    Id = "ENT-004", Name = "Fiş", OwnerComponentId = "CMP-003", ReviewStatus = ReviewStatus.Confirmed,
                    Fields = [F("id", "uuid", key: true), F("masraf_id", "uuid"), F("depolama_anahtarı"), F("içerik_türü")],
                },
                new DataEntity
                {
                    Id = "ENT-005", Name = "Onay olayı", OwnerComponentId = "CMP-004", ReviewStatus = ReviewStatus.Confirmed,
                    Fields = [F("id", "uuid", key: true), F("masraf_id", "uuid"), F("aktör_id", "uuid"), F("karar"), F("zaman", "timestamp")],
                    Constraints = ["yalnızca eklenir (append-only)"],
                },
                new DataEntity
                {
                    Id = "ENT-006", Name = "Muhasebe gönderimi", OwnerComponentId = "CMP-006", ReviewStatus = ReviewStatus.Confirmed,
                    Fields = [F("id", "uuid", key: true), F("masraf_id", "uuid"), F("idempotency_anahtarı"), F("durum"), F("deneme_sayısı", "int"), F("sonraki_deneme", "timestamp", nullable: true)],
                },
            ],
            Flows =
            [
                new Flow
                {
                    Id = "FLW-001", Name = "Masraf onayı ve muhasebeye gönderim", ReviewStatus = ReviewStatus.Confirmed,
                    Description = "Onay ile gönderim niyeti birlikte kaydedilir; gönderim işleyici tarafından eşzamansız yapılır.",
                    Participants = ["ACT-002", "CMP-001", "CMP-002", "CMP-004", "CMP-005", "CMP-006", "CMP-007"],
                    Steps =
                    [
                        new FlowStep { From = "ACT-002", To = "CMP-001", Message = "Masrafı onayla" },
                        new FlowStep { From = "CMP-001", To = "CMP-002", Message = "POST /expenses/{id}/approve" },
                        new FlowStep { From = "CMP-002", To = "CMP-004", Message = "Onay komutu" },
                        new FlowStep { From = "CMP-004", To = "CMP-005", Message = "Onay olayı ve gönderim niyetini tek işlemde kaydet" },
                        new FlowStep { From = "CMP-002", To = "CMP-001", Message = "202 Accepted", Kind = StepKind.Return },
                        new FlowStep { From = "CMP-006", To = "CMP-005", Message = "Bekleyen gönderimi al" },
                        new FlowStep { From = "CMP-006", To = "CMP-007", Message = "Masrafı idempotency anahtarıyla gönder" },
                        new FlowStep { From = "CMP-006", To = "CMP-005", Message = "Gönderimi tamamlandı işaretle", AltId = "alt-1", Condition = "Gönderim başarılı" },
                        new FlowStep { From = "CMP-006", To = "CMP-005", Message = "Yeniden deneme zamanı planla", AltId = "alt-1", Condition = "Muhasebe hatası" },
                    ],
                },
                new Flow
                {
                    Id = "FLW-002", Name = "Masraf girişi ve fiş yükleme", ReviewStatus = ReviewStatus.Confirmed,
                    Participants = ["ACT-001", "CMP-001", "CMP-002", "CMP-003", "CMP-005", "CMP-008"],
                    Steps =
                    [
                        new FlowStep { From = "ACT-001", To = "CMP-001", Message = "Masraf bilgilerini ve fişi gönder" },
                        new FlowStep { From = "CMP-001", To = "CMP-002", Message = "POST /expenses" },
                        new FlowStep { From = "CMP-002", To = "CMP-003", Message = "Masraf oluştur" },
                        new FlowStep { From = "CMP-003", To = "CMP-005", Message = "Masrafı kaydet" },
                        new FlowStep { From = "CMP-003", To = "CMP-008", Message = "Fişi sakla" },
                        new FlowStep { From = "CMP-002", To = "CMP-001", Message = "201 Created", Kind = StepKind.Return },
                    ],
                },
            ],
            Endpoints =
            [
                new ApiEndpoint { Id = "API-001", Method = "POST", Path = "/expenses", Summary = "Masraf oluşturur", ComponentId = "CMP-002", Authorization = "Çalışan; kiracı kapsamlı", ReviewStatus = ReviewStatus.Confirmed },
                new ApiEndpoint { Id = "API-002", Method = "POST", Path = "/expenses/{id}/receipts", Summary = "Masrafa fiş ekler", ComponentId = "CMP-002", Authorization = "Masrafın sahibi; kiracı kapsamlı", ReviewStatus = ReviewStatus.Confirmed },
                new ApiEndpoint { Id = "API-003", Method = "POST", Path = "/expenses/{id}/approve", Summary = "Masrafı onaylar ve gönderim niyetini oluşturur", ComponentId = "CMP-002", Authorization = "Yönetici; kiracı kapsamlı", ReviewStatus = ReviewStatus.Confirmed },
                new ApiEndpoint { Id = "API-004", Method = "POST", Path = "/expenses/{id}/reject", Summary = "Masrafı reddeder", ComponentId = "CMP-002", Authorization = "Yönetici; kiracı kapsamlı", ReviewStatus = ReviewStatus.Confirmed },
                new ApiEndpoint { Id = "API-005", Method = "GET", Path = "/expenses/{id}/approvals", Summary = "Onay geçmişini getirir", ComponentId = "CMP-002", Authorization = "Yönetici veya masrafın sahibi", ReviewStatus = ReviewStatus.Confirmed },
            ],
            TraceLinks =
            [
                Trace("TRC-001", "DEC-001", "REQ-008", TraceKind.Addresses),
                Trace("TRC-002", "DEC-001", "REQ-009", TraceKind.Addresses),
                Trace("TRC-003", "DEC-002", "REQ-001", TraceKind.Addresses),
                Trace("TRC-004", "DEC-003", "REQ-002", TraceKind.Addresses),
                Trace("TRC-005", "DEC-003", "REQ-007", TraceKind.Addresses),
                Trace("TRC-006", "DEC-004", "REQ-003", TraceKind.Addresses),
                Trace("TRC-007", "DEC-005", "REQ-004", TraceKind.Addresses),
                Trace("TRC-008", "DEC-005", "REQ-006", TraceKind.Addresses),
                Trace("TRC-009", "DEC-006", "REQ-005", TraceKind.Addresses),
                Trace("TRC-010", "CMP-002", "DEC-001", TraceKind.Implements),
                Trace("TRC-011", "CMP-009", "DEC-002", TraceKind.Implements),
                Trace("TRC-012", "ENT-001", "DEC-002", TraceKind.Implements),
                Trace("TRC-013", "CMP-006", "DEC-003", TraceKind.Implements),
                Trace("TRC-014", "ENT-006", "DEC-003", TraceKind.Implements),
                Trace("TRC-015", "FLW-001", "DEC-003", TraceKind.Implements),
                Trace("TRC-016", "REL-007", "DEC-004", TraceKind.Implements),
                Trace("TRC-017", "FLW-001", "DEC-004", TraceKind.Implements),
                Trace("TRC-018", "CMP-004", "DEC-005", TraceKind.Implements),
                Trace("TRC-019", "ENT-005", "DEC-005", TraceKind.Implements),
                Trace("TRC-020", "API-003", "DEC-005", TraceKind.Implements),
                Trace("TRC-021", "API-005", "DEC-005", TraceKind.Implements),
                Trace("TRC-022", "CMP-008", "DEC-006", TraceKind.Implements),
                Trace("TRC-023", "DEC-004", "ASM-001", TraceKind.Assumes),
                Trace("TRC-024", "DEC-002", "ASM-002", TraceKind.Assumes),
                Trace("TRC-025", "DEC-002", "OQ-001", TraceKind.BlockedBy),
                Trace("TRC-026", "DEC-005", "OQ-002", TraceKind.BlockedBy),
                Trace("TRC-027", "ENT-003", "OQ-002", TraceKind.BlockedBy),
            ],
        };

        return model;
    }

    /// <summary>
    /// §9.5 demo değişikliği: "şirketler kendi iki aşamalı onay sürecini tanımlayabilsin".
    /// Temel sürüm: <see cref="Model"/>. Devam eden masrafların politika sürümü sorusu cevaplanmadığı için
    /// ilgili karar geçici varsayımla 'ertelendi' işaretlenir ve bağımlı nesneler açık soruya bağlanır.
    /// </summary>
    public static (ModelPatch Patch, IReadOnlyList<PossibleImpact> Possible) TwoStageApprovalChange()
    {
        const string gReq = "grp-requirements";
        const string gData = "grp-data";
        const string gApi = "grp-api";
        const string gFlow = "grp-flow";
        const string gOpen = "grp-open-question";

        var baseModel = Model();
        var ent001 = baseModel.Entities.First(e => e.Id == "ENT-001");
        var ent003 = baseModel.Entities.First(e => e.Id == "ENT-003");
        var ent005 = baseModel.Entities.First(e => e.Id == "ENT-005");
        var dec005 = baseModel.Decisions.First(d => d.Id == "DEC-005");
        var api003 = baseModel.Endpoints.First(e => e.Id == "API-003");
        var flw001 = baseModel.Flows.First(f => f.Id == "FLW-001");

        var ops = new List<PatchOperation>
        {
            // Gereksinim + karar
            ModelPatch.Upsert(ObjectType.Requirement, new Requirement
            {
                Id = "REQ-010", Text = "Şirketler kendi (en çok iki aşamalı) onay politikasını tanımlayabilmeli",
                Kind = RequirementKind.Functional, Priority = Priority.High, Origin = Origin.UserStatement,
                SourceRefs = [new SourceRef { SourceId = "SRC-001", Excerpt = ChangeRequestText }],
                AcceptanceCriteria =
                [
                    new AcceptanceCriterion { Id = "AC-008", Text = "Şirket yöneticisi iki adımlı bir onay politikası kaydedebilir" },
                    new AcceptanceCriterion { Id = "AC-009", Text = "Aktif adımın yetkisi olmayan kullanıcı onay veremez" },
                ],
            }, gReq),
            ModelPatch.Upsert(ObjectType.Decision, new Decision
            {
                Id = "DEC-007", Title = "Onay politikası sürümlenir; masraf başladığı politika sürümüne sabitlenir",
                Choice = "Politika değişince yeni sürüm oluşur; her masraf başladığı sürümün adımlarını izler (geçici varsayım).",
                Alternatives = [new Alternative { Name = "Devam edenler yeni politikaya geçer", Tradeoff = "Onay sürecinin ortasında kural değişir; geçmiş tutarsızlaşır." }],
                Rationale = "Devam eden masrafların hangi politikayı izleyeceği OQ-004'te cevap bekliyor; geçici varsayımla ilerlenir.",
                Consequences = "Masraf kaydı politika sürümü ve aktif adım bilgisini taşır; cevap farklı çıkarsa geçiş kuralı yeniden yazılır.",
                ReviewStatus = ReviewStatus.Deferred,
            }, gReq),
            ModelPatch.Upsert(ObjectType.Decision, new Decision
            {
                Id = "DEC-008", Title = "Onay yetkisi aktif adımın rolüne göre politika tabanlı denetlenir",
                Choice = "Onay komutu, masrafın aktif adımındaki rolü ve kiracı kapsamını yetkilendirme politikasıyla denetler.",
                Rationale = "AC-009: aktif adımın yetkisi olmayan kullanıcı onay veremez.",
                Consequences = "Yetkilendirme artık yalnızca 'yönetici mi' değil, 'bu adımın onaylayıcısı mı' sorusunu yanıtlar.",
                ReviewStatus = ReviewStatus.Proposed,
            }, gReq),
            ModelPatch.Upsert(ObjectType.Assumption, new Assumption
            {
                Id = "ASM-003", Text = "Devam eden masraflar başlatıldıkları politika sürümüyle tamamlanır",
                Rationale = "Geçici varsayım; OQ-004 cevaplanana kadar doğrulanmamıştır.", Status = AssumptionStatus.Unverified,
                Origin = Origin.Assumption, ReviewStatus = ReviewStatus.Proposed,
            }, gOpen),
            ModelPatch.Upsert(ObjectType.OpenQuestion, new OpenQuestion
            {
                Id = "OQ-004", Text = "Devam eden masraflar başlatıldıkları onay politikası sürümüyle mi tamamlanmalı?",
                Impact = Priority.Critical, ReviewStatus = ReviewStatus.Proposed,
            }, gOpen),
            Link("TRC-028", "DEC-007", "REQ-010", TraceKind.Addresses, gReq),
            Link("TRC-029", "DEC-008", "REQ-010", TraceKind.Addresses, gReq),
            Link("TRC-030", "DEC-007", "ASM-003", TraceKind.Assumes, gOpen),
            Link("TRC-031", "DEC-007", "OQ-004", TraceKind.BlockedBy, gOpen),
            Link("TRC-032", "ENT-003", "OQ-004", TraceKind.BlockedBy, gOpen),
            Link("TRC-033", "DEC-008", "DEC-007", TraceKind.DependsOn, gReq),

            // Mevcut kararın sonuçlarını güncelle (doğrudan değişiklik)
            ModelPatch.Upsert(ObjectType.Decision, dec005 with
            {
                Consequences = "Düzeltme ayrı bir olay olur; her olay hangi politika adımında verildiğini taşır. Onaylı masrafın değiştirilebilirliği OQ-002'ye bağlı.",
            }, gReq),

            // Veri modeli
            ModelPatch.Upsert(ObjectType.Entity, new DataEntity
            {
                Id = "ENT-007", Name = "Onay politikası", OwnerComponentId = "CMP-004", ReviewStatus = ReviewStatus.Proposed,
                Fields = [F("id", "uuid", key: true), F("kiracı_id", "uuid"), F("ad"), F("aktif_sürüm_id", "uuid", nullable: true)],
                Relations = [new EntityRelation { TargetEntityId = "ENT-008", Cardinality = Cardinality.OneToMany, Label = "sürümleri" }],
            }, gData),
            ModelPatch.Upsert(ObjectType.Entity, new DataEntity
            {
                Id = "ENT-008", Name = "Onay politikası sürümü", OwnerComponentId = "CMP-004", ReviewStatus = ReviewStatus.Proposed,
                Fields = [F("id", "uuid", key: true), F("politika_id", "uuid"), F("sürüm_no", "int"), F("oluşturulma", "timestamp")],
                Constraints = ["değiştirilemez (yeni sürüm oluşturulur)"],
                Relations = [new EntityRelation { TargetEntityId = "ENT-009", Cardinality = Cardinality.OneToMany, Label = "adımları" }],
            }, gData),
            ModelPatch.Upsert(ObjectType.Entity, new DataEntity
            {
                Id = "ENT-009", Name = "Onay adımı", OwnerComponentId = "CMP-004", ReviewStatus = ReviewStatus.Proposed,
                Fields = [F("id", "uuid", key: true), F("politika_sürümü_id", "uuid"), F("sıra", "int"), F("onaylayıcı_rolü")],
                Constraints = ["en çok iki adım"],
            }, gData),
            ModelPatch.Upsert(ObjectType.Entity, ent001 with
            {
                Relations = [.. ent001.Relations, new EntityRelation { TargetEntityId = "ENT-007", Cardinality = Cardinality.OneToMany, Label = "onay politikaları" }],
            }, gData),
            ModelPatch.Upsert(ObjectType.Entity, ent003 with
            {
                Fields = [.. ent003.Fields, F("politika_sürümü_id", "uuid", nullable: true), F("aktif_adım_sırası", "int", nullable: true)],
            }, gData),
            ModelPatch.Upsert(ObjectType.Entity, ent005 with
            {
                Fields = [.. ent005.Fields, F("adım_sırası", "int", nullable: true)],
            }, gData),
            Link("TRC-034", "ENT-007", "DEC-007", TraceKind.Implements, gData),
            Link("TRC-035", "ENT-008", "DEC-007", TraceKind.Implements, gData),
            Link("TRC-036", "ENT-009", "DEC-007", TraceKind.Implements, gData),

            // API
            ModelPatch.Upsert(ObjectType.Endpoint, new ApiEndpoint
            {
                Id = "API-006", Method = "PUT", Path = "/approval-policy", Summary = "Şirketin onay politikasının yeni sürümünü kaydeder",
                ComponentId = "CMP-002", Authorization = "Şirket yöneticisi; kiracı kapsamlı", ReviewStatus = ReviewStatus.Proposed,
            }, gApi),
            ModelPatch.Upsert(ObjectType.Endpoint, new ApiEndpoint
            {
                Id = "API-007", Method = "GET", Path = "/approval-policy", Summary = "Aktif onay politikasını getirir",
                ComponentId = "CMP-002", Authorization = "Şirket kullanıcıları; kiracı kapsamlı", ReviewStatus = ReviewStatus.Proposed,
            }, gApi),
            ModelPatch.Upsert(ObjectType.Endpoint, api003 with
            {
                Summary = "Aktif adımı onaylar; son adımsa gönderim niyetini oluşturur, değilse sonraki adıma geçirir",
                Authorization = "Aktif adımın onaylayıcısı; kiracı kapsamlı",
            }, gApi),
            Link("TRC-037", "API-006", "DEC-007", TraceKind.Implements, gApi),
            Link("TRC-038", "API-003", "DEC-008", TraceKind.Implements, gApi),

            // Akış
            ModelPatch.Upsert(ObjectType.Actor, new Actor
            {
                Id = "ACT-003", Name = "Finans onaylayıcısı", Description = "İkinci adımın onaylayıcısı.",
                Origin = Origin.SystemProposal, ReviewStatus = ReviewStatus.Proposed,
            }, gFlow),
            ModelPatch.Upsert(ObjectType.Flow, new Flow
            {
                Id = "FLW-003", Name = "İki aşamalı onay", ReviewStatus = ReviewStatus.Proposed,
                Description = "İlk onaydan sonra politika sonraki adım gerektiriyorsa masraf ikinci onaylayıcıya geçer; yalnızca son onayda gönderim niyeti oluşur.",
                Participants = ["ACT-002", "ACT-003", "CMP-001", "CMP-002", "CMP-004", "CMP-005"],
                Steps =
                [
                    new FlowStep { From = "ACT-002", To = "CMP-001", Message = "İlk adımı onayla" },
                    new FlowStep { From = "CMP-001", To = "CMP-002", Message = "POST /expenses/{id}/approve" },
                    new FlowStep { From = "CMP-002", To = "CMP-004", Message = "Aktif adım yetkisini denetle ve onayla" },
                    new FlowStep { From = "CMP-004", To = "CMP-005", Message = "Onay olayını adım bilgisiyle kaydet" },
                    new FlowStep { From = "CMP-004", To = "CMP-005", Message = "Aktif adımı ikinci adıma ilerlet", AltId = "alt-1", Condition = "Politikada sonraki adım var" },
                    new FlowStep { From = "CMP-004", To = "CMP-005", Message = "Gönderim niyetini aynı işlemde kaydet", AltId = "alt-1", Condition = "Son adım" },
                    new FlowStep { From = "ACT-003", To = "CMP-001", Message = "İkinci adımı onayla" },
                ],
            }, gFlow),
            ModelPatch.Upsert(ObjectType.Flow, flw001 with
            {
                Steps = flw001.Steps.Select(s => s.Message.StartsWith("Onay olayı ve gönderim", StringComparison.Ordinal)
                    ? s with { Message = "Onay olayını kaydet; son onaysa gönderim niyetini de tek işlemde kaydet" } : s).ToList(),
            }, gFlow),
            Link("TRC-039", "FLW-003", "DEC-007", TraceKind.Implements, gFlow),
            Link("TRC-040", "FLW-003", "DEC-008", TraceKind.Implements, gFlow),
        };

        var patch = new ModelPatch
        {
            Summary = "İki aşamalı şirket onay politikası",
            Operations = ops,
            Groups =
            [
                new PatchGroup { Id = gReq, Title = "Gereksinim ve kararlar", Description = "Şirkete özel onay politikası; politika sürümleme ve adım bazlı yetkilendirme." },
                new PatchGroup { Id = gData, Title = "Veri modeli", Description = "Politika, politika sürümü ve onay adımı kavramları; masraf kaydında aktif adım." },
                new PatchGroup { Id = gApi, Title = "API", Description = "Politika yönetimi ve aktif adımı onaylama." },
                new PatchGroup { Id = gFlow, Title = "Akışlar", Description = "İlk onaydan sonra ikinci onaya geçiş." },
                new PatchGroup { Id = gOpen, Title = "Açık soru ve geçici varsayım", Description = "Devam eden masrafların politika sürümü cevaplanana kadar geçici varsayım." },
            ],
        };

        IReadOnlyList<PossibleImpact> possible =
        [
            new("REQ-004", "Adım bazlı onay geçmişinin gösterimi bu gereksinimi etkileyebilir."),
            new("API-005", "Onay geçmişi yanıtına adım bilgisi eklenmesi gerekebilir."),
            new("DEC-002", "Politika kayıtları da kiracı kapsamına alınmalı; kiracı filtresi yeni varlıkları kapsamalı."),
        ];

        return (patch, possible);

        static PatchOperation Link(string id, string from, string to, TraceKind kind, string group) =>
            ModelPatch.Upsert(ObjectType.TraceLink, new TraceLink
            {
                Id = id, FromId = from, ToId = to, Kind = kind, ReviewStatus = ReviewStatus.Proposed,
            }, group);
    }
}
