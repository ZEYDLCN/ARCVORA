# Pilot ve doğrulama rehberi (Faz 0)

Bu belge **kod dışı** işi tarif eder (ürün dokümanı §22). Yazılım bu varsayımları doğrulamaz; yalnızca doğrulamayı mümkün kılar.
Beğeni ve genel ilgi satın alma kanıtı sayılmaz.

## Kimle?

Gerçekten müşteri projesi teslim eden **beş** ekip (müşteriye özel web uygulaması / B2B SaaS geliştiren küçük ekipler ve ajanslar).
İlk satın alma hipotezi: teknik lider veya ajans sahibi. Son kullanıcı ile satın alma kararını verenin aynı kişi olup olmadığını görüşmelerde sorun.

Her ekipten geçmiş bir gereksinim paketi veya başlayan bir proje isteyin. İlk çıktı gerekirse **elle desteklenebilir**; otomasyon düzeyini ekibe açıkça söyleyin.
Kullanıcı belgelerini izin almadan pazarlama örneğine dönüştürmeyin.

## Görüşme soruları

1. Son projenizde geliştirmeye başladıktan sonra hangi karar eksik çıktı?
2. Bu eksik kararı kim fark etti ve neyi yeniden yapmak zorunda kaldınız?
3. Şu anda teknik devir için hangi belgeleri hazırlıyorsunuz?
4. Yeni müşteri talebi geldiğinde etkiyi nasıl değerlendiriyorsunuz?
5. Hazırladığımız paketin hangi bölümünü gerçek işinizde kullandınız?

## Güçlü ve zayıf sinyaller

| Güçlü | Zayıf |
|---|---|
| Kullanıcı proje modelini **düzeltiyor** | "Güzel diyagram" |
| Gerçek bir **değişiklik talebi** getiriyor | Tek seferlik belge istiyor, geri dönmüyor |
| Sonraki iş için **ödeme** yapıyor | Genel ilgi / beğeni |
| Çıktının bir bölümünü gerçek işinde kullanıyor | Önerileri sürekli baştan yazıyor |

## Karar kuralları (§22.3)

- İlk çıktıdan sonra dönmüyorlarsa → değişiklik akışının değeri incelenir.
- Yalnızca tek seferlik belge istiyorlarsa → abonelik varsayımı yeniden ele alınır.
- Önerileri sürekli baştan yazıyorlarsa → kapsam daraltılır ve ilgili teknoloji profili iyileştirilir.

## Pilot sırasında kaydedilecekler (uygulamadaki karşılıkları)

| Soru | Nereden |
|---|---|
| Hangi öneri kabul edildi / reddedildi / kısmen kabul edildi? | `Proposal` durumu ve `AcceptedGroupsJson` |
| Kullanıcı modeli ne kadar düzeltti? | Sürüm geçmişi (`kind=edit`) ve denetim kaydı |
| Kabul edilen paket başına gerçek maliyet | `Proposal.TokensUsed`, `RepairsUsed` (sağlayıcı fiyatıyla çarpılır) — *maliyet panosu henüz yok* |
| Hangi çıktı hangi tasarıma ait? | `Artifact`: model hash'i + profil/üretici sürümü |
| Başarısız işler | `Job.ErrorCode`, `Stage`, `Problems` |

## Başarı eşikleri

**Pilot öncesi kararlaştırılır** (kullanılabilir taslak oranı, kritik eksik yakalama, gereksiz bulgu yükü, değişiklik kapsaması, üretim dayanıklılığı, çıktı maliyeti — §20.3).
Bu depoda ölçülmemiş doğruluk, performans veya zaman kazancı yüzdesi yoktur ve eklenmemelidir.

## Ön koşul kontrol listesi (canlı kullanıcı verisi girmeden önce)

- [ ] LLM sağlayıcısının veri saklama ve model eğitimi koşulları doğrulandı; sözleşme/ayar uygun.
- [ ] Kaynak metindeki kişisel/ticari veri için kullanıcıya açık uyarı veriliyor (hazır değil).
- [ ] Silme ve saklama politikası, yedeklerdeki veri ömrüyle birlikte tanımlandı (hazır değil).
- [ ] Marka, alan adı ve sosyal hesap uygunluğu kontrol edildi (doküman §2.1).
- [ ] `Jwt__Key` ve veritabanı parolası gizli yönetimden geliyor; TLS arkasında çalışıyor.
