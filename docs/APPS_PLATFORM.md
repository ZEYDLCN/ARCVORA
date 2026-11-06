# ARCVORA Apps — şirket içi akış uygulamaları platformu

> **Gizlilik:** Bu belge ve `Apps` modülü şirket ürünüdür; **açık kaynak değildir ve herkese açık bir depoya gönderilmemelidir.**
> Bu dal yerelde geliştirilir; ARCVORA'nın herkese açık deposuna push edilmez. Kodun nereye (özel depo) taşınacağını sahibi belirler.
>
> **Lisans sınırı:** Herkese açık ARCVORA (mimari araç) **MIT** lisanslıdır (`main`). Bu **Apps modülü MIT kapsamına alınmadı** ve varsayılan olarak kapalıdır; şirketin “açık kaynak olamaz” şartı Apps içindir. Apps'i de MIT yapmak (veya telif/lisans sahibini belirlemek) ayrı, geri dönüşü olmayan bir karardır; sahipleri yazılı kararlaştırana kadar bu kodu herkese açık bir yere koymayın. Bu dal ana dalı birleştirince kök `LICENSE` (MIT) görünür: o dosya yalnızca MIT'e konu kodu kapsar.

## 1) Kaynak: yazışmadan çıkan istekler

Kaynak, Yavuz Durbak ile yapılan WhatsApp yazışmasıdır (4 Kasım 2026). Her madde yazışmadaki bir cümleye dayanır.

| # | İstek | Dayanak (yazışmadan) |
|---|---|---|
| 1 | Masraf sabit bir sistem olmasın; **şirketler kendi akış sistemini kendileri oluştursun** (izin, masraf, avans vb.). | "masraf yerine kendimiz avans izin vb oluşturabilelim", "kendi akış sistemini oluştursun şirketler" |
| 2 | **Tasarlanan akış çalışan bir uygulama (app) olsun.** ARCVORA menüsüne "APP" bölümü gelsin. | "bu akış sonrası çıkan akış app olsun", "akışı biten uygulama, app olsun burada" (menüde "Öneriler"in altına APP) |
| 3 | **Talepler muhasebeye doğrudan aktarılsın.** | "taleplerini muhasebeye direk aktaran bir yapı olur" |
| 4 | İhtiyaç olan **başka akışlar da üretilebilsin; hepsi tek uygulamada.** | "ihtiyaç olan başka akışlarda üretiriz", "tek uygulamada yapıcaz hepsini" |
| 5 | **Rol bazlı yönetim.** Yalnızca "app yetkisi" verilen kişi girince ilgili app'i kullanabilsin. | "bu sayfa rol bazlı yönetilsin", "sadece app yetkisi verdiğimiz girdiğinde ilgili appi kullanabilsin" |
| 6 | **Master admin** her şeyi tasarlayabilsin. | "master admin dediğimiz tüm her şeyi tasarlayabilsin" |
| 7 | **Şirketin kendi sunucusunda yayınlansın**, ekip kullansın. | "bizim sunucularda publish ederiz, ekip kullanır" |
| 8 | **Açık kaynak olmasın**, GitHub'da public paylaşılmasın. | "paylaşmıyoruz bu uygulamayı gitte public, opensource olamaz" |
| 9 | İlk gerçek kullanım: **evrakla yapılan izin işini uygulamaya çevirmek** (şirkette İK yazılımı yok, yüzlerce çalışan yok). | "humanist yok bizde… bundan dolayı evrakla yaptığımız işi appe çevirelim" |

Yazışmada ayrıca şu ayrım var: "herhangi bir İK app'ini yapay zekâ ile 3 günde bitiririm, ama o herkesin yapabileceği şey olur."
Hedef, sabit bir İK uygulaması değil; **şirketin kendi akışlarını tanımlayabildiği genel bir platformdur.**

## 2) Anlaşılanlar, varsayımlar ve teyit gereken sorular

Geliştirmeyi durdurmamak için aşağıdaki **varsayımlar** yapıldı. Her biri değiştirilebilir bir karardır; teyit edildikçe güncellenir.

