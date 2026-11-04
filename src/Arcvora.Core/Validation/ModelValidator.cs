using Arcvora.Core.Model;
using Arcvora.Core.Profiles;

namespace Arcvora.Core.Validation;

/// <summary>
/// Deterministik model kontrolleri (§15.2). Bu kontroller mimari doğruluğu veya iş ihtiyacının
/// karşılandığını KANITLAMAZ: şema/referans kontrolü yalnızca yapısal bütünlüğü, izlenebilirlik kontrolü
/// yalnızca bağlantının varlığını söyler.
/// </summary>
public static class ModelValidator
{
    private static readonly string[] HttpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    private static readonly string[] VagueTerms =
    [
        "hızlı", "hizli", "kolay", "güvenli", "guvenli", "ölçeklenebilir", "olceklenebilir", "performanslı",
        "esnek", "basit", "modern", "kullanıcı dostu", "fast", "easy", "secure", "scalable", "performant", "robust",
    ];

    private static readonly IReadOnlyDictionary<TraceKind, (ObjectType[] From, ObjectType[] To)> TraceRules =
        new Dictionary<TraceKind, (ObjectType[], ObjectType[])>
        {
            [TraceKind.Addresses] = ([ObjectType.Decision], [ObjectType.Requirement]),
            [TraceKind.Implements] = (
                [ObjectType.Component, ObjectType.Entity, ObjectType.Flow, ObjectType.Endpoint, ObjectType.Relationship],
                [ObjectType.Decision]),
            [TraceKind.DependsOn] = ([ObjectType.Decision], [ObjectType.Decision]),
            [TraceKind.Assumes] = ([ObjectType.Decision], [ObjectType.Assumption]),
            [TraceKind.BlockedBy] = (
                [ObjectType.Requirement, ObjectType.Decision, ObjectType.Component, ObjectType.Entity,
                 ObjectType.Flow, ObjectType.Endpoint, ObjectType.Relationship, ObjectType.Assumption],
                [ObjectType.OpenQuestion]),
        };

    public static ValidationReport Validate(ArchitectureModel model)
    {
        var findings = new List<ValidationFinding>();
        var index = new ModelIndex(model);

        Schema(model, index, findings);
        References(model, index, findings);
        Consistency(model, index, findings);
        Traceability(model, index, findings);
        Profile(model, findings);
        ReviewSuggestions(model, index, findings);

        return new ValidationReport(findings);
    }

    // ---------------------------------------------------------------- şema

