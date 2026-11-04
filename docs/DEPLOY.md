# Ücretsiz dağıtım: Render (uygulama) + Neon (veritabanı)

Amaç: ARCVORA'yı kredi kartı ve aylık ücret olmadan, internetten açılan çalışır bir adrese koymak.
Uygulama **tek imaj, tek süreç**tir (arayüz + API + arka plan işleyicisi); dışarıdan yalnızca PostgreSQL gerekir.

> **Doğrulama durumu (dürüst not).** Dağıtım yapılandırması bu depoda `scripts/e2e.sh --prod` ile sınandı: yayınlanmış uygulama,
> `wwwroot`'ta derlenmiş arayüz, `Production` ortamı, `DATABASE_URL` (`postgres://…`), uygulama içi worker, proxy başlıkları;
> tüm API ve tarayıcı yolculukları bu yapılandırmada geçti. **`Dockerfile` ise bu oturumda derlenemedi** (Docker çalıştırılamadı);
> yalnızca aşamaları ayrı ayrı elle çalıştırıp doğruladım. İlk `docker build` hatası olursa lütfen çıktıyı paylaşın.
> Aşağıdaki barındırıcı sınırları internetten **bu oturumda** okunan kaynaklara dayanır ve değişebilir; kaydolmadan önce bağlantılardaki güncel sayfaya bakın.

## Ne bekleyeceksiniz (ücretsiz katmanın bedeli)

