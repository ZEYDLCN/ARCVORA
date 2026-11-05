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
| İzin | Tarih aralığı + gün sayısı (otomatik hesap) + açıklama + vekil. **İzin bakiyesi/hak edişi takibi ilk sürümde yok.** | Bakiye takibi ve resmî tatil hesabı gerekir mi? Yazdırılabilir/imzalı çıktı? |
| Bildirim | İlk sürümde **uygulama içi** (onay kutusu, taleplerim). E-posta/WhatsApp yok. | E-posta (SMTP) gerekir mi? |
| Ekler | Fiş/belge yükleme **faz G**'de (masraf için gerekli). İzin için gerekmez. | Masraf akışı ne zaman devreye girecek? |
| ARCVORA ile ilişki | Aynı uygulama, aynı giriş. Mimari tasarım ekranları **yalnızca master admin/düzenleyicilere** görünür; app kullanıcıları yalnızca APP bölümünü görür. Akış tasarlayıcısı ayrı bir ekrandır; "ARCVORA akışından app taslağı" faz G. | Otomatik üretim mi, elle tasarım mı beklenir? |
| Sunucu | Linux + Docker + PostgreSQL 16 varsayıldı (`Dockerfile` bu ortamda derlenmedi; ilk derlemede doğrulanmalı). | Altyapı nedir? |
| Açık kaynak | Bu modül kapalıdır. Herkese açık ARCVORA deposuna push edilmez. | Depo/lisans/mülkiyet kararı sahibine ait. |

## 3) Kapsam dışı (ilk sürüm)

Dosya/belge yükleme (faz G), e-posta/WhatsApp bildirimi, izin bakiyesi ve resmî tatil takvimi, PDF/imzalı çıktı, SSO/AD,
çok adımlı paralel dallanma, form içi tablolar (satır kalemleri), mobil uygulama, raporlama panoları.

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
`RequestEvents`, `AccountingEntries`, `AccountingSettings`. Mevcut `Users`, `Workspaces`, `Memberships`, `AuditEntries` korunur.
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

Hata biçimi: `application/problem+json` `{ status, code, title, details }`. Formda `422 invalid_request` + `details: [{code, path, message}]` (path `data.<alan>`).

## 10) Arayüz

Oturum açan herkes aynı kabuğu görür; menü role göre sadeleşir.

- **App kullanıcısı:** Uygulamalarım · Taleplerim · Onay kutusu · (muhasebe üyesiyse) Muhasebe.
- **Master admin:** yukarıdakilere ek olarak **Mimari projeler** (mevcut ARCVORA), **App tasarla**, **Yönetim** (kullanıcılar, gruplar, app yetkileri, muhasebe ayarları).
- Tasarlayıcı: alan ekle/sırala, adım ekle, onaylayıcı seç, koşul tanımla, **canlı önizleme** (talep eden gözüyle form), doğrulama bulguları, yayınla.

## 11) Güvenlik ilkeleri

- Rol ve üyelik **her istekte sunucuda** doğrulanır; arayüz yalnızca kolaylıktır.
- Çalışma alanı dışı kayıt `404`; yetkisiz ama üye `403`.
- App kullanıcısı mimari projeleri listeleyemez, açamaz, dolaylı yoldan da (ör. örnek proje, dışa aktarma) erişemez.
- Parolalar PBKDF2; yeni kullanıcıya verilen geçici parola ilk girişte değiştirilmeye çağrılır (`mustChangePassword`).
- Webhook gövdesi HMAC ile imzalanır; adres yalnızca master admin tarafından girilir. **Not:** Şirket içi muhasebe sistemleri çoğu kez özel ağdadır; bu nedenle özel IP'lere gönderim engellenmez. Adres yalnızca `http/https` olmalıdır.
- Denetim kaydı: kullanıcı/grup/yetki değişiklikleri, yayınlar, kararlar, muhasebe işlemleri.

