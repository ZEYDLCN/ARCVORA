# ARCVORA Apps — kullanım rehberi (master admin, çalışan, onaylayıcı, muhasebe)

> **Gizli:** Bu modül şirket ürünüdür; açık kaynak değildir. Kapsam, varsayımlar ve faz planı: [`APPS_PLATFORM.md`](APPS_PLATFORM.md).
> Sunucuya kurulum: [`DEPLOY_COMPANY.md`](DEPLOY_COMPANY.md).

## 1) Kim ne yapar?

| Rol | Menüde ne görür | Ne yapabilir |
|---|---|---|
| **Master admin** | Uygulamalar · Mimari · Uygulama tasarla · Yönetim · Vekâletlerim · Belge doğrula · Hesap | Her şeyi tasarlar: kullanıcı/grup açar, uygulama tasarlar ve yayınlar, yetki verir, muhasebe ayarlarını yapar, tüm talepleri görür. Yayınlanmış her uygulamayı kullanabilir. |
| **Uygulama kullanıcısı** (`app_user`) | Uygulamalar · Taleplerim · Onay kutusu · Vekâletlerim · Belge doğrula · Hesap | **Yalnızca kendisine yetki verilen** uygulamalardan talep açar ve taleplerini izler. Mimari projeleri ve tasarım/yönetim sayfalarını **göremez** (sunucu da reddeder). |
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

## 6a) Ekler: fiş, fatura, belge, tarama

Formda **Dosya** türünde bir alan (ör. masraf şablonunda “Fiş/fatura”) açılır. **Uygulama tasarla → alan → Dosya**: “en çok kaç dosya” ve zorunluluk verilir.

- Çalışan **Dosya seç** ile yükler (PDF, JPEG, PNG, WebP) veya telefonda **Kamera ile çek** düğmesiyle fişin fotoğrafını doğrudan çekip ekler. Dosya türü **içeriğinden** anlaşılır; uzantısı/bildirilen türü yanlışsa (ör. `.exe` adı verilmiş bir fotoğraf) adı düzeltilir, gerçekten desteklenmeyen içerik (SVG, HTML, arşiv, yürütülebilir) **reddedilir**.
- Tek dosya en çok **10 MB** (yönetici 25 MB’a kadar yükseltebilir: `Attachments__MaxSizeMb`). Boş dosya kabul edilmez.
- Yüklenen dosya talep gönderilene kadar yalnızca yükleyene görünür; gönderilmezse 24 saat sonra temizlenir. Aynı dosya iki talebe eklenemez.
- Dosyayı yalnızca **talebi görebilenler** açar/indirir: talep eden, onaylayıcılar (görev atandıysa), vekil, ilgili muhasebe üyesi, master admin. Başkası `404` alır (dosyanın varlığı bile ifşa edilmez). Dosyalar tarayıcıda **yürütülmez**: her zaman indirme olarak (`attachment`, `nosniff`, önbelleksiz) sunulur.
- Dosya içeriği **virüs taramasından geçmez**; şirket politikanız gerekiyorsa ters proxy/uç nokta güvenliğinde taratın.
- Talep silindiğinde (saklama süresi) ekleri de silinir.

## 6b) Yazdırılabilir PDF ve belge doğrulama

Talep ayrıntısında **PDF indir**: form alanları, onay zinciri (kim, ne zaman, ne dedi), taleple ilgili sistem notları, alt bilgide **talep numarası ve doğrulama kodu** içeren A4 çıktıdır. Eklenen **PNG/JPEG** fotoğraf ve fişler (4 MB’a kadar, en çok 8 görsel) belgenin sonuna **gömülür**; PDF, WebP veya büyük dosyalar yalnızca **adıyla listelenir** (dosyanın kendisi talepten indirilir).

- Bu çıktı ıslak imzanın **yerine geçen kayıt** olarak tasarlanmıştır; hukuken geçerliliği şirketin iç yönetmeliğine/danışmanına bağlıdır (kod bunu garanti edemez).
- **Belge doğrula** menüsü (giriş yapmış herkes): basılı formdaki talep numarası + doğrulama kodunu girin; sistem belgenin **gerçek, değiştirilmemiş ve hangi durumda olduğunu** söyler. Kod, talebin numarası, **durumu**, sonuç zamanı ve verisinden gizli bir anahtarla (HMAC) üretilir. Bu yüzden talep sonradan değişirse (ör. onaydan sonra iptal) **eski çıktının kodu artık doğrulanmaz** — yani basılı kâğıdın güncel olup olmadığı anlaşılır. Doğrulama sonucu talebin güncel durumunu da gösterir. Anahtar `Jwt__Key`’den türetilir: **`Jwt__Key`’i değiştirirseniz daha önce basılmış tüm çıktıların kodu geçersiz olur.**
- Yanlış numara, uydurma kod veya başka çalışma alanının numarası aynı biçimde “doğrulanamadı” döner; hangi parçasının yanlış olduğu söylenmez. Doğrulayan kişi aynı çalışma alanının üyesi olmalıdır.

