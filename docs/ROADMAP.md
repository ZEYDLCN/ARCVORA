# ARCVORA — Fazlandırılmış Yol Haritası

Kaynak: ürün dokümanı v1.0 (4 Kasım 2026), özellikle §10 (kapsam), §23 (yol haritası), §25 (MVP kabul listesi) ve §26 (uygulama sırası).

Bu belge **ne zaman** değil **hangi bitiş ölçütüyle** ilerlediğimizi anlatır. Takvim taahhüdü yoktur (§23).
Durum sütunu depodaki gerçek koda göre tutulur; "tamam" yalnızca testle doğrulanmış işler için yazılır.

İlk ürün dikey kesiti (§23):

> tek proje → onaylanan gereksinimler → mimari model → üç görünüm → yeni talep → etki incelemesi → yeni sürüm → Markdown çıktısı

## Faz özeti

| Faz | Ad | Doküman karşılığı | Bu depodaki karşılığı | Durum |
|----:|----|-------------------|-----------------------|:-----:|
| 0 | Problem doğrulama | §22, §23 Aşama 0 | `docs/PILOT_PLAYBOOK.md` (kod dışı hazırlık) | 🟡 rehber hazır; görüşmeler ve ödeme denemesi kod dışı, yapılmadı |
| 1 | Model temeli | §14, §15.2, §18, §26.1–2 | `Arcvora.Core` | ✅ |
| 2 | Çalışma alanı çekirdeği (erişim, sürüm, kabul) | §16.2, §17, §19, §26.3 | `Arcvora.Infrastructure`, `Arcvora.Api` | ✅ |
| 3 | AI taslak hattı | §15, §26.4 | `Arcvora.Core/Ai`, `Arcvora.Infrastructure/Ai`, worker | ✅ sahte LLM ve HTTP stub ile; ⬜ canlı sağlayıcı kalitesi ölçülmedi |
| 4 | Değişiklik etki motoru | §16, §26.5 | `Arcvora.Core/Impact`, proposal akışı | ✅ |
| 5 | Web çalışma alanı | §12 | `web/` | ✅ temel akış |
| 6 | Kalite ve güvenilirlik | §20, §26.6 | `tests/`, CI | 🟡 §20.2 testleri tamam; değerlendirme kümesi ve eşikler yok |
| 7 | Ücretli pilot hazırlığı | §21–22, §23 Aşama 5 | maliyet/hata görünürlüğü | ⬜ |
| 8 | Spring Boot profili | §11, §23 Aşama 6 | `ProfileCatalog` (planned) | ⬜ |
| 9 | Entegrasyonlar | §23 Aşama 7 | — | ⬜ |

> Numaralandırma doküman §23'ten farklıdır: §26'daki uygulama sırasına (model → erişim/sürüm → AI → etki) uymak için
> "Çalışma alanı çekirdeği" AI hattından **önce** gelir. Doküman fazı 3 (Çalışma alanı) burada 2 + 5'e bölünmüştür.

---

## Faz 0 — Problem doğrulama (kod dışı)

**Amaç:** Gerçek müşteri projesi teslim eden beş ekipte problemin ve ödeme isteğinin doğrulanması.

- Görüşme soruları, sinyal listesi ve pilot teklifi: `docs/PILOT_PLAYBOOK.md`.
- **Bitiş ölçütü:** Gerçek projede kullanım + ödeme denemesi. *Bu ölçüt yazılımla karşılanamaz; kod bu fazı ilerletmez.*

## Faz 1 — Model temeli

**Amaç:** Elle hazırlanmış bir modelden tutarlı çıktı almak (§26 adım 1–2).

Teslimler
- Ortak mimari model şeması (Project, Source, Requirement, Assumption, Decision, Component, Relationship, Entity, Flow, TraceLink, OpenQuestion, Actor, Endpoint).
- Kaynak (`origin`) ile onay durumunun (`reviewStatus`) ayrı alanlar olması.
- Patch modeli (upsert/remove, gruplu) ve patch uygulayıcı; sağlayıcıdan bağımsız sağlayıcı-geçici kimlik çözümü.
- Deterministik doğrulayıcı: şema, referans, tutarlılık, izlenebilirlik, profil ve *inceleme önerisi* kontrolleri — her bulgu türü ayrı.
- Görünüm üreticileri: Mermaid bileşen (flowchart), sequence, ER; güvenli kaçış (escape) kuralları.
- Markdown teknik devir çıktısı (tek dosya + paket) ve içerik hash'i.
- Örnek proje: şirket masraf yönetimi (§9) + iki aşamalı onay değişikliği.