| Konu | Varsayım (geliştirmede uygulanan) | Teyit gerek |
|---|---|---|
| Şirket sayısı | Tek dağıtım = tek şirket. Veri modeli yine çalışma alanı (tenant) yalıtımlıdır; ileride çok şirkete açılabilir. | Evet: "şirketler" çoğul mu, tek şirket mi? |
| Hesaplar | Self-servis kayıt kapatılır (`Auth__RegistrationOpen=false`); kullanıcıları master admin açar, parolayı verir, kullanıcı değiştirebilir. | SSO/Active Directory gerekir mi? |
| Onay zinciri | Adımlar sıralıdır; onaylayıcı **doğrudan yönetici**, **belirli kişi** veya **grup** olabilir; koşullu adım (ör. tutar > X) desteklenir. | İzin/avans/masraf için gerçek zincirler nedir? |
| Muhasebe | Muhasebe sistemi bilinmiyor. Genel çözüm: onaylanan talep **muhasebe kuyruğuna** düşer; (a) HTTP **webhook** ile otomatik, (b) kuyruk ekranından **elle işaretleme**, (c) **CSV** dışa aktarma. | **Yanıt (5 Kasım):** şirkette muhasebe/İK yazılımı yok, süreçler evrakla yürüyor → dahili kuyruk + CSV birincil, webhook isteğe bağlı (bkz. §15). |
| İzin | Tarih aralığı + gün sayısı (otomatik hesap; resmî tatiller düşülür) + açıklama + vekil. **İzin bakiyesi** (kıdem hakkı, rezerve/iade, devir) **faz G7**'de eklendi. | Yaş/sözleşme istisnaları ve izin türleri şirkete göre netleşmeli. |
| Bildirim | Uygulama içi (onay kutusu, taleplerim) + **e-posta (SMTP, faz G4)**. WhatsApp/SMS yok. | Şirketin SMTP sunucusu/hesabı? |
| Ekler | Fiş/belge/tarama yükleme **faz G1**'de eklendi (PDF/JPEG/PNG/WebP, içerikten tür denetimi, yetkili erişim). | Hangi evraklara hangi ek zorunlu? |
| ARCVORA ile ilişki | Aynı uygulama, aynı giriş. Mimari tasarım ekranları **yalnızca master admin/düzenleyicilere** görünür; app kullanıcıları yalnızca APP bölümünü görür. Akış tasarlayıcısı ayrı bir ekrandır; "ARCVORA akışından app taslağı" faz G. | Otomatik üretim mi, elle tasarım mı beklenir? |
| Sunucu | Linux + Docker + PostgreSQL 16 varsayıldı (`Dockerfile` bu ortamda derlenmedi; ilk derlemede doğrulanmalı). | Altyapı nedir? |
| Açık kaynak | Bu modül kapalıdır. Herkese açık ARCVORA deposuna push edilmez. | Depo/lisans/mülkiyet kararı sahibine ait. |

## 3) Kapsam dışı (ilk sürüm)

**İlk sürümde** kapsam dışıydı; **sonradan faz G ile eklenenler (§12):** dosya/belge yükleme, e-posta bildirimi, izin bakiyesi ve resmî tatil takvimi,
PDF çıktı, SSO (OIDC), mobil (PWA), raporlar. **Hâlâ kapsam dışı:** WhatsApp/SMS bildirimi, yerel AD'ye doğrudan LDAP, nitelikli e-imza,
çok adımlı paralel dallanma, form içi tablolar (satır kalemleri), yerel (native) mobil uygulama, grafiksel raporlama panoları.

## 4) Roller ve yetkiler

| Rol | Kim | Yapabildikleri |
|---|---|---|
| **Master admin** (`admin`) | Platformu yöneten kişi(ler) | Her şeyi tasarlar: mimari projeler (ARCVORA), app tasarımı/yayını, kullanıcı/grup yönetimi, app yetkisi verme, muhasebe ayarları. Tüm app'leri kullanabilir. |
| Düzenleyici / okuyucu (`editor`/`reader`) | Mimari ekibi | ARCVORA proje yetkileri aynen korunur. App'lerde yalnızca kendilerine verilen yetki kadar. |
| **App kullanıcısı** (`app_user`) | Çalışanlar | **Yalnızca** kendilerine yetki verilen app'leri kullanır: talep açar, taleplerini izler. Mimari projeleri **göremez** (404). |

Ek, rolden bağımsız yetenekler:

- **Onaylayıcı:** Akış, bir adımı birine atadığında (yönetici/kişi/grup) o kişi "Onay kutusu"nda görevi görür. App yetkisi gerekmez.
- **Muhasebe:** Muhasebe grubunun üyeleri (ve master admin) muhasebe kuyruğunu görür ve işler.
- **Doğrudan yönetici:** Üyelik üzerinde tanımlıdır; "yönetici onayı" adımını çözer.