## 12) Fazlar ve bitiş ölçütleri

| Faz | Teslim | Durum ve kanıt |
|---|---|---|
| **A** | `app_user` rolü; kullanıcı oluşturma; parola değiştirme/sıfırlama; gruplar; doğrudan yönetici; `app_user` için mimari projelere erişim kapalı | ✅ `UserManagementTests`, `GroupTests`, `PasswordResetTests` |
| **B** | Akış tanımı modeli, doğrulayıcı, form doğrulama, hesaplanan alanlar, onaylayıcı çözümleyici, durum makinesi (Core) | ✅ Core birim testleri (200) |
| **C** | Kalıcılık + API: tasarım/yayın/sürüm, yetki, talep, onay, iptal, zaman çizelgesi, onay kutusu | ✅ `AppsDesignTests`, `AppsRuntimeTests` (paralel karar, sürüm sürekliliği, yalıtım), `RemovedApproverTests` |
| **D** | Muhasebe kuyruğu, webhook işçisi (HMAC, idempotent, yeniden deneme), CSV, işlendi işareti | ✅ `AccountingTests` (imza, yeniden deneme, eşzamanlı işçi, CSV enjeksiyonu) |
| **E** | Web: portal, form, taleplerim, onay kutusu, tasarlayıcı, yönetim, muhasebe; rol bazlı menü | ✅ vitest (140) + tarayıcı yolculuğu `apps-ui.mjs` |
| **F** | Şablonlar (İzin, Avans, Masraf); uçtan uca betikler; şirket dağıtım dokümanı | ✅ `TemplatesTests` (gerçek sunucu), `apps-journey.mjs`, `apps-ui.mjs`; ⬜ `Dockerfile` derlenmedi |
| **G** | (Sonra) ekler/fiş yükleme, e-posta bildirimi, izin bakiyesi, PDF çıktı, ARCVORA akışından taslak, SSO, raporlar | ⬜ |

## 13) Riskler

- **Muhasebe sistemi bilinmiyor:** genel webhook/CSV çözümü gerçek sisteme uymayabilir; bağlayıcı ayrı bir iş olur.
- **Hukuki/ticari:** kodun sahipliği ve gizliliği teknik değil, sözleşme konusudur; geliştirmeye başlamadan netleştirilmelidir.
- **Dockerfile** henüz derlenmedi; ilk kurulumda sorun çıkabilir. Dağıtım adımları: [`DEPLOY_COMPANY.md`](DEPLOY_COMPANY.md).
- **Kullanıcı deneyimi** gerçek çalışanlarla denenmedi; izin formu şirketin gerçek evrakına göre uyarlanmalı.

## 14) Gerçekleşen kapsam ve dürüst sınırlar

Kod, test ve belgeler bu fazlandırmaya göre tamamlandı; kullanım için bkz. [`APPS_KULLANIM.md`](APPS_KULLANIM.md). Aşağıdakiler **yapılmadı veya doğrulanmadı**:

- Gerçek bir muhasebe yazılımına bağlantı (yalnızca imzalı genel webhook + CSV + elle işleme).
- Gerçek çalışanlarla kullanılabilirlik denemesi; izin formu şirketin gerçek evrakına göre uyarlanmalıdır.
- `Dockerfile` derlemesi ve şirket sunucusunda kurulum.
- Fiş/belge yükleme, e-posta/WhatsApp bildirimi, izin bakiyesi/resmî tatil, PDF çıktı, SSO (faz G).
- Yük/performans denemesi: veri modeli yüzlerce çalışan için uygundur ama ölçülmedi.
- Webhook gizli anahtarı veritabanında düz metindir (imza üretmek için); yedekleri koruyun.
- Parola sıfırlama e-postayla değil, master admin eliyledir; yalnızca master admin'in açtığı hesaplar sıfırlanabilir.

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

### G fazı: öncelik önerisi

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
