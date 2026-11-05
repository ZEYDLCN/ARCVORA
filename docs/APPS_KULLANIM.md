# ARCVORA Apps — kullanım rehberi (master admin, çalışan, onaylayıcı, muhasebe)

> **Gizli:** Bu modül şirket ürünüdür; açık kaynak değildir. Kapsam, varsayımlar ve faz planı: [`APPS_PLATFORM.md`](APPS_PLATFORM.md).
> Sunucuya kurulum: [`DEPLOY_COMPANY.md`](DEPLOY_COMPANY.md).

## 1) Kim ne yapar?

| Rol | Menüde ne görür | Ne yapabilir |
|---|---|---|
| **Master admin** | Uygulamalar · Mimari · Yönetim · Hesap | Her şeyi tasarlar: kullanıcı/grup açar, uygulama tasarlar ve yayınlar, yetki verir, muhasebe ayarlarını yapar, tüm talepleri görür. Yayınlanmış her uygulamayı kullanabilir. |
| **Uygulama kullanıcısı** (`app_user`) | Uygulamalar · Hesap | **Yalnızca kendisine yetki verilen** uygulamalardan talep açar ve taleplerini izler. Mimari projeleri ve tasarım/yönetim sayfalarını **göremez** (sunucu da reddeder). |
| Düzenleyici / okuyucu | Uygulamalar · Mimari · Hesap | ARCVORA mimari projelerinde çalışır (yetkisi kadar). Uygulama tasarlayamaz. |
| **Onaylayıcı** | (rol değil, yetenek) | Bir akış adımı kendisine atandığında **Onay kutusu**nda görür; uygulama yetkisi gerekmez. |
| **Muhasebe** | Uygulamalar · **Muhasebe** · Hesap | Ayarlarda “muhasebe grubu” olarak seçilen grubun üyeleri kuyruğu görür, kayıtları işler, CSV indirir. |

## 2) İlk kurulum (master admin)

1. Sunucuyu `Auth__RegistrationOpen=true` ile başlatın, **ilk hesabı** kendiniz açın (kayıt ekranından). Bu hesap master admin olur ve şirketin çalışma alanını oluşturur.
2. Hesabınızı açtıktan sonra `Auth__RegistrationOpen=false` yapıp yeniden başlatın: dışarıdan kimse kayıt olamaz.
3. **Yönetim → Kullanıcılar → Yeni kullanıcı**: ad, e-posta, rol (çoğu çalışan için “Uygulama kullanıcısı”) ve **doğrudan yönetici**. Sistem bir **geçici parola** önerir; kullanıcıya güvenli bir yolla iletin. Kullanıcı ilk girişte kendi parolasını belirlemek zorundadır.
4. **Doğrudan yönetici** “yönetici onayı” adımını çözer. Yöneticisi olmayan çalışanın talebi master admin’e düşer ve bu açıkça belirtilir.
5. **Yönetim → Gruplar**: ör. “Muhasebe”, “Genel müdür”. Gruplar onaylayıcı veya muhasebe ekibi olarak kullanılır.
6. En az **iki** master admin tutun (son master admin silinemez/düşürülemez).

## 3) Uygulama tasarlama

**Uygulama tasarla** → hazır şablondan başlayın (İzin, Avans, Masraf) ya da boş bir uygulama açın. Şablonlar şirketin gerçek evrakına göre uyarlanmak üzere başlangıç noktasıdır.

**Alanlar**

| Tür | Not |
|---|---|
| Kısa / uzun metin | “En fazla karakter” sınırı verilebilir. |
| Sayı, Tutar | En küçük/en büyük değer; tutar en fazla 2 ondalık ve varsayılan olarak negatif olamaz. |
| Tarih | “Şu tarihten önce olamaz” (ör. bitiş ≥ başlangıç). |
| Tek / çoklu seçim | Seçenekler her satıra bir tane. |
| Onay kutusu | Zorunluysa işaretli olmalı (ör. beyan). |
| Kişi | Çalışma alanı üyelerinden seçilir (ör. vekil). |
| **Otomatik hesap** | Bir sayı alanı iki tarihten hesaplanabilir: takvim günü (uçlar dahil) veya hafta içi günü. Değeri **sunucu** üretir. |

