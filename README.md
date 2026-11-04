# ARCVORA

> **Turn intent into architecture.** Fikrini anlat. Kararlarını netleştir. Mimariyi birlikte geliştir.

ARCVORA; yazılım fikirlerini ve gereksinimlerini gerekçeli mimari kararlara, birbiriyle tutarlı diyagramlara ve
geliştiriciye teslim edilebilecek bir teknik plana dönüştüren; yeni talepler geldikçe bu planın nasıl değişmesi
gerektiğini gösteren bir çalışma alanıdır. Mermaid yalnızca sistemin ürettiği görünümlerden biridir; kalıcı kayıt,
gereksinimler, kararlar, bileşenler ve bunların birbirleriyle ilişkileridir.

**Durum:** ürün dokümanındaki ilk dikey kesit çalışıyor ve testli:
`tek proje → onaylanan gereksinimler → mimari model → üç görünüm → yeni talep → etki incelemesi → yeni sürüm → Markdown çıktısı`.
Ürün doğrulaması (gerçek pilotlar, fiyat, kalite eşikleri) **yapılmadı**; bu depo yazılım tarafını kapsar. Ayrıntı: [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Neler var?

| Alan | Açıklama |
|---|---|
| Ortak model | Gereksinim, varsayım, karar, bileşen, bağlantı, varlık, akış, endpoint, izlenebilirlik ve açık soru nesneleri. **Kaynak** (`origin`: kullanıcı / sistem önerisi / varsayım) ile **onay durumu** (`reviewStatus`) ayrı alanlardır. |
| Doğrulayıcı | Şema, referans, tutarlılık, izlenebilirlik, profil kontrolleri (deterministik) ve ayrı türde **inceleme önerileri** (kural tabanlı, kanıt değil). |
| Görünümler | Bileşen (flowchart), sequence, ER — hepsi aynı model sürümünden türetilir; güvenli Mermaid kaçışı. |
| Sürümler | Değişmez, doğrusal geçmiş; sürüm başına içerik hash'i; geri dönüş yeni sürüm olarak. |
| Öneri → kabul | Değişiklikler önce **öneri** (taslak) olarak hazırlanır. Kabul `baseVersion` kontrollüdür: eski temel sürüme dayalı öneri güncel modeli ezemez (`409`), tekrar kabul çift sürüm üretmez, kısmi kabul bağımlılık tutarlılığını korur. |
| Etki motoru | **Doğrudan / bağımlı / olası / belirsiz** etki; alan bazlı özet; cevap bekleyen sorulara bağlı belirsizlikler. |
| AI hattı | Üretim → kimlik çözümü → koruyucu → doğrulama → en fazla N onarım → etki analizi. Token/süre/deneme bütçesi. Kaynak metin güvenilmeyen veridir. |
| İş kuyruğu | PostgreSQL üzerinde dayanıklı işler: `SKIP LOCKED` claim, lease + kalp atışı, worker çökmesi kurtarma, yeniden deneme, iptal, `Idempotency-Key`. |
| Erişim | JWT, çalışma alanı üyeliği, roller (yönetici/düzenleyici/okuyucu) + ayrı kabul yetkisi. Başka çalışma alanındaki proje için `404`. |
| Çıktı | Tek dosya Markdown veya paket (zip); her çıktı model sürümü, profil sürümü, üretici sürümü ve hash ile kaydedilir. |
| Web | React + TypeScript çalışma alanı: gereksinimler/kararlar/sorular, diyagramlar, gerekçe ve ilişki zinciri, değişiklik incelemesi, mobil sekmeler, koyu/açık tema. |

## Hızlı başlangıç

Gereksinimler: .NET 8 SDK, Node 22, PostgreSQL 16 (ya da Docker).

```bash
docker compose -f docker-compose.dev.yml up -d       # yalnızca PostgreSQL

cd src/Arcvora.Api && dotnet run                      # Development: otomatik migrasyon, worker API içinde çalışır
# başka bir terminalde:
cd web && npm ci && npm run dev                       # http://localhost:5173  (/api istekleri API'ye yönlenir)
```

Kayıt olun ve **“Örnek projeyi incele”** deyin: elle hazırlanmış bir masraf yönetimi modeli ve bekleyen “iki aşamalı onay”
değişikliği gelir. Kendi fikrinizle denemek için **“İlk mimarini oluştur”**.

### Tek süreçten sunum

```bash
cd web && npm ci && npm run build
cp -r dist ../src/Arcvora.Api/wwwroot                 # API, wwwroot varsa SPA'yı da sunar
```

### Üretimde ayrı worker

API'de `Worker__InProcess=false` bırakın ve `src/Arcvora.Worker` projesini ayrı süreç olarak çalıştırın (aynı veritabanı).
Production'da `Database__MigrateOnStartup=false` tutup migrasyonu dağıtım adımında uygulayın
(`dotnet ef database update -p src/Arcvora.Infrastructure -s src/Arcvora.Infrastructure`).

## Yapılandırma

Ortam değişkenleri `__` ile iç içe anahtar yazar (örn. `Ai__Provider`). Örnekler: [`.env.example`](.env.example).

| Anahtar | Varsayılan | Açıklama |
|---|---|---|
| `ConnectionStrings__Arcvora` | yerel PostgreSQL | Veritabanı |
| `Jwt__Key` | — (zorunlu, ≥32 karakter) | Eksikse API **başlamaz**. Development için `appsettings.Development.json`'da yalnızca geliştirme anahtarı vardır. |
| `Ai__Provider` | `heuristic` | `heuristic`: anahtarsız, **yapay zekâ olmayan** anahtar sözcük iskeleti (arayüzde böyle etiketlenir). `anthropic`: Anthropic Messages API. |
| `Ai__Anthropic__ApiKey` / `Model` | — / `claude-sonnet-5-5` | `anthropic` seçilip anahtar yoksa uygulama başlangıçta hata verir. |
| `Ai__MaxTokensPerJob`, `Ai__MaxRepairs`, `Ai__TimeoutSeconds` | 120000, 2, 300 | İş başına bütçe; sınırsız düzeltme döngüsü yoktur. |
| `Jobs__*` | bkz. `JobOptions` | Yoklama aralığı, lease süresi, eşzamanlılık, deneme sayısı. |
| `Worker__InProcess` | `false` | `true`: worker API içinde (yalnızca geliştirme kolaylığı). |

> **AI sağlayıcısını açmadan önce:** proje kaynak metinleri ticari sır içerebilir ve olduğu gibi sağlayıcıya gönderilir.
> Sağlayıcının veri saklama ve eğitim koşullarını doğrulayın (doküman §19).

## Testler

```bash
dotnet test                       # birim + entegrasyon; entegrasyon testleri gerçek PostgreSQL ister
cd web && npm run typecheck && npm test
```

Entegrasyon testleri her sınıf için geçici bir veritabanı oluşturur. Bağlantı için `ARCVORA_TEST_PG`
(varsayılan: `Host=localhost;Username=postgres;Password=postgres`). CI: [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

Kapsanan kritik senaryolar (doküman §20.2): başka çalışma alanına ait proje/sürüme erişimin reddi; aynı kabulün tekrarı ve
eşzamanlı kabulün tek sürüm üretmesi; eski temel sürüme ait önerinin güncel modeli ezmemesi; worker çökmesi sonrası güvenli
yeniden işleme; silinen bileşene bağlı ilişkinin tespiti; görünümlerin doğru sürümden üretilmesi; düşmanca Mermaid girdisi;
LLM/sağlayıcı hatasında son kabul edilmiş modelin korunması.

## Depo yapısı

```
src/Arcvora.Core            Alan modeli, patch, doğrulama, görünümler, etki motoru, AI hattı (saf, bağımlılıksız)
src/Arcvora.Infrastructure  EF Core/PostgreSQL, sürüm deposu, kabul, iş kuyruğu, Anthropic istemcisi, kimlik
src/Arcvora.Api             Minimal API + (varsa) SPA sunumu
src/Arcvora.Worker          Ayrı üretim işleyicisi
web/                        React + TypeScript çalışma alanı
tests/                      Birim (Core) ve entegrasyon (gerçek PostgreSQL) testleri
docs/                       ROADMAP, ARCHITECTURE, PILOT_PLAYBOOK
```

## MVP kabul listesi (doküman §25) — durum

| # | Madde | Durum |
|--:|---|---|
| 1 | Fikir ve teknoloji profili kaydı | ✅ test + arayüz |
| 2 | Kaynağa bağlı gereksinimler, düzenleme | ✅ test + arayüz |
| 3 | Mimariyi etkileyen açık sorular (turda ≤3) | ✅ sunucuda zorlanır |
| 4 | Bilgi / varsayım / sistem önerisi ayrımı | ✅ `origin` ≠ `reviewStatus` |
| 5 | Karar gerekçeleri ve ilgili gereksinimler | ✅ izlenebilirlik zinciri |
| 6 | Üç görünüm aynı model sürümünden | ✅ test |
| 7 | Geçersiz referans/şema hataları yakalanıyor | ✅ test |
| 8 | Yeni talep için değişiklik önerisi ve açık kararlar | ✅ örnek + sahte LLM ile test; **canlı LLM kalitesi ölçülmedi** |
| 9 | Eski sürüme dayalı kabul güncel modeli ezmiyor | ✅ test |
| 10 | Önceki sürümler korunuyor | ✅ test |
| 11 | Model sürümüne bağlı teknik teslim | ✅ Markdown + paket (`IMPLEMENTATION_PLAN.md` henüz yok) |
| 12 | Hata/timeout son kabul edilmiş modeli bozmuyor | ✅ test |
| 13 | Projeler arası yetkisiz erişim engelleniyor | ✅ test |
| 14 | Gerçek pilotta başlangıç + değişiklik turu | ⬜ kod dışı — bkz. [`docs/PILOT_PLAYBOOK.md`](docs/PILOT_PLAYBOOK.md) |

## Bilinen sınırlar (dürüst liste)

- **Canlı Anthropic çağrısı bu ortamda denenmedi** (API anahtarı yok). İstemcinin HTTP sözleşmesi ve hata sınıflandırması stub ile test edildi; üretim kalitesi bir değerlendirme kümesi ve pilotla ölçülmelidir. Doküman bu aşamada ölçülmemiş doğruluk/zaman kazancı iddiası içermez; bu depo da içermez.
- Buluşsal sağlayıcı bir **iskelet** üretir (kabul ölçütü, akış, endpoint uydurmaz) ve değişiklik talebi için tasarım değişikliği önermez; yalnızca talebi gereksinim adayı olarak kaydeder ve olası etkileri işaretler.
- Spring Boot profili `planned` olarak listelenir ve **seçilemez** (doküman §11.2).
- Profil sürüm aralıkları resmî kaynaklardan doğrulanmadı (`verification: pending`).
- Parola sıfırlama, e-posta doğrulama, refresh token, saklama/silme politikası ve yedeklerde veri ömrü tanımlı değil.
- Çok kullanıcılı dal/birleştirme yok; geçmiş doğrusaldır.
- Marka adı, alan adı ve sosyal hesap uygunluğu kontrol edilmedi (doküman §2.1).