| Konu | Durum |
|---|---|
| Uyuma | Render ücretsiz web servisi 15 dk istek gelmezse uyur; ilk istek yaklaşık 1 dk sürebilir ([Render](https://render.com/docs/free)). |
| Veritabanı | Render'ın ücretsiz Postgres'i **30 günde süresi dolar** ([Render](https://render.com/docs/free)); bu yüzden veritabanı için Neon önerilir. |
| Neon | Kalıcı ücretsiz plan; boştayken 5 dk sonra uyur, aylık bilgi işlem saati ve depolama sınırı vardır. Kaynaklar 0,5 GB mı 1 GB mı konusunda çelişiyor; güncel sayfaya bakın ([Neon](https://neon.com/pricing)). |
| Yedek | Ücretsiz katmanda yedeğe güvenmeyin. Önemli veriyi düzenli olarak **Markdown/Paket olarak dışa aktarın**. |
| AI | Varsayılan **buluşsal üretici** (anahtarsız, AI değildir; arayüzde öyle etiketlenir). Gerçek model eklemek aşağıda. |

## 1) Veritabanı: Neon

1. <https://neon.com> → hesap aç → **New project** (bölge: size yakın).
2. **Connection string**'i kopyalayın. **Pooled (…-pooler…) değil, _Direct_ bağlantıyı** seçin: iş kuyruğu `FOR UPDATE SKIP LOCKED` kullanır ve havuzlayıcıyla (transaction modu) uyumsuz olabilir.
3. Adres şöyle görünür: `postgresql://kullanici:parola@ep-xxx.region.aws.neon.tech/neondb?sslmode=require`. **Olduğu gibi** `DATABASE_URL` olarak verilir; uygulama bu biçimi tanır, çevirmeniz gerekmez.

## 2) Uygulama: Render

1. Depoyu GitHub'a gönderin. <https://render.com> → **New → Web Service** → deponuzu seçin.
2. **Runtime: Docker** (kökteki `Dockerfile` kullanılır). **Instance Type: Free**.
3. **Environment** bölümüne şunları ekleyin:

| Değişken | Değer | Neden |
|---|---|---|
| `DATABASE_URL` | Neon'dan kopyaladığınız adres | Veritabanı |
| `Jwt__Key` | `openssl rand -base64 48` çıktısı | Oturum imzası (≥ 32 karakter; **zorunlu**, yoksa uygulama başlamaz) |
| `Jobs__PollIntervalMs` | `5000` | İş kuyruğunun Neon'u gereksiz uyandırmasını azaltır |

`Dockerfile` zaten şunları varsayılan yapar: `ASPNETCORE_ENVIRONMENT=Production`, `Worker__InProcess=true` (işler API içinde çalışır),
`Database__MigrateOnStartup=true` (tablolar ilk açılışta oluşur), `ForwardedHeaders__Enabled=true` (Render proxy'si arkasında istemci başına hız sınırı).

4. **Health Check Path:** `/health`. **Create Web Service**.
5. Derleme birkaç dakika sürer. Bitince `https://<ad>.onrender.com` açılır.

## 3) İlk kullanım ve kayıt kapatma

1. Adresi açın → **Kayıt ol** (parola en az 10 karakter) → kendi hesabınızı oluşturun.
2. **Herkese açık bir adresiniz var: herkes kayıt olabilir.** Kendi hesabınızı açtıktan sonra Render'da `Auth__RegistrationOpen=false` ekleyin (servis yeniden başlar).
   Yeni kayıtlar `403 registration_closed` döner; mevcut hesaplar girmeye devam eder. Birkaç kişiyle paylaşacaksanız onların hesaplarını **kapatmadan önce** açtırın.

## 4) Gerçek bir AI modeli eklemek (isteğe bağlı)

- **Anthropic (ücretli, kullanım başına):** `Ai__Provider=anthropic` ve `Ai__Anthropic__ApiKey=<anahtar>`. **Önce `Auth__RegistrationOpen=false` yapın**; aksi halde yabancılar anahtarınızın maliyetini üretir.
  Proje kaynak metinleri olduğu gibi sağlayıcıya gönderilir; ticari sır varsa sağlayıcının veri koşullarını kontrol edin.
- **Yerel model (Qwen/Ollama):** ücretsiz barındırıcılarda GPU yoktur, orada çalıştırılamaz. Kendi bilgisayarınızdaki Ollama'yı bir tünel
  (ör. Cloudflare Tunnel) ile Render'a açıp `Ai__Provider=local`, `Ai__Local__BaseUrl=https://<tünel>`, `Ai__Local__Model=<etiket>` verebilirsiniz.
  **Dikkat:** tüneli kimlik doğrulaması olmadan herkese açık bırakmayın; ayrıca üretim yavaş olur, bilgisayarınız açık olmalıdır. Ayrıntı: `docs/LOCAL_LLM.md`.

## 5) Başka ücretsiz yollar

- **Her zaman açık sanal makine:** Oracle Cloud "Always Free" ARM VM'i. Raporlara göre ücretsiz sınır yakın zamanda 4 OCPU/24 GB'tan **2 OCPU/12 GB'a** indirildi;
  kendi hesabınızdaki *Limits* sayfasına bakın ([Oracle](https://docs.oracle.com/iaas/Content/FreeTier/resourceref.htm)). Kart doğrulaması ve kapasite sorunları olabilir.
  VM'de aynı `Dockerfile` ile çalıştırın, PostgreSQL'i ayrı bir konteynerde tutun.
- **Kısa süreli gösterim:** GitHub Codespaces'te `scripts/dev.sh` çalıştırıp bağlantı noktasını paylaşmak (oturum kapanınca biter).
- **Koyeb vb.:** ücretsiz katman koşulları kaynaklara göre tutarsız göründü; kullanacaksanız güncel fiyat sayfasını doğrulayın.

## Sorun giderme

| Belirti | Çözüm |
|---|---|
| Uygulama başlamıyor: `Jwt:Key en az 32 karakter olmalı` | `Jwt__Key` verin. |
| `Veritabanı adresi (postgres://…) çözümlenemedi` | `DATABASE_URL` içinde sunucu ve **/veritabanı_adı** olmalı. |
| `password authentication failed` / SSL hatası | Neon'da **Direct** bağlantıyı ve `?sslmode=require`'ı kullanın. |
| İşler "kuyrukta" kalıyor | `Worker__InProcess=true` olmalı (Dockerfile varsayılanı; ortamda `false` verdiyseniz kaldırın). |
| `429` ile kimse giriş yapamıyor | `ForwardedHeaders__Enabled=true` olmalı (aksi halde tüm kullanıcılar proxy'nin tek IP'sini paylaşır). Doğrudan internete açık çalıştırıyorsanız **kapalı** tutun. |
| İlk açılış çok yavaş | Uyuyan servis/veritabanı uyanıyor; bekleyin. |
| `docker build` hata veriyor | Çıktıyı paylaşın; imaj bu oturumda derlenemedi. |

## Bu yapılandırmayı yerelde sınamak

```bash
scripts/e2e.sh --prod --no-ui   # yayınlanmış uygulama + wwwroot + DATABASE_URL (postgres://) ile tek süreç; Docker gerekmez
scripts/e2e.sh --prod           # + tarayıcı yolculukları
```

Kaynaklar: [Render ücretsiz katman](https://render.com/docs/free) · [Neon fiyatlandırma](https://neon.com/pricing) · [Oracle Always Free](https://docs.oracle.com/iaas/Content/FreeTier/resourceref.htm)