## 6c) Vekâlet (yetki devri) ve yeniden atama

Yönetici izinliyken talepler beklemesin diye: **Vekâletlerim → Yeni vekâlet** → vekil, **başlangıç–bitiş (dahil)**, isteğe bağlı açıklama.

- Vekil, o günler arasında vekâlet verenin **Onay kutusu**ndaki görevleri “vekâleten” rozetiyle görür ve karara bağlar; kararın zaman çizelgesinde “*X adına Y*” yazar.
- Günler çalışma alanının **yerel saatine** göre hesaplanır (Türkiye UTC+3); süre bitince yetki kendiliğinden biter, ayrıca bir işlem gerekmez.
- Vekâlet **zincirlenmez** (vekilin vekili yetkili olmaz); kişi kendine vekil olamaz; kimse kendi talebini vekâlet yoluyla da onaylayamaz; aynı adımda aynı kişi iki kez karar veremez. Bir kişinin **aynı günlere denk gelen ikinci vekâleti** reddedilir (önce eskisini iptal edin).
- Vekâlet veren, vekil veya master admin vekâleti **iptal edebilir**; iptalde karşı tarafa e-posta bildirimi gider (e-posta açıksa). Master admin “Tüm vekâletler”i görür ve başkası adına vekâlet açabilir.
- Kullanıcı çalışma alanından çıkarılırsa onunla ilgili vekâletler iptal edilir.
- **Yeniden atama (master admin):** bekleyen bir görev, onaylayıcı ayrıldığı/ulaşılamadığı için başka birine devredilebilir (talep ayrıntısı → bekleyen görevin yanındaki “Yeniden ata…” seçici). Yeni kişi aktif üye olmalıdır; olay çizelgeye yazılır.

## 6d) E-posta bildirimleri

SMTP kuruluysa (`DEPLOY_COMPANY.md` §5a) sistem şu durumlarda **Türkçe e-posta** gönderir: onayınız bekleniyor, hatırlatma (24 saatte bir, en çok 3), talebiniz onaylandı/reddedildi/iptal edildi, vekâlet verildi/iptal edildi, hesabınız oluşturuldu.

- Mailler hiçbir zaman **parola içermez** (geçici parola size yöneticinizce ayrıca iletilir); kullanıcı metni HTML olarak çalıştırılmaz, başlık satırı enjeksiyonuna karşı temizlenir.
- Her kullanıcı **Hesap → E-posta bildirimleri** ile kendi bildirimlerini kapatabilir.
- **Yönetim → E-posta**: durum (açık/kapalı), **test e-postası**, gönderim kuyruğu (bekleyen/gönderildi/atlandı/başarısız) ve “Yeniden dene”. SMTP kapalıyken hiçbir mail kuyruğa alınmaz; sistem normal çalışır.
- Gönderim **kuyruk (outbox)** üzerindendir: mail sunucusu geçici kapalıysa talep/onay etkilenmez, mail artan aralıkla en çok 6 kez denenir.
- Onaylayıcı **vekâlet vermişse** (vekâlet aralığı sürerken), görev ve hatırlatma e-postası **vekile de** “vekâleten” başlığıyla gider; talep edenin kendisi vekilse kendi talebi için mail almaz.
- E-posta güvenilir bir **bildirim**dir, onay yolu değildir: onay yalnızca uygulamada yapılır.

## 6e) Arama, raporlar, dışa aktarma

- **Yönetim → Tüm talepler:** numara, özet veya kişi adına göre arama; durum, uygulama ve tarih aralığı süzgeci; sayfalama. Talep eden kendi taleplerini **Taleplerim**’de arar.
- **Yönetim → Raporlar:** dönem içinde toplam/durum dökümü, uygulama başına sayılar, **onaylayıcı yükü**, belirli saati aşan **geciken görevler**. Demo veriyi dahil etme seçeneği vardır (varsayılan: demo hariç).
- **CSV:** talepler ve muhasebe kaydı Excel’de açılacak biçimde (UTF‑8, `;`) indirilir; formül enjeksiyonu korumalıdır (§6).
- **Denetim (Yönetim → Denetim):** kim, ne zaman, hangi işlemi yaptı (kullanıcı/grup/yetki/yayın/karar/saklama/anonimleştirme…); işlem ve tarih süzgeci, sayfalama.

