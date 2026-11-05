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

Caddy sertifikayı kendisi alır. nginx kullanıyorsanız `X-Forwarded-For` ve `X-Forwarded-Proto` başlıklarını iletin; uygulama istemci başına hız sınırı için bunlara güvenir (`ForwardedHeaders__Enabled=true`).

## 4) İlk açılış

1. `Auth__RegistrationOpen=true` ile başlatın, tarayıcıdan **ilk hesabı** açın (master admin).
2. `Auth__RegistrationOpen=false` yapıp `docker compose up -d` ile yeniden başlatın. Artık yalnızca master admin kullanıcı açar.
3. Yönetim ekranından kullanıcıları, grupları ve uygulamaları kurun: [`APPS_KULLANIM.md`](APPS_KULLANIM.md).
4. İkinci bir master admin açın (tek master admin kaybı = yönetim kaybı).

## 5) Yapılandırma özeti

| Değişken | Varsayılan | Açıklama |
|---|---|---|
| `DATABASE_URL` | — | `postgres://…` (zorunlu; `ConnectionStrings__Arcvora` yerine geçer) |
| `Jwt__Key` | — | **Zorunlu**, ≥ 32 karakter; değişirse tüm oturumlar düşer |
| `Auth__RegistrationOpen` | `true` | Şirket kurulumunda ilk hesaptan sonra `false` |
| `Worker__InProcess` | `true` (Dockerfile) | İşçiler (iş kuyruğu + muhasebe webhook) API süreci içinde çalışır |
| `Accounting__MaxAttempts` | `8` | Webhook en fazla deneme |
| `Accounting__RetryBaseSeconds` | `30` | Bekleme = taban × 2^(deneme−1) sn (en çok 6 saat) |
| `Accounting__TimeoutSeconds` | `15` | Webhook istek zaman aşımı |
| `RateLimit__AuthPerMinute` | `10` | Giriş/parola uçlarında IP başına dakikalık sınır |
| `Ai__Provider` | `heuristic` | Mimari araç için; Apps modülü yapay zekâ **kullanmaz** |

## 6) Yedekleme ve geri yükleme

```bash
# Günlük yedek (cron): şirketin yedekleme politikasına göre dışarıya kopyalayın
docker compose exec -T db pg_dump -U arcvora -Fc arcvora > /yedek/arcvora-$(date +%F).dump

# Geri yükleme (boş bir veritabanına)
docker compose exec -T db pg_restore -U arcvora -d arcvora --clean --if-exists < /yedek/arcvora-2026-11-05.dump
```

- **Geri yüklemeyi düzenli olarak deneyin**; denenmemiş yedek yedek değildir.
- Yedekler webhook gizli anahtarlarını ve talep verilerini içerir: şifreli ve erişimi kısıtlı saklayın.

## 7) Güncelleme

1. Yedek alın. 2. Yeni imajı çekin/derleyin. 3. `docker compose up -d` (şema otomatik güncellenir). 4. `/health` ve bir test talebiyle doğrulayın. Geri dönüş: eski imaj + yedekten geri yükleme (şema geriye dönük uyumlu olmayabilir).

## 8) Kontrol listesi

- [ ] `Jwt__Key` rastgele ve gizli · [ ] HTTPS · [ ] veritabanı portu kapalı · [ ] kayıt kapalı · [ ] en az 2 master admin
- [ ] Yedek + geri yükleme denendi · [ ] webhook adresi/gizli anahtarı tanımlı ve alıcı imzayı doğruluyor
- [ ] Kod ve imaj **özel** depoda · [ ] mülkiyet/lisans konusu yazılı netleşti (teknik değil, sözleşme konusu)
- [ ] Zaman: tarihler ve talep numarasındaki yıl **UTC**’dir; gece yarısı civarı yerel saatle bir gün fark görünebilir
