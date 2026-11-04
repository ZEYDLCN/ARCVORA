# ARCVORA — Adım adım yerelde kullanım (macOS + yerel Qwen)

Bu rehber sıfırdan başlayıp bir fikri, yerelde çalışan bir Qwen modeliyle mimariye dönüştürmeyi anlatır.
Komutlar macOS (Terminal) içindir; Linux'ta aynıdır, Windows için README'deki PowerShell adımlarına bakın.

> **Dürüst not:** Yerel modelin **kalitesi** burada ölçülmedi. Test paketi sahte bir model sunucusuyla ARCVORA'nın yerel sağlayıcı yolunu
> (istek biçimi, şemalı çıktı, iş kuyruğu, doğrulama, onarım, kabul) uçtan uca doğrular; gerçek Qwen çıktısı için aşağıdaki "Gerçek modelle dene" adımını kendi makinenizde çalıştırın.

## 1) Bir kez kurulacaklar

| Araç | Kurulum | Kontrol |
|---|---|---|
| .NET 8 SDK | <https://dotnet.microsoft.com/download> | `dotnet --list-sdks` → `8.0.x` görünmeli |
| Node.js 20+ | `brew install node` | `node -v` |
| PostgreSQL 16 | Docker Desktop kuruluysa `scripts/dev.sh` kendisi başlatır; yoksa Postgres.app / `brew install postgresql@16` | `psql --version` |
| Ollama | `brew install ollama` veya <https://ollama.com/download> | `ollama --version` |

## 2) Kodu alın

```bash
git clone https://github.com/ZEYDLCN/ARCVORA.git
cd ARCVORA
```

## 3) Yerel modeli hazırlayın (Ollama)

1. **Bağlamı büyütün.** Mimari taslağı uzun çıktıdır; varsayılan bağlam kısa kalırsa çıktı kesilir.
   ```bash
   OLLAMA_CONTEXT_LENGTH=16384 ollama serve
   ```
   Ollama uygulamasını (menü çubuğu) kullanıyorsanız `ollama serve` "address already in use" der; o zaman uygulamadan çıkıp yukarıdaki komutla başlatın
   ya da Ollama belgelerindeki macOS ortam değişkeni yöntemini kullanın.
2. **Modeli indirin** (yeni bir terminal sekmesinde). Etiketi kendiniz doğrulayın: <https://ollama.com/library> — örnekler: `qwen3:8b`, `qwen2.5:7b-instruct`.
   ```bash
   ollama pull <etiket>
   ollama list            # indirilen modelin adını buradan birebir kopyalayın
   ```
   Kaba bellek rehberi (Apple Silicon, birleşik bellek): 8 GB → 7–8B (4-bit), 16 GB ve üstü → 14B'ye kadar. Model belleğe sığmazsa çok yavaşlar.
3. **Modeli ARCVORA'dan bağımsız deneyin.** Bu betik sunucuya ulaşılıyor mu, şemalı (`json_schema`) çıktı çalışıyor mu ve yaklaşık token/sn kaç, söyler:
   ```bash
   scripts/check-local-llm.sh http://localhost:11434 <etiket>
   ```
   `✓ Şema kısıtlı çıktı çalışıyor` görmelisiniz. Görmüyorsanız Ollama'yı güncelleyin.

## 4) ARCVORA'yı yerel modelle başlatın

```bash
export Ai__Provider=local
export Ai__Local__BaseUrl=http://localhost:11434
export Ai__Local__Model=<etiket>        # ollama list'teki ad, birebir
export Ai__TimeoutSeconds=1800          # yerel üretim yavaş olabilir
export Jobs__Concurrency=1              # aynı anda tek üretim (bellek)
# Qwen3 gibi "düşünen" modellerde:
# export Ai__Local__DisableThinking=true

scripts/dev.sh
```

`Ai__Local__Model` boşsa API **bilerek başlamaz** (yanlış yapılandırma sessizce başka sağlayıcıya düşmez).
Sağlayıcıyı hiç ayarlamazsanız anahtarsız, kural tabanlı **buluşsal üretici** çalışır; arayüz bunu "buluşsal (AI değil)" diye etiketler.

Açıldığında: arayüz <http://localhost:5173>, API <http://localhost:5217/health>. Durdurmak için `Ctrl+C`.

## 5) Arayüzde bir fikri mimariye dönüştürün

1. **Kayıt ol** sekmesi → ad, e-posta, **en az 10 karakterlik** parola → *Hesap oluştur*.
2. İsterseniz önce **Örnek projeyi incele** ile hazır "Masraf yönetimi" projesini gezin (tuval, Mermaid görünümleri, kararlar, akışlar).
3. Kendi projeniz: **İlk mimarini oluştur** → proje adı + fikir metni (örn. "Çalışanlar masraf girer, yönetici onaylar. Fiş fotoğrafı yüklenir.") →
   teknoloji profili → **Projeyi oluştur**.
4. Sol menüde **Kaynaklar** → **Gereksinimleri çıkar**. Üretim arka planda çalışır; üstte iş durumu görünür (yerel modelde dakikalar sürebilir, **İptal et** vardır).
5. Sol menüde **Öneriler** → biten öneriye tıklayın. Sağda **etki özeti** ve **Kabul edilecek gruplar** çıkar.
   - Tüm grupları kabul için **Hepsini kabul et**; bazılarını seçmek için onay kutularını kaldırın → **Seçilenleri kabul et**.
   - Bir grup başka bir gruba bağlıysa (ör. izlenebilirlik bağlantıları çekirdeğe) bağımlılığı olmadan kabul edilemez; sistem nedenini söyler.
   - Beğenmediyseniz **Reddet**. Öneri bir taslaktır; kabul edilene kadar model değişmez.
