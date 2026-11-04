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
| Web | React + TypeScript çalışma alanı: taneli renkli zemin üzerinde yüzen kenar çubuğu (daraltılabilir, ⌘K / Ctrl+K arama), **logolu mimari tuvali** (noktalı akış çizgileri, teknoloji yığını), buzlu cam (glassmorphism) görünümü, Mermaid görünümleri, gerekçe ve ilişki zinciri, değişiklik incelemesi, mobilde çekmece menü, koyu/açık tema. |

## Yerelde çalıştırma (adım adım)

> macOS + yerel Qwen/Ollama ile baştan sona arayüz yolculuğu için: [`docs/KULLANIM.md`](docs/KULLANIM.md).

### 0) Bir kez kurulacaklar

| Araç | Sürüm | Not |
|---|---|---|
| .NET SDK | 8.x | `dotnet --version` → `8.…` |
| Node.js | 20 veya üzeri | `node --version` |
| PostgreSQL | 16 | **Ya** Docker Desktop (önerilen, tek komut) **ya da** PostgreSQL'i doğrudan kurun |
| Git | — | |

### 1) Kodu alın

```bash
git clone https://github.com/ZEYDLCN/ARCVORA.git
cd ARCVORA
git checkout feat/arcvora-mvp        # çalışma bu dalda; main'e birleştirilene kadar
```

### 2a) Tek komutla (macOS / Linux / WSL / Windows'ta Git Bash)

```bash
scripts/dev.sh
```

PostgreSQL'i (Docker ile) başlatır, hazır olmasını bekler, `web` bağımlılıklarını kurar, API'yi ve web arayüzünü birlikte çalıştırır. `Ctrl+C` hepsini kapatır.
PostgreSQL'i kendiniz yönetiyorsanız: `ARCVORA_SKIP_DB=1 scripts/dev.sh`.

### 2b) Elle (Windows PowerShell dahil — üç terminal)