**Onay adımları** sırayla çalışır.

- **Kim onaylar:** talep edenin yöneticisi · belirli bir kişi · bir grup (biri onaylasa yeter *veya* hepsi onaylamalı) · master admin.
- **Koşul:** boşsa her talepte çalışır; doluysa hepsi sağlanınca (ör. gün sayısı > 10, tutar > 10.000). Sağlanmazsa adım **atlanır**; tüm adımlar atlanırsa talep otomatik onaylanır.
- Talep eden kendi talebini onaylamaz. Tek onaylayıcı kendisiyse ve başka master admin yoksa kendi talebini onaylayabilir; bu zaman çizelgesinde işaretlenir.

**Tamamlanma:** “Onaylanınca muhasebe kuyruğuna ekle” + kategori.

**Önizleme ve sorunlar:** sağda talep edenin göreceği form canlı görünür; “Sorunlar” kutusu yayını engelleyen eksikleri (ör. onaylayıcı seçilmemiş, geçersiz önek) anında söyler. Sunucu yayın sırasında ayrıca onaylayıcı kişi/grubun var olduğunu doğrular.

**Kaydet / Yayınla:** taslak istediğiniz kadar kaydedilir; **yayınla** değişmez bir sürüm (v1, v2…) üretir. **Açık talepler açıldıkları sürümle devam eder**, yeni talepler en son sürümü kullanır. Aynı taslağı iki yönetici aynı anda düzenlerse ikincisi uyarılır (sessizce ezilmez).

**Yetkiler sekmesi:** uygulamayı kullanabilecek kişi ve grupları seçin. Seçilmeyen çalışan uygulamayı listede görmez, formu açamaz ve talep oluşturamaz.

**Arşivle:** uygulama kullanıma kapanır; mevcut talepler sürer. Hiç yayınlanmamış taslaklar silinebilir.

## 4) Çalışan: talep açma ve izleme

1. **Uygulamalarım** → uygulamayı seçin → formu doldurun (hatalar alanın altında görünür) → **Talebi gönder**.
2. Talep numarası verilir (ör. `IZN-2026-0007`). Aynı formu çift tıklasanız da tek talep oluşur.
3. **Taleplerim**: durum (İncelemede / Onaylandı / Reddedildi / İptal), kimde beklediği, zaman çizelgesi.
4. İnceleme sürerken talebi **iptal edebilirsiniz**; sonuçlanmış talep iptal edilemez.

## 5) Onaylayıcı

**Onay kutusu** menüdeki rozetle bekleyen görev sayısını gösterir. Talebi açın → **Onayla** (yorum isteğe bağlı) veya **Reddet** (gerekçe **zorunlu**). Aynı adımda birden fazla kişi varsa ve “biri yeter” ise ilk karar adımı bitirir, diğer görevler gereksiz kalır.

“Yönlendirilmiş görev” rozeti, sizin yöneticiniz/grup üyesi olduğunuz için değil, talep edenin yöneticisi (veya grup) tanımlı olmadığı için size düştüğünü söyler.

## 6) Muhasebe

Onaylanan talep (uygulamada muhasebe açıksa) **aynı işlemde** muhasebe kuyruğuna yazılır; kayıt ya birlikte oluşur ya hiç oluşmaz.

- **Muhasebe** menüsü: kayıtlar, durum filtresi (Beklemede / Gönderildi / İşlendi / Hata), ayrıntıda talebin tüm alanları ve onay bilgileri.
- **İşlendi:** kaydı elle işlediğinizi işaretler (isteğe bağlı not).
- **CSV indir:** Excel’de açılacak biçimde (UTF‑8, `;` ayırıcı). Kullanıcının yazdığı `=`, `+`, `-`, `@` ile başlayan metinlerin önüne `'` konur; Excel bunları formül olarak çalıştırmaz. Saatler UTC’dir.
- **Webhook (isteğe bağlı):** Yönetim → Muhasebe ayarları. Adres tanımlıysa her kayıt JSON olarak `POST` edilir; başarısız olursa artan aralıkla en fazla 8 kez denenir, sonra **Hata** olur ve “Yeniden dene” görünür. Adres tanımlı değilse kayıtlar beklemede kalır.