6. **Mimari** → **Mimari taslak oluştur** → yine **Öneriler**'den inceleyip kabul edin. Sonuç **Tuval**'de (teknoloji logolarıyla), **Bileşenler (Mermaid)**, **Veri modeli** ve **Akışlar** sekmelerinde görünür.
7. Yeni bir istek: **Yeni talep ekle** → metni yazın → **Etkiyi analiz et**. Çıkan öneride hangi nesnelerin etkilendiği listelenir; kabul etmek yeni sürüm üretir.
   Arada model başka bir sürüme geçtiyse eski temelli öneri **kabul edilmez**, "yeniden değerlendir" istenir.
8. **Doğrulama**: şema/referans bulguları ve izlenebilirlik uyarıları. **Markdown** veya **Paket** ile dışa aktarın (her çıktı sürüm ve özet hash'iyle kaydedilir).
9. Her yerde **Ctrl+K** (⌘K) ile gereksinim, karar, bileşen, akış ve öneri araması.

Yerel modelle üretilen önerilerde etiket `AI: local:<etiket>` olarak görünür. Model hatalı taslak üretirse ARCVORA en fazla `Ai__MaxRepairs` (varsayılan 2) kadar onarım turu dener; olmazsa iş "başarısız" olur ve son kabul edilmiş model korunur.

## 6) Testleri çalıştırın

```bash
# Birim + entegrasyon (gerçek PostgreSQL gerekir)
dotnet test tests/Arcvora.Core.Tests
ARCVORA_TEST_PG="Host=localhost;Username=postgres;Password=postgres" dotnet test tests/Arcvora.Integration.Tests
cd web && npm test && cd ..

# Uçtan uca (geçici veritabanı + API + web açar, bitince temizler)
scripts/e2e.sh --no-ui        # yalnız API yolculuğu (Playwright gerekmez)
scripts/e2e.sh --local --no-ui   # sahte yerel modelle (istek biçimi + onarım turu dahil)

# Tarayıcı yolculuğu için bir kez:
cd web && npm i --no-save playwright && npx playwright install chromium && cd ..
scripts/e2e.sh                # buluşsal üretici + tarayıcı
scripts/e2e.sh --local        # sahte yerel model + tarayıcı
```

Uçtan uca testler şunları kapsar: açılış animasyonu, kayıt/giriş, proje, gereksinim çıkarımı, mimari taslak, kısmi kabul ve bağımlılık reddi,
bayat öneri (409), geri alma, dışa aktarma, çalışma alanı yalıtımı (404), tuval/Mermaid, arama, tema, mobil taşma, çıkış;
roller (okuyucu / düzenleyici / ayrı kabul yetkisi / yönetici), paralel kabulün tek sürüm üretmesi, çakışan düzenlemede `409 version_conflict`,
geçersiz yamanın modeli bozamaması, açık soru yanıtı, denetim kaydı, proje silme; `--local` ile ayrıca yavaş modelde iş iptali ve zaman aşımı.

### Gerçek modelle dene

API'yi 4. adımdaki ayarlarla (gerçek Ollama'ya bağlı) başlattıktan sonra, aynı yolculuğu gerçek model üzerinde koşabilirsiniz:

```bash
ARCVORA_API_URL=http://localhost:5217 LOCAL_MODEL=<etiket> E2E_JOB_TIMEOUT_MS=1800000 node scripts/e2e/local-llm.mjs
```

Bu komut kayıt olur, proje açar, gereksinimleri çıkarır ve mimari taslak ister; sağlayıcı etiketini (`local:<etiket>`), token kaydını ve
taslağın **doğrulamadan geçip öneri olarak listelendiğini** denetler. Gerçek sunucuda istek günlüğü bulunmadığından istek-biçimi ve onarım denetimleri atlanır
(onlar yalnız sahte sunucuyla sınanır). Çıktının *kalitesini* bu betik ölçmez: onu 5. adımı kendi fikirlerinizle yürüterek siz değerlendirirsiniz.

## Sorun giderme

| Belirti | Çözüm |
|---|---|
| `Ai:Local:Model` hatasıyla API açılmıyor | `Ai__Local__Model` boş: `ollama list` çıktısındaki adı verin. |
| `404 model not found` | Ad `ollama list` ile birebir aynı olmalı. |
| "Model çıktısı token sınırında kesildi" | `OLLAMA_CONTEXT_LENGTH` ve `Ai__Local__MaxOutputTokens` değerlerini artırın. |
| İş `timeout` ile bitiyor | `Ai__TimeoutSeconds` artırın veya daha küçük/hızlı model seçin. |
| "onarım hakkı doldu" + sorun listesi | Daha büyük model deneyin veya `Ai__MaxRepairs` değerini artırın (varsayılan 2; her onarım bir üretim daha demektir). |
| `password authentication failed` | `ConnectionStrings__Arcvora` ile kendi PostgreSQL bilgilerinizi verin. |
| `429` (kayıt/giriş) | Kimlik uçları dakikada sınırlıdır (`RateLimit__AuthPerMinute`, varsayılan 10); biraz bekleyin. |
| Port dolu (`5217`, `5173`) | Eski `dotnet`/`node` sürecini kapatın. |