**Terminal 1 — veritabanı** (Docker yoksa kendi PostgreSQL'inizi kullanın, bkz. aşağıdaki not):

```powershell
docker compose -f docker-compose.dev.yml up -d
```

**Terminal 2 — API** (migrasyon otomatik uygulanır, işleyici API içinde çalışır):

```powershell
cd src/Arcvora.Api
dotnet run
```

`Now listening on: http://localhost:5217` satırını görün.

**Terminal 3 — web arayüzü:**

```powershell
cd web
npm ci
npm run dev
```

### 3) Açın

Tarayıcıda **http://localhost:5173** → **Kayıt ol** → **“Örnek projeyi incele”**. Elle hazırlanmış masraf yönetimi modeli, mimari tuval ve bekleyen “iki aşamalı onay” değişikliği gelir.
Kendi fikrinizle: **“İlk mimarini oluştur”**. API anahtarı olmadan çalışan buluşsal sağlayıcı bir *iskelet* üretir (arayüzde “AI değil” etiketli); gerçek model için bkz. [`docs/LOCAL_LLM.md`](docs/LOCAL_LLM.md) veya `Ai__Provider=anthropic`.

> **Kendi PostgreSQL'iniz varsa** (Docker yok): kullanıcı/parola `postgres`/`postgres` değilse bağlantıyı verin ve veritabanı adı olarak yeni bir ad kullanın — ARCVORA veritabanını ve tabloları kendisi oluşturur.
> PowerShell: `$env:ConnectionStrings__Arcvora="Host=localhost;Database=arcvora;Username=KULLANICI;Password=PAROLA"` · bash: `export ConnectionStrings__Arcvora="Host=localhost;Database=arcvora;Username=…;Password=…"` (API'yi başlatmadan önce, aynı terminalde).

### Sık karşılaşılan sorunlar

| Belirti | Çözüm |
|---|---|
| API açılırken `Failed to connect to 127.0.0.1:5432` / `Connection refused` | PostgreSQL çalışmıyor. `docker compose -f docker-compose.dev.yml up -d` veya yerel PostgreSQL servisini başlatın. |
| `password authentication failed` | Parola farklı: yukarıdaki `ConnectionStrings__Arcvora` ile kendi bilgilerinizi verin. |
| Web açılıyor ama giriş/kayıt “Bağlantı kurulamadı” | API çalışmıyor ya da farklı portta. Web'i `VITE_API_TARGET=http://localhost:PORT npm run dev` ile başlatın (PowerShell: `$env:VITE_API_TARGET="http://localhost:PORT"`). |
| `5217` / `5173` portu dolu | Başka bir süreç kullanıyor; kapatın ya da `ASPNETCORE_URLS` ve `VITE_API_TARGET` ile port değiştirin. |
| `Jwt:Key en az 32 karakter olmalı` | Yalnızca Development dışında görülür: `Jwt__Key` verin. `dotnet run` varsayılan olarak Development kullanır; farklı bir ortam değişkeni (`ASPNETCORE_ENVIRONMENT`) tanımlıysa kaldırın. |
| `npm ci` hata veriyor | Node 20+ olduğundan emin olun; `web/node_modules` klasörünü silip yeniden deneyin. |
| İş “Kuyrukta” kalıyor | Worker API içinde çalışır (`Worker:InProcess`, Development'ta açık). Ayrı worker'ı yalnızca üretimde kullanırsınız. |

### Testleri çalıştırmak

```bash
dotnet test                         # entegrasyon testleri çalışan bir PostgreSQL ister (ARCVORA_TEST_PG)
cd web && npm test
```

### Tek süreçten sunum (üretim benzeri)

```bash
cd web && npm ci && npm run build
cp -r dist ../src/Arcvora.Api/wwwroot                 # API, wwwroot varsa SPA'yı da sunar
```

### Üretimde ayrı worker

API'de `Worker__InProcess=false` bırakın ve `src/Arcvora.Worker` projesini ayrı süreç olarak çalıştırın (aynı veritabanı).
Production'da `Database__MigrateOnStartup=false` tutup migrasyonu dağıtım adımında uygulayın
(`dotnet ef database update -p src/Arcvora.Infrastructure -s src/Arcvora.Infrastructure`).

## Logolar ve marka işaretleri

Teknoloji logoları ([simple-icons](https://simpleicons.org), CC0) paketinden, **resmî marka renkleriyle** gelir ve yalnızca bir bileşenin hangi teknolojiyi
kullandığını belirtmek için gösterilir (tuvaldeki düğümler, teknoloji yığını şeridi, profil kartları, giriş sayfası). Markalar ilgili sahiplerine aittir;
bu, ARCVORA'nın onlarla bir ortaklığı veya onayı olduğu anlamına gelmez. Bileşenin `technology` alanındaki serbest metin anahtar sözcüklerle eşlenir
(`web/src/lib/techLogo.ts`); eşleşme yoksa tür simgesi kullanılır, logo uydurulmaz.

Yalnızca kullanılan 43 logo `web/src/lib/logos.generated.ts` içinde tutulur. Yenilemek veya eklemek için: `cd web && npm run gen:logos`.
AWS, Azure ve SQL Server logoları simple-icons paketinden kaldırıldığı için dahil değildir.

## Açılış animasyonu

Uygulama ilk açıldığında ~2,5 sn'lik bir logo animasyonu oynar: belirme ve ölçek → 360° dönüş → lavanta ışık ve parıltı → marka adı → uygulamaya yumuşak geçiş.
Logo yeniden çizilmedi; mevcut `Logo` bileşeni kullanılır. Renkler marka belirteçlerinden gelir (`--brand-ink #091A20`, `#153D46`, `#A98AF2`, `#72BFC0`, `#D4E7D2`).

- Yalnızca CSS `transform` / `opacity` ile hareket eder; yerleşim kaymaz (ölçülen CLS: 0).
- Oturum başına bir kez oynar (`sessionStorage`); tıklama veya herhangi bir tuş ile atlanır. Yeniden izlemek için adrese `?intro` ekleyin.
- `prefers-reduced-motion` açıksa dönüş ve parıltı yok; kısa bir solma ile geçer.
- Animasyon bittiğinde (`animationend`) kendiliğinden kalkar; olay hiç gelmezse 4,5 sn'lik güvenlik zamanlayıcısı devreye girer, yani uygulamayı engelleyemez.
- Kodu: `web/src/components/Intro.tsx`, `web/src/intro.css`, `web/src/lib/intro.ts`.

Ölçümler başsız (headless) Chromium'da, yazılım render ile alındı; gerçek GPU'da kare hızı bundan iyi veya farklı olabilir. Cam katmanlı uygulamaya geçiş anı en ağır kısım.

## Yapılandırma

Ortam değişkenleri `__` ile iç içe anahtar yazar (örn. `Ai__Provider`). Örnekler: [`.env.example`](.env.example).

| Anahtar | Varsayılan | Açıklama |
|---|---|---|
| `ConnectionStrings__Arcvora` | yerel PostgreSQL | Veritabanı |
| `Jwt__Key` | — (zorunlu, ≥32 karakter) | Eksikse API **başlamaz**. Development için `appsettings.Development.json`'da yalnızca geliştirme anahtarı vardır. |
| `Ai__Provider` | `heuristic` | `heuristic`: anahtarsız, **yapay zekâ olmayan** anahtar sözcük iskeleti (arayüzde böyle etiketlenir). `anthropic`: Anthropic Messages API. `local`: OpenAI uyumlu yerel sunucu (Ollama, llama.cpp, vLLM; ör. **Qwen**) — bkz. [`docs/LOCAL_LLM.md`](docs/LOCAL_LLM.md). |
| `Ai__Anthropic__ApiKey` / `Model` | — / `claude-sonnet-5-5` | `anthropic` seçilip anahtar yoksa uygulama başlangıçta hata verir. |
| `Ai__Local__BaseUrl` / `Model` | `http://localhost:11434` / — | `local` için; `Model` boşsa uygulama başlamaz. Bağlam penceresi **en az 16k** olmalı. |
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

Uçtan uca (gerçek PostgreSQL + API + web + isteğe bağlı sahte yerel model; geçici veritabanını kendisi açıp siler):

```bash
scripts/e2e.sh --no-ui           # API yolculuğu, roller/yetkiler, eşzamanlılık, yaşam döngüsü (112 doğrulama)
scripts/e2e.sh --local --no-ui   # + sahte yerel model: istek biçimi, onarım turu, yavaş modelde iş iptali ve zaman aşımı
scripts/e2e.sh                   # + tarayıcı yolculuğu (Playwright: cd web && npm i --no-save playwright && npx playwright install chromium)
```

Sahte model ([`scripts/e2e/mock-llm.mjs`](scripts/e2e/mock-llm.mjs)) ARCVORA'nın `Ai__Provider=local` yolunu sınamak içindir; **gerçek model kalitesi hakkında bir şey söylemez**.
Uçtan uca testler henüz CI'da çalıştırılmıyor (GitHub Actions'ta denenmedi).

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

- **Yerel model (Qwen) ile gerçek çalıştırma yapılmadı**: istemci, şema ve bağlantı testle ve sahte OpenAI uyumlu sunucuyla doğrulandı; model ağırlıkları bu ortamda indirilemedi. Kendi makinenizde denemek için [`docs/LOCAL_LLM.md`](docs/LOCAL_LLM.md) ve `scripts/check-local-llm.sh`.
- **Canlı Anthropic çağrısı bu ortamda denenmedi** (API anahtarı yok). İstemcinin HTTP sözleşmesi ve hata sınıflandırması stub ile test edildi; üretim kalitesi bir değerlendirme kümesi ve pilotla ölçülmelidir. Doküman bu aşamada ölçülmemiş doğruluk/zaman kazancı iddiası içermez; bu depo da içermez.
- Buluşsal sağlayıcı bir **iskelet** üretir (kabul ölçütü, akış, endpoint uydurmaz) ve değişiklik talebi için tasarım değişikliği önermez; yalnızca talebi gereksinim adayı olarak kaydeder ve olası etkileri işaretler.
- Spring Boot profili `planned` olarak listelenir ve **seçilemez** (doküman §11.2).
- Profil sürüm aralıkları resmî kaynaklardan doğrulanmadı (`verification: pending`).
- Parola sıfırlama, e-posta doğrulama, refresh token, saklama/silme politikası ve yedeklerde veri ömrü tanımlı değil.
- Çok kullanıcılı dal/birleştirme yok; geçmiş doğrusaldır.
- Marka adı, alan adı ve sosyal hesap uygunluğu kontrol edilmedi (doküman §2.1). Arayüzdeki Arcvora logosu özgün bir çalışma işaretidir; kesin marka kimliği değildir.
- Mimari tuvali, bileşenleri **türe göre** soldan sağa dizer (istemci → API → modül → işleyici → veri → dış servis); sürükle-bırak düzenleme yoktur ve çok geniş mimarilerde yatay kaydırma gerekir.
