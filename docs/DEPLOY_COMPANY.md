# ARCVORA Apps — şirket sunucusuna kurulum

> **Gizli:** Şirket ürünüdür; imajı ve kaynak kodu **özel** (private) bir depoda/kayıt defterinde tutun, herkese açık bir yere göndermeyin.
> Ücretsiz/herkese açık dağıtım rehberi (`DEPLOY.md`) bu modül için **uygun değildir**: orada kayıt kapatma ve gizlilik varsayımları farklıdır.

## Doğrulama durumu (dürüst not)

- Uygulama **yayınlanmış çıktı olarak tek süreçte** (arayüz `wwwroot`’ta, `Production` ortamı, `DATABASE_URL`, uygulama içi işçi) `scripts/e2e.sh --prod` ile sınanabilir; bu rehberdeki ayarlar o yapılandırmadır.
- **`Dockerfile` bu ortamda derlenemedi** (Docker çalıştırılamadı); aşamaları elle doğrulandı. İlk `docker build` sizin makinenizde yapılacak; hata verirse çıktıyı paylaşın.
- Gerçek bir muhasebe yazılımına bağlantı **sınanmadı** (muhasebe sistemi bilinmiyor). Webhook, imzalı genel bir HTTP çağrısıdır; alıcı tarafı şirketin sistemine göre yazılmalıdır (`APPS_KULLANIM.md` §6).
- Gerçek çalışanlarla kullanılabilirlik denemesi yapılmadı.

## Gereksinimler

| | |
|---|---|
| Sunucu | Docker (veya .NET 8 çalışma zamanı + Node 22 ile derleme) · 1 vCPU / 1 GB RAM başlangıç için yeterlidir |
| Veritabanı | **PostgreSQL 16** (ayrı konteyner veya yönetilen servis). Başka sistem desteklenmez. |
| Ağ | Kullanıcılara HTTPS; API → muhasebe webhook adresine çıkış (özel ağ olabilir) |

## 1) Derleme

```bash
docker build -t sirket/arcvora:1 .
```

İmajı **özel** kayıt defterine gönderin (ör. `registry.sirket.local`).

## 2) Çalıştırma (docker compose örneği)

```yaml
# docker-compose.yml — sunucuda, gizli değerler .env dosyasından
services:
  db:
    image: postgres:16
    restart: unless-stopped
    environment:
      POSTGRES_USER: arcvora
      POSTGRES_PASSWORD: ${DB_PASSWORD}
      POSTGRES_DB: arcvora
    volumes: [pgdata:/var/lib/postgresql/data]
    healthcheck: { test: ['CMD-SHELL', 'pg_isready -U arcvora'], interval: 10s, retries: 5 }
    # Veritabanı portunu DIŞARI AÇMAYIN.

  app:
    image: sirket/arcvora:1
    restart: unless-stopped
    depends_on: { db: { condition: service_healthy } }
    environment:
      DATABASE_URL: postgres://arcvora:${DB_PASSWORD}@db:5432/arcvora
      Jwt__Key: ${JWT_KEY}                 # en az 32 karakter: openssl rand -base64 48
      Auth__RegistrationOpen: "false"      # ilk hesabı açtıktan SONRA (aşağıya bakın)
      # ForwardedHeaders__Enabled=true Dockerfile'da varsayılandır; uygulamayı doğrudan internete AÇMAYIN, bir ters proxy arkasında çalıştırın.
    ports: ['127.0.0.1:8080:8080']         # yalnızca ters proxy'nin erişimi için

volumes: { pgdata: {} }
```

`.env`: `DB_PASSWORD=...` ve `JWT_KEY=...` (ikisi de rastgele, depoya konmaz).

Şema, ilk açılışta otomatik oluşur (`Database__MigrateOnStartup=true` Dockerfile’da varsayılandır). **Her güncellemeden önce yedek alın** (aşağıda).

## 3) HTTPS için ters proxy (Caddy örneği)

```
arcvora.sirket.com {
    reverse_proxy 127.0.0.1:8080
}
```