**Bitiş ölçütü:** Örnek modelden üç görünüm ve Markdown çıktısı üretilir; geçersiz referans yakalanır; aynı model → aynı hash.

## Faz 2 — Çalışma alanı çekirdeği

**Amaç:** Erişim kontrolü, değişmez sürümler ve güvenli kabul (§26 adım 3).

Teslimler
- Kimlik doğrulama (JWT), çalışma alanı, üyelik, roller (yönetici/düzenleyici/okuyucu) + ayrı kabul yetkisi.
- Proje kapsamlı nesne bazlı yetkilendirme; başka çalışma alanındaki proje için `404`.
- Değişmez, doğrusal sürüm geçmişi; sürüm başına model hash'i; geri dönüş yeni sürüm olarak.
- `baseVersion` kontrollü kabul; eski temel sürüme dayalı öneri güncel modeli ezmez (`409`).
- Tekrarlanan kabul çift sürüm oluşturmaz (idempotent).
- Dayanıklı iş kayıtları (claim/lease/yeniden deneme/iptal) ve ayrı worker süreci.
- Audit kaydı; dışa aktarma kayıtları (model sürümü + profil + üretici sürümü + hash).

**Bitiş ölçütü:** §20.2'deki kritik testlerin erişim, kabul ve worker maddeleri geçer.

## Faz 3 — AI taslak hattı

**Amaç:** AI üretimini sağlam modelin önüne eklemek (§26 adım 4). LLM öneri üretir; kabul, yetki ve yazım uygulama kurallarındadır.

Teslimler
- `ILlmClient` sağlayıcı arayüzü; Anthropic Messages API istemcisi (zorunlu tool-use ile şemalı çıktı) ve OpenAI uyumlu yerel sunucu istemcisi (Qwen/Ollama/llama.cpp; şema kısıtlı çıktı). *Yerel istemci testle doğrulandı; gerçek Qwen kalitesi ölçülmedi.*
- `IArchitectureGenerator`: gereksinim çıkarma, mimari taslak, değişiklik analizi.
- Boru hattı: üretim → kimlik çözümü → patch uygulama → doğrulama → en fazla N onarım denemesi → inceleme.
- İş başına token/süre/deneme bütçesi; sınırsız düzeltme döngüsü yok.
- Anahtarsız çalışan, **AI olmadığı açıkça etiketlenen** buluşsal (heuristic) üretici: geliştirme ve demo içindir.
- Güvenilmeyen kaynak içeriği: kaynak metin talimat değil veri olarak sunulur; LLM'in veritabanına/dosyaya erişimi yoktur.

**Bitiş ölçütü:** Sahte (fake) LLM ile boru hattı uçtan uca test edilir; hata/timeout son kabul edilmiş modeli değiştirmez.
*Canlı sağlayıcı kalitesi bu fazın kodla kanıtlanabilir kısmı değildir; değerlendirme kümesi + pilot gerektirir (Faz 6/7).*

## Faz 4 — Değişiklik etki motoru

**Amaç:** Yeni talebin tasarıma etkisini göstermek ve güvenle yeni sürüme dönüştürmek (§16, §26 adım 5).

Teslimler
- Etki türleri: **doğrudan / bağımlı / olası / belirsiz**; graf erişilebilirliği ≠ "kesin değişir".
- Alan bazlı özet (gereksinim, veri modeli, API, akış, bileşen, karar, açık soru).
- Grup bazlı kısmi kabul; bağımlılığı kopan seçim reddedilir (yeni bileşeni reddedip ona bağlantıyı kabul etmek engellenir).
- Örnek: iki aşamalı onay demosu (§9.5).

**Bitiş ölçütü:** İki aşamalı onay değişikliği analiz edilir, kısmen/tamamen kabul edilir, yeni sürüm oluşur, önceki sürüm erişilebilir kalır.

## Faz 5 — Web çalışma alanı

**Amaç:** §12'deki üç panelli çalışma yüzeyi.