Webhook istek başlıkları ve doğrulama:

| Başlık | Anlam |
|---|---|
| `Idempotency-Key` | Kayıt kimliği. Alıcı aynı anahtarı ikinci kez görürse yok saymalıdır (yeniden denemelerde tekrar gelebilir). |
| `X-Arcvora-Event` | `accounting.entry` |
| `X-Arcvora-Signature` | `sha256=<HMAC-SHA256(gizli anahtar, ham gövde)>` (gizli anahtar tanımlıysa) |

```js
// Alıcı tarafta imza doğrulama (Node)
import crypto from 'node:crypto';
const expected = 'sha256=' + crypto.createHmac('sha256', process.env.ARCVORA_SECRET).update(rawBody).digest('hex');
const ok = crypto.timingSafeEqual(Buffer.from(expected), Buffer.from(req.headers['x-arcvora-signature'] ?? ''));
```

Gövde örneği: `entryId`, `requestId`, `number`, `app {id,name}`, `category`, `requester {id,name,email}`, `submittedAt`, `approvedAt`, `summary`, `fields [{key,label,type,value,currency?,valueLabel?}]`, `approvals [{step,by{id,name},at,comment}]`.

## 7) Güvenlik ve sınırlar

- Yetki **her istekte sunucuda** doğrulanır; menüden gizlenen sayfa API’den de kapalıdır. Başka çalışma alanına ait kayıt için `404` döner.
- Kullanıcının yazdığı metinler hiçbir yerde HTML olarak çalıştırılmaz.
- Webhook gizli anahtarı veritabanında düz metin saklanır (imza üretmek için gerekli) ve arayüze/API’ye geri verilmez; veritabanı yedeklerini ve erişimini buna göre koruyun.
- Webhook adresi özel (şirket içi) ağda olabilir; bu bilerek engellenmemiştir. Adresi yalnızca master admin girebilir.
- **İlk sürümde yok:** dosya/fiş yükleme, e-posta/WhatsApp bildirimi, izin bakiyesi ve resmî tatil takvimi, PDF/imzalı çıktı, SSO/Active Directory, form içi tablolar. Bkz. `APPS_PLATFORM.md` §3.
- İzin bakiyesi tutulmaz: sistem izin **kaydını ve onayını** yönetir, hak edişi hesaplamaz.

## 8) Sorun giderme

| Belirti | Neden / çözüm |
|---|---|
| Çalışan uygulamayı görmüyor | Uygulama yayınlanmamış/arşivde ya da çalışana yetki verilmemiş (Uygulama tasarla → Yetkiler). |
| “Bu uygulama şu anda kullanıma açık değil” | Uygulama arşivde veya hiç yayınlanmamış. |
| Talep master admin’e düştü | Çalışanın doğrudan yöneticisi tanımlı değil (Yönetim → Kullanıcılar) veya seçilen grup boş. |
| Yayınla düğmesi kapalı | Sağdaki “Sorunlar” kutusunu okuyun; her sorunun alanı yazılıdır. |
| “Taslak başka bir yönetici tarafından değiştirilmiş” | Sayfayı yenileyin; iki yönetici aynı taslağı düzenledi. |
| Kayıt “Hata” durumunda | Webhook adresine ulaşılamadı veya 2xx dönmedi (ayrıntı satırda). Alıcıyı düzeltin → **Yeniden dene** veya elle **İşlendi**. |
| Kullanıcı parolasını unuttu | **Yönetim → Kullanıcılar → Parola sıfırla**: yeni bir geçici parola üretilir, kullanıcı ilk girişte kendi parolasını belirler. Yalnızca master admin’in **kendi açtığı** hesaplar için çalışır; kendi kaydıyla açılmış (başka bir çalışma alanına da sahip olabilecek) hesapların parolasını yalnızca sahibi değiştirebilir. |