Caddy sertifikayı kendisi alır; istek gövdesi sınırı varsayılan olarak yeterlidir. **nginx** için ek yüklemelerinin (varsayılan 10 MB, en çok 25 MB) geçmesi adına `client_max_body_size 30m;` verin (varsayılan 1 MB ek yüklemeyi `413` ile keser). nginx kullanıyorsanız `X-Forwarded-For` ve `X-Forwarded-Proto` başlıklarını iletin; uygulama istemci başına hız sınırı için bunlara güvenir (`ForwardedHeaders__Enabled=true`).

## 4) İlk açılış

1. `Auth__RegistrationOpen=true` ile başlatın, tarayıcıdan **ilk hesabı** açın (master admin).
2. `Auth__RegistrationOpen=false` yapıp `docker compose up -d` ile yeniden başlatın. Artık yalnızca master admin kullanıcı açar.
3. Yönetim ekranından kullanıcıları, grupları ve uygulamaları kurun: [`APPS_KULLANIM.md`](APPS_KULLANIM.md).
4. İkinci bir master admin açın (tek master admin kaybı = yönetim kaybı).

## 5) Yapılandırma özeti

| Değişken | Varsayılan | Açıklama |
|---|---|---|
| `DATABASE_URL` | — | `postgres://…` (zorunlu; `ConnectionStrings__Arcvora` yerine geçer) |
| `Jwt__Key` | — | **Zorunlu**, ≥ 32 karakter; değişirse tüm oturumlar düşer **ve basılı PDF çıktılarının doğrulama kodları geçersiz olur** |
| `Auth__RegistrationOpen` | `true` | Şirket kurulumunda ilk hesaptan sonra `false` |
| `Worker__InProcess` | `true` (Dockerfile) | İşçiler (iş kuyruğu + muhasebe webhook) API süreci içinde çalışır |
| `Accounting__MaxAttempts` | `8` | Webhook en fazla deneme |
| `Accounting__RetryBaseSeconds` | `30` | Bekleme = taban × 2^(deneme−1) sn (en çok 6 saat) |
| `Accounting__TimeoutSeconds` | `15` | Webhook istek zaman aşımı |
| `RateLimit__AuthPerMinute` | `10` | Giriş/parola uçlarında IP başına dakikalık sınır |
| `Ai__Provider` | `heuristic` | Mimari araç için; Apps modülü yapay zekâ **kullanmaz** |
| `Attachments__Store` | `database` | Ek dosyalar: `database` (PostgreSQL'de, yedeğe girer) veya `disk` (`Attachments__Path` altında) |
| `Attachments__Path` | — | `disk` için kök klasör; **kalıcı birime** bağlayın ve yedeğe ekleyin |
| `Attachments__MaxSizeMb` | `10` | Tek dosya üst sınırı (1–25) |
| `Attachments__MaxStagedPerUser` · `StagedTtlHours` | `20` · `24` | Talebe bağlanmamış yüklemeler: kişi başı sınır ve temizleme süresi |
| `Locale__UtcOffsetMinutes` | `180` | Yerel gün/saat (vekâlet günleri, PDF saati, hatırlatma). Türkiye UTC+3 = 180 |
| `Documents__MaxImageBytes` | `4194304` | PDF'e gömülecek tek görsel üst sınırı; fazlası yalnızca listelenir |
| `Maintenance__IntervalMinutes` | `10` | Bakım turu (hazırlanmış ek temizliği, hatırlatma, saklama süresi) |
| `Email__Enabled` | `false` | **Kapalıyken hiçbir mail kuyruğa alınmaz.** Aşağıdaki bölüme bakın |
| `Email__Host` · `Port` · `Security` | — · `587` · `starttls` | SMTP: `starttls` (587), `ssl` (465) veya `none` (yalnızca güvenilir iç ağ) |
| `Email__User` · `Email__Password` | — | SMTP kimliği. **Parola yalnızca ortam değişkeninden** okunur; API'de/hatada görünmez |
| `Email__From` · `FromName` | — · `ARCVORA Apps` | Gönderen adresi (zorunlu) ve adı |
| `Email__PublicUrl` | — | Maillerdeki bağlantı kökü (`https://apps.sirket.com`); boşsa mailde bağlantı olmaz |
| `Email__ReminderAfterHours` · `ReminderRepeatHours` · `MaxReminders` | `24` · `24` · `3` | Bekleyen onay hatırlatmaları |
| `Email__RetentionDays` | `90` | Gönderilmiş/başarısız e-posta kayıtlarının silinme süresi |
| `Oidc__Enabled` | `false` | Tek oturum açma (OpenID Connect). Aşağıdaki bölüme bakın |
| `Oidc__Authority` · `ClientId` · `ClientSecret` | — | Sağlayıcı bilgileri (üretimde `https` zorunlu) |
| `Oidc__PublicUrl` | — | Bu uygulamanın dış adresi; sağlayıcıya `…/api/auth/oidc/callback` kaydedilir |
| `Oidc__AutoProvision` · `DefaultWorkspaceId` | `false` · — | Bilinmeyen e-postaya hesap açılsın mı ve hangi çalışma alanına |
| `Oidc__AllowedEmailDomains__0` | — | Doluysa yalnızca bu alan adları (`sirket.com`) |

## 5a) E-posta bildirimleri (SMTP)

Varsayılan **kapalıdır**. Açmak için bir SMTP sunucusu (şirket posta sunucusu, Microsoft 365, Google Workspace SMTP rölesi…) gerekir:

```yaml
      Email__Enabled: "true"
      Email__Host: smtp.sirket.com
      Email__Port: "587"
      Email__Security: starttls
      Email__User: apps@sirket.com
      Email__Password: ${SMTP_PASSWORD}      # .env'de tutun
      Email__From: apps@sirket.com
      Email__PublicUrl: https://arcvora.sirket.com
```

- Yönetim → **E-posta** ekranından **test e-postası** gönderip kuyruğu (bekleyen/gönderilen/başarısız) izleyin; başarısız kayıt yeniden denenebilir.
- Mailler **outbox** ile gider: SMTP geçici kapalıyken talep/onay işlemi bozulmaz, mail üstel bekleme ile yeniden denenir.
- Mail hiçbir zaman parola içermez. Her kullanıcı kendi hesabından e-postayı kapatabilir (**E-posta bildirimleri**).
- Kendinden imzalı sertifika için `Email__AllowInvalidCertificate=true` seçeneği vardır; güvenlik riski taşır, yalnızca zorunluysa kullanın.

## 5b) Tek oturum açma (SSO / Active Directory)

OpenID Connect (yetkilendirme kodu + PKCE) ile **Microsoft Entra ID, ADFS, Keycloak, Okta, Google Workspace** gibi sağlayıcılar kullanılır.
**Yerel (şirket içi) Active Directory'ye doğrudan LDAP bağlantısı yoktur**; AD kullanıyorsanız ADFS veya Entra ID üzerinden bağlanın.

1. Sağlayıcıda bir uygulama kaydedin; **dönüş adresi:** `https://arcvora.sirket.com/api/auth/oidc/callback`.
2. `Oidc__Enabled=true`, `Oidc__Authority`, `Oidc__ClientId`, `Oidc__ClientSecret`, `Oidc__PublicUrl` değerlerini verin. Açılışta ayarlar doğrulanır; hatalıysa uygulama **nedenini yazarak** başlamaz.
3. Giriş sayfasında “Şirket hesabıyla giriş” düğmesi çıkar; parola ile giriş de çalışmaya devam eder.
4. Kimlik **e-posta ile eşleşir**: yöneticinin açtığı hesapla aynı e-postayı taşıyan kişi o hesaba girer (sağlayıcıda e-posta doğrulanmış olmalıdır; `Oidc__RequireEmailVerified`). Bilinmeyen e-postalar için hesap **otomatik açılmaz**, `Oidc__AutoProvision=true` yapmadıkça. Otomatik açılan hesap yalnızca “uygulama kullanıcısı”dır, rolü yükseltmek yine master admin'e aittir.
5. Sınırlar: giriş biletleri bellekte tutulur (tek örnekli çalışma varsayımı; birden çok uygulama kopyası kullanacaksanız ters proxy'de oturum yapışkanlığı gerekir). Sağlayıcıdan çıkış (single logout) yoktur.

## 5c) Mobil (PWA)

Ayrı bir mobil uygulama yoktur; arayüz telefon ekranına uyumludur ve **ana ekrana eklenebilir** (PWA: manifest + simgeler + service worker).
Telefonda tarayıcı menüsünden “Ana ekrana ekle” deyin. Service worker yalnızca uygulama kabuğunu önbelleğe alır; **`/api` altındaki hiçbir veri önbelleğe alınmaz**, çevrimdışıyken talep gönderilemez/onaylanamaz (bilinçli).
Ters proxy `sw.js`, `index.html` ve `manifest.webmanifest` için sunucunun `Cache-Control: no-cache` başlığını **değiştirmemeli** (güncellemeler gecikir). PWA kurulumu HTTPS gerektirir.

## 6) Yedekleme ve geri yükleme

```bash
# Günlük yedek (cron): şirketin yedekleme politikasına göre dışarıya kopyalayın
docker compose exec -T db pg_dump -U arcvora -Fc arcvora > /yedek/arcvora-$(date +%F).dump

# Geri yükleme (boş bir veritabanına)
docker compose exec -T db pg_restore -U arcvora -d arcvora --clean --if-exists < /yedek/arcvora-2026-11-05.dump
```

- **Geri yüklemeyi düzenli olarak deneyin**; denenmemiş yedek yedek değildir.
- Yedekler webhook gizli anahtarlarını, talep verilerini ve (varsayılan `database` deposunda) **ek dosyaları** içerir: şifreli ve erişimi kısıtlı saklayın.
- `Attachments__Store=disk` kullanıyorsanız ek klasörünü (`Attachments__Path`) de **aynı zamanda** yedekleyin; veritabanı ile klasör uyumsuz dönerse dosyası olmayan ek kayıtları “bulunamadı” döner.
- Mail sunucusu parolası ve SSO istemci sırrı yedeğe girmez (ortam değişkenidir); parola yöneticinizde ayrıca saklayın.

## 7) Güncelleme

1. Yedek alın. 2. Yeni imajı çekin/derleyin. 3. `docker compose up -d` (şema otomatik güncellenir). 4. `/health` ve bir test talebiyle doğrulayın. Geri dönüş: eski imaj + yedekten geri yükleme (şema geriye dönük uyumlu olmayabilir).

## 8) Kontrol listesi

- [ ] `Jwt__Key` rastgele ve gizli · [ ] HTTPS · [ ] veritabanı portu kapalı · [ ] kayıt kapalı · [ ] en az 2 master admin
- [ ] Yedek + geri yükleme denendi · [ ] webhook adresi/gizli anahtarı tanımlı ve alıcı imzayı doğruluyor
- [ ] Kod ve imaj **özel** depoda · [ ] mülkiyet/lisans konusu yazılı netleşti (teknik değil, sözleşme konusu)
- [ ] E-posta: test e-postası gitti, `Email__PublicUrl` doğru · [ ] SSO kullanılıyorsa dönüş adresi kayıtlı ve bir deneme girişi yapıldı
- [ ] Ek klasörü/veritabanı yedeği birlikte alınıyor · [ ] ters proxy gövde sınırı ≥ ek sınırı · [ ] demo veri **kaldırıldı** (Yönetim → Demo veri)
- [ ] Çalışma alanı saati: `Locale__UtcOffsetMinutes` doğru (Türkiye 180) · [ ] resmî/dinî tatil takvimi yıl başında girildi
- [ ] Zaman: tarihler ve talep numarasındaki yıl **UTC**’dir; gece yarısı civarı yerel saatle bir gün fark görünebilir