Teslimler
- Sol: kapsam/gereksinimler/kararlar/açık sorular. Orta: Mermaid görünümleri. Sağ: seçili nesnenin gerekçesi, bağlantıları, değişiklik incelemesi.
- Üst çubuk: aktif sürüm, teknoloji profili, taslak (bekleyen öneri) durumu, dışa aktarma.
- Mobilde çekmece menü; diyagramın metinsel karşılığı her zaman erişilebilir.
- Tasarım dili (4 Kasım 2026 revizyonu): kabartmalı buzlu cam zemin, yarı saydam (glassmorphism) kenar çubuğu ve kartlar, ⌘K arama, marka logolu mimari tuvali (`ArchCanvas`), tema anahtarı.
- Açılış logo animasyonu (`Intro`): ~2,5 sn, oturum başına bir kez, atlanabilir, `prefers-reduced-motion` uyumlu; yalnızca `transform`/`opacity` (README "Açılış animasyonu").
- Durumlar: boş, analiz sürüyor, açıklama bekliyor, taslak hazır, doğrulama hatası, sağlayıcı hatası, iptal, kabul.
- Durumlar yalnızca renkle anlatılmaz (etiket + simge).

**Bitiş ölçütü:** Örnek proje UI'dan baştan sona gezilebilir; build ve tip denetimi CI'da geçer.

## Faz 6 — Kalite ve güvenilirlik (sürekli)

- §20.2 kritik testleri otomatikleştirilir (entegrasyon testleri gerçek PostgreSQL ile).
- Değerlendirme kümesi (masraf, randevu, abonelik, envanter, destek) — *örnek içerikleri uzman incelemesi gerektirir.*
- Başarı eşikleri **pilot öncesi kararlaştırılır**; bu depoda ölçülmemiş doğruluk/zaman kazancı yüzdesi yer almaz.

## Faz 7 — Ücretli pilot hazırlığı

- Kabul edilen paket başına gerçek sağlayıcı maliyeti ölçümü, hata takibi, geri bildirim.
- Veri saklama/silme politikası ve sağlayıcı koşulları doğrulaması (§19).

## Faz 8 — Spring Boot profili

- Aynı değerlendirme kümesiyle ASP.NET Core ile kıyaslanmadan **açılmaz**; `ProfileCatalog` içinde `planned` kalır ve seçilemez (§11.2).

## Faz 9 — Entegrasyonlar

- Git depo analizi, PR etkileri, kurum kuralları: yalnızca somut müşteri ihtiyacı ve ölçülen değer sonrası (§10, §23 Aşama 7).

---

## Kapsam dışı (bilinçli)

Tam uygulama üretimi, Git deposundan mimari çıkarımı, canlı yük/maliyet simülasyonu, ham Mermaid içe aktarma (§10, §14.3).
İndirilen Mermaid dosyalarındaki dış düzenlemeler modele otomatik aktarılmaz.

## Açık dış bağımlılıklar

- Marka/alan adı/sosyal hesap doğrulaması (§2.1) — kod dışı.
- LLM sağlayıcısının veri saklama ve eğitim koşulları (§19) — entegrasyon öncesi doğrulanmalı.
- Profildeki sürüm aralıklarının resmî kaynaklardan doğrulanması (§11.2) — profil `verification: pending` olarak işaretlidir.

---

## Durum kaydı (4 Kasım 2026)

**Doğrulanan** (otomatik): 85 çekirdek birim + 77 entegrasyon (gerçek PostgreSQL) + 22 web testi; tip denetimi ve üretim derlemesi.
Ayrıca gerçek tarayıcıda (Playwright) elle sürülen akışlar: kayıt → örnek proje → tüm görünümler → etki incelemesi → kabul → eski sürüm
(salt okunur) → mobil; fikir metninden gereksinim çıkarımı → mimari taslak; düşmanca etiketli modelin gerçek Mermaid ile çizimi.

**Doküman §20.2 kritik testleri:** tamamı otomatik (bkz. `tests/Arcvora.Integration.Tests`).

**Bilinen boşluklar / sıradaki işler (öncelik sırasıyla):**

1. Canlı LLM ile değerlendirme kümesi (masraf, randevu, abonelik, envanter, destek) ve **pilot öncesi karar verilecek** başarı eşikleri. *Uzman içeriği gerektirir.*
2. `IMPLEMENTATION_PLAN.md` (sıralı geliştirme işleri ve bağımlılıkları) — paket çıktısında henüz yok.
3. Maliyet görünürlüğü (kabul edilen paket başına sağlayıcı maliyeti), hata izleme, geri bildirim toplama (Faz 7).
4. Saklama/silme politikası, parola sıfırlama, e-posta doğrulama, yedeklerde veri ömrü.
5. Üretim dağıtımı: Dockerfile/dağıtım betikleri yok (yalnızca geliştirme için PostgreSQL compose dosyası).
6. Profil sürüm aralıklarının resmî kaynaklardan doğrulanması (`verification: pending`).
7. Spring Boot profili: aynı değerlendirme kümesiyle kıyaslanmadan açılmaz.