    private static void Schema(ArchitectureModel m, ModelIndex index, List<ValidationFinding> f)
    {
        if (m.SchemaVersion != ArchitectureModel.CurrentSchemaVersion)
            f.Add(Err("SCH-006", FindingKind.Schema, null,
                $"Desteklenmeyen şema sürümü '{m.SchemaVersion}' (beklenen {ArchitectureModel.CurrentSchemaVersion})."));

        foreach (var dup in index.Duplicates.Distinct())
            f.Add(Err("SCH-001", FindingKind.Schema, dup, $"'{dup}' kimliği birden fazla nesnede kullanılıyor."));

        foreach (var o in index.All)
        {
            if (Ids.TypeOf(o.Id) != o.Type)
                f.Add(Err("SCH-002", FindingKind.Schema, o.Id,
                    $"'{o.Id}' kimliği {Ids.Prefix(o.Type)}-NNN biçiminde olmalı."));
        }

        foreach (var s in m.Sources) NotBlank(f, s.Id, "kaynak metni", s.Text);
        foreach (var r in m.Requirements) NotBlank(f, r.Id, "gereksinim metni", r.Text);
        foreach (var a in m.Assumptions) NotBlank(f, a.Id, "varsayım metni", a.Text);
        foreach (var d in m.Decisions) NotBlank(f, d.Id, "karar başlığı", d.Title);
        foreach (var c in m.Components) NotBlank(f, c.Id, "bileşen adı", c.Name);
        foreach (var e in m.Entities)
        {
            NotBlank(f, e.Id, "varlık adı", e.Name);
            if (e.Fields.Any(x => string.IsNullOrWhiteSpace(x.Name)))
                f.Add(Err("SCH-003", FindingKind.Schema, e.Id, "Varlıkta adı boş alan var."));
        }
        foreach (var a in m.Actors) NotBlank(f, a.Id, "aktör adı", a.Name);
        foreach (var fl in m.Flows) NotBlank(f, fl.Id, "akış adı", fl.Name);
        foreach (var q in m.OpenQuestions) NotBlank(f, q.Id, "soru metni", q.Text);
        foreach (var ep in m.Endpoints)
        {
            if (!HttpMethods.Contains(ep.Method?.ToUpperInvariant()))
                f.Add(Err("SCH-003", FindingKind.Schema, ep.Id, $"Geçersiz HTTP metodu '{ep.Method}'."));
            if (string.IsNullOrWhiteSpace(ep.Path) || !ep.Path.StartsWith('/'))
                f.Add(Err("SCH-003", FindingKind.Schema, ep.Id, "Endpoint yolu '/' ile başlamalı."));
        }

        var acIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in m.Requirements)
        foreach (var ac in r.AcceptanceCriteria)
        {
            if (!Ids.IsAcceptanceCriterionId(ac.Id))
                f.Add(Err("SCH-004", FindingKind.Schema, r.Id, $"Kabul ölçütü kimliği '{ac.Id}' AC-NNN biçiminde olmalı."));
            else if (!acIds.Add(ac.Id) || index.Contains(ac.Id))
                f.Add(Err("SCH-004", FindingKind.Schema, r.Id, $"Kabul ölçütü kimliği '{ac.Id}' benzersiz değil."));
            if (string.IsNullOrWhiteSpace(ac.Text))
                f.Add(Err("SCH-003", FindingKind.Schema, r.Id, $"Kabul ölçütü '{ac.Id}' boş."));
        }
    }

    private static void NotBlank(List<ValidationFinding> f, string id, string what, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            f.Add(Err("SCH-003", FindingKind.Schema, id, $"{what} boş olamaz."));
    }

    // ------------------------------------------------------------ referans

    private static void References(ArchitectureModel m, ModelIndex index, List<ValidationFinding> f)
    {
        foreach (var r in m.Relationships)
        {
            RequireType(f, index, "REF-001", r.Id, r.SourceId, ObjectType.Component, "bağlantı kaynağı");
            RequireType(f, index, "REF-001", r.Id, r.TargetId, ObjectType.Component, "bağlantı hedefi");
        }

        foreach (var e in m.Entities)
        {
            if (e.OwnerComponentId is not null)
                RequireType(f, index, "REF-002", e.Id, e.OwnerComponentId, ObjectType.Component, "veri sahibi bileşen");
            foreach (var rel in e.Relations)
                RequireType(f, index, "REF-003", e.Id, rel.TargetEntityId, ObjectType.Entity, "ilişki hedef varlığı");
        }

        foreach (var fl in m.Flows)
        {
            foreach (var p in fl.Participants)
            {
                var t = index.TypeOf(p);
                if (t is null)
                    f.Add(Err("REF-004", FindingKind.Reference, fl.Id, $"Akış katılımcısı '{p}' modelde yok."));
                else if (t is not (ObjectType.Component or ObjectType.Actor))
                    f.Add(Err("REF-004", FindingKind.Reference, fl.Id, $"Akış katılımcısı '{p}' bileşen veya aktör değil."));
            }

            var parts = new HashSet<string>(fl.Participants);
            foreach (var s in fl.Steps)
            {
                if (!parts.Contains(s.From))
                    f.Add(Err("REF-005", FindingKind.Reference, fl.Id, $"Adım göndereni '{s.From}' akış katılımcısı değil."));
                if (!parts.Contains(s.To))
                    f.Add(Err("REF-005", FindingKind.Reference, fl.Id, $"Adım alıcısı '{s.To}' akış katılımcısı değil."));
            }
        }

        foreach (var t in m.TraceLinks)
        {
            var from = index.Find(t.FromId);
            var to = index.Find(t.ToId);
            if (from is null) f.Add(Err("REF-006", FindingKind.Reference, t.Id, $"Bağlantı kaynağı '{t.FromId}' modelde yok."));
            if (to is null) f.Add(Err("REF-006", FindingKind.Reference, t.Id, $"Bağlantı hedefi '{t.ToId}' modelde yok."));
            if (from is not null && to is not null && TraceRules.TryGetValue(t.Kind, out var rule)
                && (!rule.From.Contains(from.Type) || !rule.To.Contains(to.Type)))
            {
                f.Add(Err("REF-007", FindingKind.Reference, t.Id,
                    $"'{t.Kind}' bağlantısı {from.Type} → {to.Type} arasında geçerli değil."));
            }
        }

        foreach (var r in m.Requirements)
        foreach (var sr in r.SourceRefs)
            RequireType(f, index, "REF-008", r.Id, sr.SourceId, ObjectType.Source, "kaynak bağlantısı");

        foreach (var ep in m.Endpoints)
            if (ep.ComponentId is not null)
                RequireType(f, index, "REF-009", ep.Id, ep.ComponentId, ObjectType.Component, "endpoint bileşeni");
    }

    private static void RequireType(List<ValidationFinding> f, ModelIndex index, string rule, string owner, string target,
        ObjectType expected, string what)
    {
        var t = index.TypeOf(target);
        if (t is null)
            f.Add(Err(rule, FindingKind.Reference, owner, $"{what} '{target}' modelde yok."));
        else if (t != expected)
            f.Add(Err(rule, FindingKind.Reference, owner, $"{what} '{target}' bir {expected} olmalı, {t} bulundu."));
    }

    // ----------------------------------------------------------- tutarlılık

    private static void Consistency(ArchitectureModel m, ModelIndex index, List<ValidationFinding> f)
    {
        foreach (var r in m.Relationships.Where(r => r.SourceId == r.TargetId))
            f.Add(Warn("CON-001", FindingKind.Consistency, r.Id, "Bileşen kendisine bağlanıyor."));

        foreach (var g in m.Relationships.GroupBy(r => (r.SourceId, r.TargetId, r.Communication)).Where(g => g.Count() > 1))
            f.Add(Warn("CON-002", FindingKind.Consistency, g.First().Id,
                $"Aynı bileşenler arasında aynı türde {g.Count()} bağlantı var: {string.Join(", ", g.Select(x => x.Id))}."));

        foreach (var e in m.Entities.Where(e => e.OwnerComponentId is null))
            f.Add(Warn("CON-003", FindingKind.Consistency, e.Id, "Varlığın sahibi bileşen tanımlı değil (§3: veri sahipliği)."));

        foreach (var fl in m.Flows)
        {
            if (fl.Participants.Count < 2 || fl.Steps.Count == 0)
                f.Add(Warn("CON-004", FindingKind.Consistency, fl.Id, "Akış en az iki katılımcı ve bir adım içermeli."));
            if (fl.Steps.Any(s => s.AltId is not null && string.IsNullOrWhiteSpace(s.Condition)))
                f.Add(Warn("CON-006", FindingKind.Consistency, fl.Id, "Alternatif yol adımında koşul yok."));
        }

        foreach (var g in m.TraceLinks.GroupBy(t => (t.FromId, t.ToId, t.Kind)).Where(g => g.Count() > 1))
            f.Add(Warn("CON-007", FindingKind.Consistency, g.First().Id, "Aynı izlenebilirlik bağlantısı birden fazla kez tanımlı."));
    }

    // -------------------------------------------------------- izlenebilirlik

    private static void Traceability(ArchitectureModel m, ModelIndex index, List<ValidationFinding> f)
    {
        var addressed = m.TraceLinks.Where(t => t.Kind == TraceKind.Addresses).Select(t => t.ToId).ToHashSet();
        var addressing = m.TraceLinks.Where(t => t.Kind == TraceKind.Addresses).Select(t => t.FromId).ToHashSet();
        var implemented = m.TraceLinks.Where(t => t.Kind == TraceKind.Implements).Select(t => t.ToId).ToHashSet();

        foreach (var r in m.Requirements.Where(r => r.ReviewStatus != ReviewStatus.Rejected))
        {
            var important = r.Priority is Priority.Critical or Priority.High;
            var coverable = r.Kind is RequirementKind.Functional or RequirementKind.Quality;
            if (important && coverable && !addressed.Contains(r.Id))
                f.Add(Warn("TRC-001", FindingKind.Traceability, r.Id,
                    $"{r.Priority} öncelikli gereksinimi karşılayan bir karar bağlantısı yok.",
                    "Bağlantı bulunması yeterlilik kanıtı değildir; yokluğu ise açık bir boşluktur."));
            if (important && coverable && r.AcceptanceCriteria.Count == 0)
                f.Add(Warn("TRC-004", FindingKind.Traceability, r.Id, "Kabul ölçütü tanımlı değil."));
        }

        foreach (var d in m.Decisions.Where(d => d.ReviewStatus != ReviewStatus.Rejected))
        {
            if (!addressing.Contains(d.Id))
                f.Add(Warn("TRC-002", FindingKind.Traceability, d.Id, "Karar hangi gereksinime dayandığını göstermiyor."));
            else if (d.ReviewStatus == ReviewStatus.Confirmed && !implemented.Contains(d.Id))
                f.Add(Info("TRC-003", FindingKind.Traceability, d.Id, "Onaylı kararı uygulayan bir bileşen/varlık/akış bağlantısı yok."));
        }

        foreach (var t in m.TraceLinks.Where(t => t.Kind == TraceKind.BlockedBy))
        {
            if (index.Find(t.ToId)?.Value is OpenQuestion { Status: QuestionStatus.Open or QuestionStatus.Deferred } q)
                f.Add(Info("TRC-005", FindingKind.Traceability, t.FromId,
                    $"'{t.FromId}' çözümlenmemiş soruya bağlı: {q.Id}. Bu nesne varsayıma dayanıyor olabilir."));
        }
    }

    // --------------------------------------------------------------- profil

    private static void Profile(ArchitectureModel m, List<ValidationFinding> f)
    {
        var profile = ProfileCatalog.Find(m.Project.TechProfileId);
        if (profile is null)
        {
            f.Add(Err("PRF-001", FindingKind.Profile, null, $"Bilinmeyen teknoloji profili '{m.Project.TechProfileId}'."));
            return;
        }
        if (!profile.IsSelectable)
            f.Add(Warn("PRF-002", FindingKind.Profile, null, $"'{profile.Name}' profili henüz doğrulanmadı."));

        foreach (var c in m.Components.Where(c => !string.IsNullOrWhiteSpace(c.Technology)))
        {
            var tech = c.Technology!.ToLowerInvariant();
            var hit = profile.ConflictingMarkers.FirstOrDefault(tech.Contains);
            if (hit is not null)
                f.Add(Warn("PRF-003", FindingKind.Profile, c.Id,
                    $"'{c.Technology}' teknolojisi {profile.Name} profiliyle uyumsuz görünüyor ('{hit}').",
                    "Profil kuralı; çalışma zamanı doğrulaması yerine geçmez."));
        }
    }

    // ------------------------------------------------------ inceleme önerileri

    private static void ReviewSuggestions(ArchitectureModel m, ModelIndex index, List<ValidationFinding> f)
    {
        foreach (var r in m.Relationships)
        {
            var target = index.Find(r.TargetId)?.Value as ArchComponent;
            var external = target?.Kind == ComponentKind.ExternalService;
            var asyncLike = r.Communication is CommunicationKind.Async or CommunicationKind.Event;
            if ((external || asyncLike) && string.IsNullOrWhiteSpace(r.FailureBehavior))
                f.Add(Warn("REV-001", FindingKind.ReviewSuggestion, r.Id,
                    "Hata davranışı tanımlı değil: bu çağrı başarısız olursa kullanıcı işlemi durmalı mı, ertelenmeli mi?",
                    "İş kuralı incelemesi (öneri); dış API koşulları bilinmiyorsa sonuç açık kalır."));
        }

        foreach (var r in m.Requirements.Where(r => r.Kind == RequirementKind.Quality && r.AcceptanceCriteria.Count == 0))
        {
            var text = r.Text.ToLowerInvariant();
            if (VagueTerms.Any(text.Contains))
                f.Add(Warn("REV-002", FindingKind.ReviewSuggestion, r.Id,
                    "Ölçülebilir değil: kritik işlem, beklenen yük ve hedef değer belirtilmeli (bilinmiyorsa açık konu olarak kaydedin).",
                    "§8.2: 'Hızlı olsun' ölçülebilir bir kalite gereksinimi sayılmaz."));
        }

        var externalRel = m.Relationships.FirstOrDefault(r =>
            index.Find(r.TargetId)?.Value is ArchComponent { Kind: ComponentKind.ExternalService });
        if (externalRel is not null)
        {
            var mentionsIdempotency =
                m.Decisions.Any(d => Mentions(d.Title) || Mentions(d.Choice) || Mentions(d.Rationale) || Mentions(d.Consequences))
                || m.Relationships.Any(r => Mentions(r.Description) || Mentions(r.FailureBehavior))
                || m.Requirements.Any(r => Mentions(r.Text));
            if (!mentionsIdempotency)
                f.Add(Info("REV-003", FindingKind.ReviewSuggestion, externalRel.Id,
                    "Dış servise yeniden deneme yapılırsa tekrar işleme davranışı (idempotency) tanımlı mı?",
                    "Dış servisin idempotency desteği doğrulanmadan çözüm eksiksiz sayılamaz."));
        }

        foreach (var d in m.Decisions.Where(d => d.ReviewStatus == ReviewStatus.Proposed))
        {
            var critical = m.TraceLinks.Any(t => t.Kind == TraceKind.Addresses && t.FromId == d.Id
                && index.Find(t.ToId)?.Value is Requirement { Priority: Priority.Critical });
            if (critical)
                f.Add(Info("REV-005", FindingKind.ReviewSuggestion, d.Id,
                    "Kritik gereksinime bağlı karar insan incelemesi bekliyor (§7.3)."));
        }
    }

    private static bool Mentions(string? s) => s?.Contains("idempot", StringComparison.OrdinalIgnoreCase) == true;

    // -------------------------------------------------------------- yardımcı

    private static ValidationFinding Err(string rule, FindingKind kind, string? obj, string msg, string? basis = null) =>
        new() { RuleId = rule, Kind = kind, Severity = Severity.Error, ObjectId = obj, Message = msg, Basis = basis };

    private static ValidationFinding Warn(string rule, FindingKind kind, string? obj, string msg, string? basis = null) =>
        new() { RuleId = rule, Kind = kind, Severity = Severity.Warning, ObjectId = obj, Message = msg, Basis = basis };

    private static ValidationFinding Info(string rule, FindingKind kind, string? obj, string msg, string? basis = null) =>
        new() { RuleId = rule, Kind = kind, Severity = Severity.Info, ObjectId = obj, Message = msg, Basis = basis };
}