## 6f) Saklama süresi ve KVKK

**Yönetim → Saklama ve KVKK**

- **Saklama süresi:** sonuçlanmış talepler (onaylı/reddedilmiş/iptal) seçilen süreden sonra silinebilir. Açık talepler ve **muhasebede işlenmemiş kayıtlar asla silinmez**. Önce **önizleme** (kaç talep silinir) gösterilir, siz **onaylayınca** silinir; “otomatik sil” yalnızca bilerek açılırsa bakım turunda çalışır. Talep silinince görevleri, geçmişi ve **ekleri** birlikte gider.
- **Anonimleştirme:** çalışma alanından çıkarılmış hesabın adı/e-postası **geri döndürülemez biçimde** silinir; geçmiş talepler “Silinmiş kullanıcı” adıyla korunur (denetim ve muhasebe kaydı bozulmaz).
- **Kişisel veri dışa aktarma (Kullanıcılar sekmesi):** bir kişinin sistemdeki verisi — hesap bilgisi, açtığı talepler (alan değerleriyle), verdiği kararlar, vekâletleri, yüklediği dosyaların listesi, izin hakları ve düşümleri ve bildirim tercihi — JSON olarak indirilir (KVKK m.11 başvurularında kullanılır). Dosyaların kendisi pakete konmaz.
- Bunlar araçlardır; **saklama süreleri ve aydınlatma metni** şirketin hukuk danışmanıyla belirlenmelidir.

## 6g) Listeler (ana veri)

**Yönetim → Listeler:** departman, masraf kategorisi, araç gibi seçenekleri **tek yerden** yönetin. Formda “Liste’den seç” türünde bir alan bu listeye bağlanır.

- Talep, seçilen **metni kendi içinde saklar**. Bir öğeyi sonradan **pasif yapmak, yeniden adlandırmak veya silmek** yeni taleplerde seçeneği değiştirir; **eski talepler gönderildikleri andaki metni göstermeye devam eder** (geçmiş kayıt değişmez).
- Bir uygulamanın kullandığı liste **silinemez**; var olmayan veya başka çalışma alanına ait bir liste bağlanmış uygulama yayınlanmaz. Liste adı çalışma alanında benzersizdir (büyük/küçük harf fark etmez). Bir liste sonradan boşaltılır/silinirse o alanı olan formlar **açıkça hata verir**, her değeri kabul etmez.

## 6h) İzin bakiyesi ve resmî tatil takvimi

**Yönetim → İzin ve tatiller**

- **Resmî tatil takvimi:** tarihi sabit tatiller tek tuşla eklenir; **dinî bayramlar her yıl elle girilir** (yıl başında, yarım gün/arife seçeneğiyle). “İş günü” hesabı (otomatik hesap alanı) bu takvimi kullanır; hafta sonu ve tatiller sayılmaz.
- **Bakiye:** kişinin yıllık izni **işe giriş tarihinden** 4857 sayılı Kanun m.53’e göre hesaplanır (1–5 yıl: 14, 5–15 yıl: 20, 15+ yıl: 26 gün). İşe giriş tarihini **Kullanıcılar** sekmesinden girin. “Kıdem haklarını dağıt” yıl için hakları yazar; **yaş (18 altı/50 üstü) ve sözleşme istisnaları** otomatik uygulanmaz, **düzeltme (+/−)** ekleyerek yansıtın. Devir için üst sınır belirlenebilir.
- **İzin akışına bağlama:** tasarlayıcıda “İzin bakiyesinden düş” açılır; gün sayısı ve başlangıç tarihi alanları seçilir. İsteğe bağlı **izin türü alanı** seçilirse yalnızca işaretli türler bakiyeden düşer (ör. mazeret izni düşmez). Çalışan talebi **gönderdiğinde** gün **rezerve edilir** (bakiye yetmiyorsa form baştan uyarır ve sunucu reddeder); **reddedilirse veya iptal edilirse iade edilir**; onaylanınca kalıcı düşer. Aynı anda iki talep açarak bakiyeyi aşmak mümkün değildir.
- Çalışan kendi bakiyesini form üzerinde görür. Hareketler (hak, düşüm, iade, düzeltme) silinmez, **kayıt defteri** olarak tutulur.

## 6i) Tek oturum açma ve mobil