## 5) Akış tanımı (schema 1)

Bir app, sürümlenmiş bir **akış tanımıdır**. Taslak düzenlenir; **yayınla** değişmez bir sürüm üretir. Açık talepler açıldıkları sürümle devam eder.

```jsonc
{
  "schema": 1,
  "prefix": "IZN",                       // talep numarası: IZN-2026-0007
  "summaryFields": ["start", "end"],     // listelerde gösterilen özet
  "fields": [
    { "key": "start", "label": "Başlangıç", "type": "date", "required": true },
    { "key": "end",   "label": "Bitiş",     "type": "date", "required": true, "notBefore": "start" },
    { "key": "days",  "label": "Gün",       "type": "number", "computed": { "op": "calendar_days", "from": "start", "to": "end" } },
    { "key": "kind",  "label": "Tür",       "type": "select", "required": true, "options": ["Yıllık", "Mazeret", "Ücretsiz"] },
    { "key": "proxy", "label": "Vekil",     "type": "user" },
    { "key": "note",  "label": "Açıklama",  "type": "long_text", "maxLength": 1000 }
  ],
  "steps": [
    { "id": "s1", "name": "Yönetici onayı", "approvers": { "type": "manager" } },
    { "id": "s2", "name": "Genel müdür", "approvers": { "type": "group", "id": "<groupId>" }, "mode": "any",
      "conditions": [{ "field": "days", "op": "gt", "value": 10 }] }
  ],
  "completion": { "accounting": { "enabled": false, "category": "izin" } }
}
```

