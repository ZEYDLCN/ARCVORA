# Yerel model (Qwen) ile çalıştırma

ARCVORA, OpenAI uyumlu (`/v1/chat/completions`) herhangi bir yerel sunucuyla çalışır: **Ollama**, **llama.cpp (`llama-server`)**, **vLLM**, **LM Studio**.
Proje metinleri yalnızca sizin sunucunuza gider; dış sağlayıcıya çıkmaz (§19) — **ancak sunucunun gerçekten yerel/özel ağda olması ve telemetri göndermemesi koşuluyla**.

> **Durum (dürüst not):** istemci, çıktı şeması ve boru hattı bağlantısı testlerle ve sahte bir OpenAI uyumlu sunucuyla doğrulandı.
> **Gerçek bir Qwen çalıştırması ve model kalitesi ölçümü yapılmadı**: geliştirme ortamında model ağırlıklarının indirildiği adres (`us.aws.cdn.hf.co`) ağ politikasıyla engelliydi ve GPU yoktu.
> Aşağıdaki hız/boyut rakamları sizin makinenizde ölçülmelidir.

## Nasıl çalışır?

- `Ai__Provider=local` → `OpenAiCompatibleLlmClient` + `LlmArchitectureGenerator`.
- Anthropic'teki zorunlu araç çağrısı yerine **şema kısıtlı çıktı** (`response_format: json_schema`) kullanılır. Şema, `ModelDraft` kayıt tiplerinden **türetilir** (enum değerleri, zorunlu alanlar, bilinmeyen alan yok), böylece model ile şema sapmaz. Sunucu tarafında kısıtlı çözümleme geçerli JSON'u büyük ölçüde garanti eder.
- Çıktı yine **güvenilmeyen girdidir**: kimlik çözümü, `GeneratorGuard`, doğrulayıcı ve sınırlı onarım döngüsü aynen çalışır. Zayıf bir model bu yüzden en kötü ihtimalle iş `validate` aşamasında başarısız olur; kabul edilmiş modele dokunmaz.
- `<think>…</think>` blokları ve kod çitleri temizlenir. Qwen3 gibi düşünen modellerde `Ai__Local__DisableThinking=true` isteğe `/no_think` ekler.
- Teklif listesinde sağlayıcı `local:<model>` olarak görünür ve “Yapay zekâ” diye etiketlenir.

## 1) Sunucuyu başlatın

**Önemli — bağlam penceresi:** örnek proje için bile istem ≈ 21 bin karakterdir (≈ 5–6 bin token; model büyüdükçe artar) ve çıktı birkaç bin token ister.
**En az 16k bağlam** verin. Ollama'nın varsayılan bağlamı küçüktür ve fazlasını **sessizce kırpar** → bozuk taslak.

### Ollama

```bash
OLLAMA_CONTEXT_LENGTH=16384 ollama serve        # güncel bir Ollama (yapılandırılmış çıktı desteği için)
ollama pull qwen2.5:7b-instruct                  # veya qwen2.5:14b-instruct, qwen3:8b …
```

### llama.cpp

```bash
llama-server -m qwen2.5-7b-instruct-q4_k_m.gguf --alias qwen2.5-7b-instruct -c 16384 -np 1 --port 8080
```

(GGUF dosyaları Hugging Face'te `Qwen/Qwen2.5-7B-Instruct-GGUF` gibi depolardadır.)

## 2) Sunucunuzu kontrol edin

```bash
scripts/check-local-llm.sh http://localhost:11434 qwen2.5:7b-instruct      # Ollama
scripts/check-local-llm.sh http://localhost:8080  qwen2.5-7b-instruct      # llama.cpp (--alias)
```

Betik: sunucuya ulaşılıyor mu, `json_schema` çalışıyor mu ve yaklaşık **token/sn**. Bir taslak birkaç bin çıktı token'ıdır; süre ≈ token / (token/sn).

## 3) ARCVORA'yı yönlendirin

```bash
Ai__Provider=local
Ai__Local__BaseUrl=http://localhost:11434      # llama-server: http://localhost:8080
Ai__Local__Model=qwen2.5:7b-instruct           # llama-server: --alias değeri
Ai__TimeoutSeconds=1800                        # yerel üretim yavaş olabilir
Ai__MaxRepairs=1                               # her onarım bir üretim daha demektir
Jobs__Concurrency=1                            # tek GPU/CPU: işleri sıraya koy
# Qwen3 için:  Ai__Local__DisableThinking=true
```

`Ai__Local__Model` boşsa uygulama **başlamaz** (yanlış yapılandırma sessizce başka sağlayıcıya düşmez).

## Beklentiler

| Konu | Not |
|---|---|
| Model boyutu | 7B sınıfı **ilk deneme için asgari**; küçük modellerin tutarlı, referansı geçerli bir mimari taslak üretmesi zordur. 14B ve üzeri daha gerçekçi bir hedeftir. *Ölçülmedi — kendi örneklerinizle karar verin.* |
| Süre | Çıktı token sayısı ÷ token/sn. CPU'da dakikalar–onlarca dakika olabilir. |
| Başarısızlık biçimi | `validate` aşamasında “onarım hakkı doldu” + sorun listesi. Model zayıfsa büyük model ya da `Ai__MaxRepairs=2` deneyin. |
| İstem boyutu | Şimdilik mevcut modelin tamamı istemde gider. Çok büyük projelerde bağlam yetmeyebilir (özetleme henüz yok). |
| Heuristic ile fark | `heuristic` AI değildir; `local` gerçek bir model çağırır ve değişiklik talebi için tasarım değişikliği önerebilir. |

## Sorun giderme

| Belirti | Neden / çözüm |
|---|---|
| “Yerel model sunucusuna ulaşılamadı” | Sunucu kapalı veya `BaseUrl` yanlış. İş geçici hata sayılır ve üstel beklemeyle yeniden denenir. |
| “Model çıktısı token sınırında kesildi” | Bağlam/çıktı sınırı küçük: `OLLAMA_CONTEXT_LENGTH`, `-c`, `Ai__Local__MaxOutputTokens` artırın. |
| 400 / “context length exceeded” | İstem bağlama sığmıyor: bağlamı büyütün. |
| 404 “model not found” | `Ai__Local__Model` sunucudaki adla aynı olmalı. |
| Çıktı şemaya uymuyor | Sunucunuz `json_schema` desteklemiyor olabilir: güncelleyin (`check-local-llm.sh` bunu gösterir). |
| Zaman aşımı (`timeout` aşaması) | `Ai__TimeoutSeconds` artırın veya daha hızlı/küçük model seçin. |

## Değerlendirme önerisi (Faz 6)

Örnek proje ve kendi 2–3 projenizle, her biri için: kabul edilebilir taslak oranı, kritik eksiklerin yakalanması, gereksiz bulgu yükü, süre.
Sonuçlara göre model boyutu ve gerekirse görev başına daha küçük üretim adımları (önce bileşenler, sonra akışlar) kararlaştırılır.
Başarı eşikleri pilot öncesi belirlenir (§20.3); bu depoda ölçülmemiş doğruluk iddiası yoktur.