- **SSO:** kuruluysa (`DEPLOY_COMPANY.md` §5b) giriş sayfasında “Şirket hesabıyla giriş” çıkar. Yönetici daha önce açmadıysa kişinin hesabı **otomatik açılmayabilir** (“bu hesapla giriş yapma izniniz yok”); önce **Yönetim → Kullanıcılar**’dan açın. Parola ile giriş de devam eder.
- **Mobil:** arayüz telefona uyumludur; tarayıcıda **“Ana ekrana ekle”** ile uygulama gibi açılır. Fiş fotoğrafı doğrudan kameradan yüklenebilir. Çevrimdışıyken uygulama açılır ama veri/işlem için bağlantı gerekir.

## 6j) Demo veri

**Yönetim → Demo veri → Örnek veri yükle:** sistemi denemek ve ekibe göstermek için örnek kullanıcılar, hazır şablonlardan uygulamalar, her durumda talepler (onaylı/reddedilmiş/iptal/bekleyen/vekâletli) ve **örnek fiş/fatura dosyaları** üretir. Hepsi “demo” olarak işaretlidir. Demo kullanıcıların **tek seferlik parolaları yükleme sonunda bir kez gösterilir**, ilk girişte değiştirmeleri zorunludur. Raporlar demo veriyi varsayılan olarak **dahil etmez**. Demo zaten yüklüyse ikinci kez yüklenmez.

**Gerçek veriye geçmeden önce “Demo veriyi sil”:** yalnızca demo işaretli kayıtları siler; gerçek kullanıcı ve talepler etkilenmez (ve demo kullanıcılar gerçek kayıtlara bağlıysa silme reddedilir).

## 7) Güvenlik ve sınırlar

- Yetki **her istekte sunucuda** doğrulanır; menüden gizlenen sayfa API’den de kapalıdır. Başka çalışma alanına ait kayıt için `404` döner.
- Kullanıcının yazdığı metinler hiçbir yerde HTML olarak çalıştırılmaz.
- Webhook gizli anahtarı veritabanında düz metin saklanır (imza üretmek için gerekli) ve arayüze/API’ye geri verilmez; veritabanı yedeklerini ve erişimini buna göre koruyun.
- Webhook adresi özel (şirket içi) ağda olabilir; bu bilerek engellenmemiştir. Adresi yalnızca master admin girebilir.
- **Yok / sınırlar:** WhatsApp/SMS bildirimi · ıslak imza veya e-imza (PDF doğrulama kodu bir **kayıt** doğrulamasıdır, nitelikli elektronik imza değildir) · form içi tablolar · yerel Active Directory’ye doğrudan LDAP (yalnızca OIDC: Entra ID/ADFS/Keycloak) · yerel (native) mobil uygulama · yüklenen dosyalarda virüs taraması · dinî bayram tarihlerinin otomatik hesabı · kıdem dışı izin türleri (doğum, evlilik, ölüm izni vb. için ayrı bir uygulama/akış tanımlayın). Bkz. `APPS_PLATFORM.md` §14.
- İzin hakkı yalnızca **kıdeme göre** hesaplanır (§6h); yaş/sözleşme istisnaları düzeltmeyle girilir.

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
| Dosya yüklenmiyor (`413`/“çok büyük”) | Dosya sınırı aşıldı (varsayılan 10 MB) veya ters proxy gövde sınırı küçük (`DEPLOY_COMPANY.md` §3). |
| “Desteklenmeyen dosya türü” | Yalnızca PDF, JPEG, PNG, WebP. Telefon fotoğrafı HEIC ise JPEG olarak paylaşın. |
| Onay maili gelmiyor | **Yönetim → E-posta**: kapalı mı, kuyrukta “başarısız” kaydı var mı (neden satırda), kullanıcı bildirimi kapatmış mı, spam klasörü. |
| Vekil görevi göremiyor | Vekâlet tarih aralığında mı (yerel gün, bitiş dahil), iptal edilmiş mi, vekil de aynı kişinin vekili mi (zincirlenmez). |
| İzin talebi “bakiyeniz yetersiz” diyor | **Yönetim → İzin ve tatiller**’de kişinin hakkı yazılmış mı, işe giriş tarihi girilmiş mi; bekleyen başka bir talep gün rezerve etmiş olabilir. |
| İş günü hesabı hafta sonu/tatili de sayıyor | Resmî tatil takvimi o yıl için girilmemiş (özellikle dinî bayramlar). |
| SSO ile “giriş yapma izniniz yok” | Hesap yönetici tarafından açılmamış ve otomatik hesap açma kapalı; veya e-posta alan adı izinli değil. |
| PDF’de Türkçe karakter/görsel eksik | Görsel boyutu PDF sınırını (4 MB) aşıyorsa yalnızca listelenir; dosyanın kendisi talepten indirilebilir. |