- **Alan tipleri:** `text`, `long_text`, `number`, `money`, `date`, `select`, `multi_select`, `checkbox`, `user`. Kurallar: `required`, `min`, `max`, `maxLength`, `options`, `notBefore` (başka bir tarih alanından önce olamaz), `computed` (`calendar_days`, `weekdays`). Sayı/tutar en fazla 10¹² ve tutar en fazla 2 ondalıklıdır; `money` varsayılan olarak negatif olamaz.
- **Onaylayıcı tipleri:** `manager` (talep edenin doğrudan yöneticisi), `user` (belirli kişi), `group` (grup; `mode`: `any` = biri onaylasa yeter, `all` = hepsi onaylamalı), `admin` (master admin'ler).
- **Çözülemeyen onaylayıcı** (yöneticisi tanımsız, grup boş, kişi bulunamadı): görev master admin'lere düşer ve olay kaydına/görev rozetine nedeniyle yazılır. Talep sessizce takılmaz.
- **Çıkarılan onaylayıcı:** çalışma alanından çıkarılan kişinin bekleyen görevleri, adımda başka bekleyen onaylayıcı yoksa master admin'e (`assignee_removed`) devredilir; varsa o devam eder. “Hepsi onaylamalı” adımlarında kişinin yerine tek bir master admin onayı beklenir.
- **Koşullar:** `eq`, `ne`, `gt`, `gte`, `lt`, `lte`, `in`; hepsi sağlanırsa adım çalışır, aksi hâlde atlanır. Tüm adımlar atlanırsa talep otomatik onaylanır.
- **Doğrulama:** Yayınlamadan önce tanım doğrulanır (tekrarlanan anahtar, bilinmeyen alan referansı, boş adım adı, döngüsel hesap, grup/kullanıcı varlığı).

## 6) Talep yaşam döngüsü

```
            gönder                 adımın tüm görevleri onaylandı
 (yok) ───────────► in_review ───────────────────────────────► sonraki adım ─► … ─► approved ─► (muhasebe kuyruğu)
                       │   ▲
        reddet         │   │ iptal (talep eden, yalnızca in_review)
                       ▼   │
                   rejected   cancelled
```

- Karar yalnızca görev **atanan kişi** tarafından, görev `pending` iken verilir; ikinci karar `409`.
- Red için gerekçe zorunludur. Onayda yorum isteğe bağlıdır.
- Talep eden kendi talebini onaylamaz; tek onaylayıcı kendisiyse görev master admin'e yönlenir. Yalnızca talep edenin kendisi tek master admin ise onaylayabilir ve bu `only_approver_is_requester` olarak işaretlenir.
- Her geçiş değişmez **olay kaydı** üretir (zaman çizelgesi).
- Onay sonrası tek işlemde (transactional outbox) muhasebe kaydı oluşturulur; teslim ayrı bir işçi tarafından yapılır.

## 7) Muhasebe aktarımı

`AccountingEntry` (talep başına tek kayıt, idempotent): `pending → sent → processed` veya `failed`.

1. Onay tamamlanınca (app tanımında `completion.accounting.enabled`) kayıt `pending` oluşur; yük, talebin alanlarından ve kimliklerinden derlenir.
2. Çalışma alanı ayarında **webhook adresi** varsa işçi `POST` eder: gövde JSON, başlıklar `Idempotency-Key`, `X-Arcvora-Signature: sha256=<hmac>`. Başarılıysa `sent`; hata hâlinde artan aralıkla en fazla 8 deneme, sonra `failed`.
3. Muhasebe kullanıcısı kuyruk ekranından kaydı **işlendi** olarak işaretler (not ile), `failed` olanı yeniden dener, CSV indirir.
4. Webhook adresi yoksa kayıtlar `pending` kalır ve elle işlenir (CSV/ekran).

## 8) Veri modeli (yeni tablolar)

`Groups`, `GroupMembers`, `Membership.ManagerId`, `AppDefinitions`, `AppVersions`, `AppGrants`, `FlowRequests`, `ApprovalTasks`,
`RequestEvents`, `AccountingEntries`, `AccountingSettings`. **Faz G ile:** `Attachments` (+ `AttachmentBlobs`), `Delegations`, `EmailMessages` (outbox),
`NotificationSettings`, `RetentionSettings`, `DataLists`/`DataListItems`, `Holidays`, `LeaveEntries` (izin kayıt defteri), `UserIdentities` (SSO).
Mevcut `Users`, `Workspaces`, `Memberships`, `AuditEntries` korunur; `Users`/`Groups`/`FlowRequests`/`AppDefinitions` demo işaretini, `Memberships` işe giriş tarihini taşır.
Tüm yeni kayıtlar `WorkspaceId` taşır; başka çalışma alanı kaydına her erişim `404` döner (mevcut desen).

## 9) API özeti

| Alan | Uç noktalar |
|---|---|
| Hesap | `POST /me/password` (kendi parolası); `GET /me` → `mustChangePassword`, çalışma alanında `role` ve `isAccounting` |
| Kullanıcı | `POST /workspaces/{id}/users` · `PATCH …/members/{userId}` (rol, yönetici) · `POST …/members/{userId}/reset-password` (yalnız admin'in açtığı hesaplar) · `DELETE …/members/{userId}` |
| Grup | `GET/POST /workspaces/{id}/groups` · `PATCH …/groups/{gid}` · `PUT …/groups/{gid}/members` · `DELETE …/groups/{gid}` |
| Dizin | `GET /workspaces/{id}/directory` (her üye; kişi seçicileri için) |
| Tasarım (admin) | `GET/POST /workspaces/{id}/apps` · `GET/PUT /apps/{id}` (taslak, `baseRevision`) · `POST /apps/{id}/publish` · `…/archive` · `…/restore` · `DELETE /apps/{id}` · `GET/PUT /apps/{id}/grants` |
| Kullanım | `GET /my/apps` · `GET /apps/{id}/form` · `POST /apps/{id}/requests` (`Idempotency-Key`) |
| Talepler | `GET /my/requests?status=` · `GET /workspaces/{id}/requests?status=&appId=` (admin) · `GET /requests/{id}` · `POST /requests/{id}/cancel` |
| Onay | `GET /my/tasks?done=` · `POST /tasks/{id}/approve` · `POST /tasks/{id}/reject` (gerekçe zorunlu) |
| Muhasebe | `GET/PUT /workspaces/{id}/accounting/settings` (admin) · `GET …/accounting/entries?status=&from=&to=` · `POST …/entries/{eid}/process` · `POST …/entries/{eid}/retry` · `GET …/accounting/export.csv?delimiter=` |

| Ekler / PDF | `POST /apps/{id}/uploads?fileName=` (ham gövde) · `GET /attachments/{id}` · `DELETE /attachments/{id}` (hazırlık) · `GET /requests/{id}/pdf` · `GET /workspaces/{id}/documents/verify?number=&code=` |
| Vekâlet / atama | `GET/POST /workspaces/{id}/delegations` (`?scope=all` admin) · `DELETE …/delegations/{did}` · `POST /tasks/{id}/reassign` (admin) |
| E-posta | `GET/PUT /workspaces/{id}/email` · `POST …/email/test` · `GET …/email/outbox` · `POST …/email/outbox/{mid}/retry` · `GET/PUT /me/notifications` |
| Arama / rapor | `GET /workspaces/{id}/requests/search?q=&status=&appId=&from=&to=&page=` · `GET …/reports/summary` · `GET …/reports/requests.csv` · `GET …/audit` |
| Saklama / KVKK | `GET/PUT …/retention` · `POST …/retention/preview` · `POST …/retention/purge` (`confirm`) · `GET …/members/{uid}/data-export` · `GET …/former-users` · `POST …/members/{uid}/anonymize` |
| Listeler | `GET/POST /workspaces/{id}/lists` · `GET/PATCH/DELETE …/lists/{lid}` · `PUT …/lists/{lid}/items` |
| İzin / tatil | `GET/POST …/holidays` · `POST …/holidays/seed-fixed` · `DELETE …/holidays/{hid}` · `GET …/leave/me` · `GET …/leave/balances` · `POST …/leave/entries` · `…/leave/distribute` · `…/leave/carryover` |
| Demo veri | `GET/POST/DELETE /workspaces/{id}/demo-data` |
| SSO | `GET /auth/providers` · `GET /auth/oidc/login` · `GET /auth/oidc/callback` · `POST /auth/oidc/ticket` |

Hata biçimi: `application/problem+json` `{ status, code, title, details }`. Formda `422 invalid_request` + `details: [{code, path, message}]` (path `data.<alan>`).

## 10) Arayüz

Oturum açan herkes aynı kabuğu görür; menü role göre sadeleşir.

- **App kullanıcısı:** Uygulamalarım · Taleplerim · Onay kutusu · Vekâletlerim · Belge doğrula · (muhasebe üyesiyse) Muhasebe.
- **Master admin:** yukarıdakilere ek olarak **Mimari projeler** (mevcut ARCVORA), **App tasarla**, **Yönetim** (kullanıcılar, gruplar, tüm talepler, listeler, izin ve tatiller, raporlar, muhasebe ayarları, e-posta, saklama/KVKK, denetim, demo veri).
- Tasarlayıcı: alan ekle/sırala, adım ekle, onaylayıcı seç, koşul tanımla, **canlı önizleme** (talep eden gözüyle form), doğrulama bulguları, yayınla.

## 11) Güvenlik ilkeleri

- Rol ve üyelik **her istekte sunucuda** doğrulanır; arayüz yalnızca kolaylıktır.
- Çalışma alanı dışı kayıt `404`; yetkisiz ama üye `403`.
- App kullanıcısı mimari projeleri listeleyemez, açamaz, dolaylı yoldan da (ör. örnek proje, dışa aktarma) erişemez.
- Parolalar PBKDF2; yeni kullanıcıya verilen geçici parola ilk girişte değiştirilmeye çağrılır (`mustChangePassword`).
- Webhook gövdesi HMAC ile imzalanır; adres yalnızca master admin tarafından girilir. **Not:** Şirket içi muhasebe sistemleri çoğu kez özel ağdadır; bu nedenle özel IP'lere gönderim engellenmez. Adres yalnızca `http/https` olmalıdır.
- Denetim kaydı: kullanıcı/grup/yetki değişiklikleri, yayınlar, kararlar, muhasebe işlemleri, saklama silmeleri, anonimleştirme.
- Ekler: tür **içerikten** (sihirli bayt) belirlenir, SVG/HTML/arşiv/yürütülebilir reddedilir, indirme `attachment` + `nosniff` + önbelleksiz, erişim talebin görünürlük kuralıyla aynıdır (yetkisiz `404`).
- E-posta: parola içeren mail yoktur; kullanıcı metni HTML olarak çalıştırılmaz; başlık satırı enjeksiyonu engellenir; SMTP/SSO sırları API'de görünmez.
- SSO: yetkilendirme kodu + PKCE, imzalı durum çerezi, tek kullanımlık bilet (atomik), e-posta doğrulaması zorunlu (varsayılan), hesap bağlama yalnızca doğrulanmış e-posta ile.
- PWA service worker `/api`'yi ve çapraz kökeni hiç işlemez; yalnızca uygulama kabuğu önbelleğe alınır.

## 12) Fazlar ve bitiş ölçütleri

| Faz | Teslim | Durum ve kanıt |
|---|---|---|
| **A** | `app_user` rolü; kullanıcı oluşturma; parola değiştirme/sıfırlama; gruplar; doğrudan yönetici; `app_user` için mimari projelere erişim kapalı | ✅ `UserManagementTests`, `GroupTests`, `PasswordResetTests` |
| **B** | Akış tanımı modeli, doğrulayıcı, form doğrulama, hesaplanan alanlar, onaylayıcı çözümleyici, durum makinesi (Core) | ✅ Core birim testleri (200) |
| **C** | Kalıcılık + API: tasarım/yayın/sürüm, yetki, talep, onay, iptal, zaman çizelgesi, onay kutusu | ✅ `AppsDesignTests`, `AppsRuntimeTests` (paralel karar, sürüm sürekliliği, yalıtım), `RemovedApproverTests` |
| **D** | Muhasebe kuyruğu, webhook işçisi (HMAC, idempotent, yeniden deneme), CSV, işlendi işareti | ✅ `AccountingTests` (imza, yeniden deneme, eşzamanlı işçi, CSV enjeksiyonu) |
| **E** | Web: portal, form, taleplerim, onay kutusu, tasarlayıcı, yönetim, muhasebe; rol bazlı menü | ✅ vitest (140) + tarayıcı yolculuğu `apps-ui.mjs` |
| **F** | Şablonlar (İzin, Avans, Masraf); uçtan uca betikler; şirket dağıtım dokümanı | ✅ `TemplatesTests` (gerçek sunucu), `apps-journey.mjs`, `apps-ui.mjs`; ⬜ `Dockerfile` derlenmedi |
| **G1** | **Ekler:** fiş/belge/tarama yükleme (içerikten tür, 10 MB, yetkili erişim, hazırlık temizliği); **demo (mock) veri üreticisi** | ✅ `AttachmentTests`, `AttachmentDiskTests`, `SampleFilesTests`, `DemoDataTests`, `FileSnifferTests`, `FileFieldTests` |
| **G2** | **Yazdırılabilir PDF** (form + onay zinciri + gömülü görsel ekler + doğrulama kodu) ve **belge doğrulama** | ✅ `RequestPdfTests`, `RequestPdfApiTests` (poppler ile metin/görsel), `apps-g-journey.mjs`, `apps-g-ui.mjs` |
| **G3** | **Vekâlet** (tarih aralıklı, zincirlenmez, yerel gün) + görev yeniden atama | ✅ `DelegationTests`, `DelegationRulesTests` (eşzamanlı karar, öz-onay, yetki bitişi) |
| **G4** | **E-posta** (SMTP, outbox, yeniden deneme, hatırlatma, vekile bildirim, kullanıcı/çalışma alanı tercihi) | ✅ `EmailTests`, `SmtpEmailTests` (gerçek SMTP soketi), `QaDeputyMailTests`, e2e gerçek SMTP alıcısı |
| **G5** | **Arama/rapor/CSV/denetim ekranı**, **saklama süresi + KVKK** (dışa aktarma, anonimleştirme) | ✅ `ReportTests`, `RetentionTests`, `QaTurkishSearchTests`, `QaPersonalDataTests`, `QaCrossFeatureTests` |
| **G6** | **Ana veri / dinamik listeler** (forma bağlı seçim listeleri) | ✅ `DataListTests`, `DataListFieldTests` |
| **G7** | **İzin bakiyesi** (kıdem hakkı, rezerve/iade, devir) + **resmî tatil takvimi** | ✅ `LeaveBalanceTests` (eşzamanlı talep, yıl sınırı), `LeaveTests` |
| **G8** | **SSO (OIDC)** + **mobil (PWA)** | ✅ `SsoTests` (sahte IdP: PKCE, durum, tekrar oynatma, hesap bağlama), `App.sso.test.tsx`, `sw.test.ts`, `StaticSiteTests`, `apps-mobile.mjs`; ⬜ gerçek bir kimlik sağlayıcıyla (Entra/ADFS/Keycloak) deneme yapılmadı |
| G9 | ARCVORA akışından app taslağı | ⬜ yapılmadı |

## 13) Riskler

- **Muhasebe sistemi bilinmiyor:** genel webhook/CSV çözümü gerçek sisteme uymayabilir; bağlayıcı ayrı bir iş olur.
- **Hukuki/ticari:** kodun sahipliği ve gizliliği teknik değil, sözleşme konusudur; geliştirmeye başlamadan netleştirilmelidir.
- **Dockerfile** henüz derlenmedi; ilk kurulumda sorun çıkabilir. Dağıtım adımları: [`DEPLOY_COMPANY.md`](DEPLOY_COMPANY.md).
- **Kullanıcı deneyimi** gerçek çalışanlarla denenmedi; izin formu şirketin gerçek evrakına göre uyarlanmalı.

## 14) Gerçekleşen kapsam ve dürüst sınırlar

Kod, test ve belgeler A–G8 fazlandırmasına göre tamamlandı; kullanım için bkz. [`APPS_KULLANIM.md`](APPS_KULLANIM.md). Aşağıdakiler **yapılmadı veya doğrulanmadı**:

- **Gerçek çalışanlarla kullanılabilirlik denemesi** yapılmadı; izin/masraf formları şirketin gerçek evrakına göre uyarlanmalıdır (evrak envanteri bekleniyor).
- **`Dockerfile` derlemesi ve şirket sunucusunda kurulum** denenmedi (bu ortamda Docker çalıştırılamadı). Aynı yapılandırma `scripts/e2e.sh --prod` ile (yayınlanmış çıktı + `wwwroot` + `DATABASE_URL` + tek süreç) doğrulandı.
- **Gerçek bir muhasebe yazılımına** bağlantı yok (imzalı genel webhook + CSV + elle işleme). **Gerçek bir kimlik sağlayıcıyla (Entra ID/ADFS/Keycloak) SSO denenmedi**; sahte bir OIDC sağlayıcıya karşı (PKCE, durum, tekrar oynatma, hesap bağlama) sınandı. Yerel Active Directory'ye **doğrudan LDAP yoktur**.
- **Gerçek SMTP sunucusuyla** (Microsoft 365/Google/şirket sunucusu: TLS, kimlik doğrulama) denenmedi; protokol yolu yerel bir SMTP alıcısına karşı uçtan uca sınandı. STARTTLS/SSL kodu vardır ama gerçek sertifikalı sunucuyla görülmedi.
- **Gerçek cihazlarda mobil deneme yok:** telefon ekran boyutu (390 px) Chromium'da sınandı (taşma, dokunma hedefi, çevrimdışı kabuk); iOS Safari/Android Chrome'da "ana ekrana ekle" elle denenmelidir. Yerel (native) mobil uygulama yoktur.
- **Yük/performans:** 30.000 sentetik talepte (yerel PostgreSQL, tek makine) arama 30–170 ms, rapor özeti ≈120 ms, 5 MB CSV ≈300 ms ölçüldü; çok kullanıcılı eşzamanlı yük ve büyük dosya trafiği ölçülmedi.
- **Virüs taraması yok:** yüklenen dosyalar yalnızca tür/boyut denetiminden geçer. Dosyalar tarayıcıda yürütülmez ama içerik zararsızlığı garanti edilmez.
- **E-imza değil:** PDF doğrulama kodu bir **kayıt bütünlüğü** denetimidir (talep değişirse kod değişir); nitelikli elektronik imza yerine geçmez. Hukuki geçerlilik şirketin yönetmeliğine/danışmanına bağlıdır.
- **İzin hesabı yalnızca kıdeme dayalıdır** (4857 m.53); yaş/sözleşme istisnaları, doğum/evlilik/ölüm izinleri gibi türler otomatik değildir (düzeltme kaydı veya ayrı akış). **Dinî bayram tarihleri her yıl elle girilir.**
- **SSO bilet defteri bellektedir:** birden çok uygulama kopyası çalıştırılırsa SSO dönüşü aynı kopyaya gelmelidir (oturum yapışkanlığı). Sağlayıcıdan toplu çıkış (single logout) yok.
- Webhook gizli anahtarı veritabanında düz metindir (imza üretmek için); SMTP parolası ve SSO sırrı ortam değişkenidir, yedeklere girmez. Yedekleri koruyun.
- Parola sıfırlama e-postayla değil, master admin eliyledir; yalnızca master admin'in açtığı hesaplar sıfırlanabilir.
- Arama Türkçe harfleri katlar (ı/İ/ş/ğ/ü/ö/ç ↔ ASCII); sıralama ise veritabanı harmanlamasına bağlıdır (Türkçe sıralama için `tr_TR` harmanlaması gerekir, varsayılan imaj yeterlidir ama "Ç/Ş/İ" sırası kusursuz olmayabilir).
- **CI bu dalda çalıştırılmadı** (yalnızca yerelde: `dotnet test`, `vitest`, `tsc`, `scripts/e2e.sh`); bir PR açıldığında GitHub Actions ilk kez koşacaktır.

## 15) Yeni bilgi: kurum tamamen evrakla çalışıyor (5 Kasım)

Yavuz'un anlattığına göre şirkette dış bir servis/yazılım yok (İK, muhasebe vb.); işler evrakla yürüyor ve **hepsi dijitale taşınacak, servisler şirket içinde kurulacak.** Etkisi:

- **Muhasebe aktarımı varsayımı kapandı:** bağlanılacak bir sistem yok. Dahili **muhasebe kuyruğu + CSV + elle işleme** asıl çözümdür; webhook yalnızca ileride bir sistem gelirse kullanılır. Kâğıt kayıt yerine bu kuyruk **kayıt defteri** olur.
- **Kapsam geniştir:** bu bir izin uygulaması değil, evrak süreçlerinin tümünün dijitalleşmesidir. Genel akış motoru buna uygundur: yeni evrak türü çoğu zaman yeni bir **akış tanımıdır**, kod değil. Kod gerektiren şeyler aşağıdaki **G fazı** maddeleridir.

### Evrak envanteri (kod yazmadan önce yapılacak iş)

Her kâğıt süreç için tek satır doldurun; tablo bir sonraki fazın sırasını belirler:

| Soru | Neden |
|---|---|
| Evrakın adı ve **kim, ne sıklıkla** doldurur? | Hangi uygulama/form, kaç kullanıcı |
| **Kim imzalar, hangi sırayla, hangi koşulda** (tutar, gün…)? | Onay adımları ve koşullar |
| Eke ne konur (fiş, fatura, sözleşme, rapor)? | Dosya yükleme gerekir mi |
| **Çıktı** gerekir mi (imzalı kâğıt, dosyaya konan nüsha)? | Yazdırılabilir/PDF çıktı |
| Kim **görür/arşivler**, ne kadar **saklanır**? | Yetki, arama, saklama süresi (KVKK) |
| Sonrasında **kime ne yapılır** (ödeme, kayıt, bordro girişi)? | Muhasebe/İK kuyruğu, rapor |
| Yöneticinin **izinli** olduğu zamanlar? | Vekâlet/devir |

Eşleme örneği: izin, avans, masraf → hazır şablonlar. Satın alma talebi, araç/ekipman talebi, sözleşme/gider onayı, fazla mesai bildirimi → yeni akış tanımı (kod değil). Müşteri/ürün listesi, bordro hesabı, stok → **ayrı modül kararı** (bu bir ERP kapsamıdır; ayrı planlanmalı).

### G fazı: öncelik önerisi (G1–G8 bu sırayla **uygulandı**, bkz. §12; G9 yapılmadı)

| Sıra | Yetenek | Gerekçe |
|---|---|---|
| G1 | **Ekler** (fiş/belge/tarama yükleme; tür ve boyut sınırı, yetkili erişim, saklama) | Masraf ve satın alma evrakı eksiz olmaz; kâğıdın yerini alan en kritik parça |
| G2 | **Yazdırılabilir / PDF çıktı** (form + onay zinciri + zaman damgası + doğrulama no) | Islak imzanın yerine geçen kayıt; denetimde gösterilecek nüsha |
| G3 | **Vekâlet / devir** (izinli yöneticinin yerine vekil onaylayıcı) | Kâğıtta imza “bulunur”, sistemde görev bekleyip takılır |
| G4 | **Bildirim** (e-posta; görev atandı, sonuçlandı) | Onay kutusuna bakmayan kişi talebi bekletir |
| G5 | **Arama, raporlar, dışa aktarma, arşiv/saklama süresi (KVKK)** | Kâğıt arşivin dijital karşılığı |
| G6 | **Ana veri / dinamik listeler** (departman, masraf kategorisi, araç listesi) | Seçeneklerin elle yazılması yerine tek yerden yönetim |
| G7 | **İzin bakiyesi / hak ediş** (+ resmî tatil takvimi) | Bakiye hâlâ elle tutuluyorsa |
| G8 | SSO/AD, mobil uygulama | Kullanıcı sayısı ve talebe göre |

### Önerilen ilerleme

1. **Pilot:** tek evrak (izin) ile başlayın; gerçek çalışanlarla bir ay kullanın, geri bildirimi yazın.
2. Evrak envanterini çıkarın; G fazını envanterden çıkan **gerçek** ihtiyaca göre sıralayın (yukarıdaki sıra bir tahmindir).
3. Her yeni yetenek önce bir evrakın somut ihtiyacıyla gerekçelendirilsin; ERP kapsamındaki modüller ayrı karar olarak ele alınsın.
