#!/usr/bin/env bash
# Yerel (OpenAI uyumlu) model sunucunuzu ARCVORA için hızlıca denetler:
#  1) sunucuya ulaşılıyor mu  2) response_format=json_schema (şema kısıtlı çıktı) çalışıyor mu  3) yaklaşık token/sn.
# Kullanım: scripts/check-local-llm.sh [BASE_URL] [MODEL]
#   Ollama:      scripts/check-local-llm.sh http://localhost:11434 qwen2.5:7b-instruct
#   llama.cpp:   scripts/check-local-llm.sh http://localhost:8080  qwen2.5-7b-instruct   (llama-server --alias değeri)
set -euo pipefail
BASE="${1:-http://localhost:11434}"
MODEL="${2:?Model adı gerekli (ör. qwen2.5:7b-instruct)}"

BODY=$(cat <<JSON
{"model":"$MODEL","stream":false,"temperature":0.2,"max_tokens":200,
 "messages":[{"role":"system","content":"Yalnızca JSON döndür."},
             {"role":"user","content":"Bir mimari bileşeni tanımla: adı 'Bildirim servisi', türü module."}],
 "response_format":{"type":"json_schema","json_schema":{"name":"cmp","strict":true,"schema":{
   "type":"object","additionalProperties":false,"required":["name","kind"],
   "properties":{"name":{"type":"string"},"kind":{"type":"string","enum":["client","api","module","worker","database","external_service","queue","storage"]}}}}}}
JSON
)

echo "→ POST $BASE/v1/chat/completions  (model: $MODEL)"
OUT=$(mktemp); trap 'rm -f "$OUT"' EXIT
if ! TIME=$(curl -sS -m 600 -o "$OUT" -w '%{http_code} %{time_total}' -H 'content-type: application/json' -d "$BODY" "$BASE/v1/chat/completions"); then
  echo "✗ Sunucuya ulaşılamadı. Sunucu çalışıyor mu? (Ollama: 'ollama serve', llama.cpp: 'llama-server')"; exit 1
fi
CODE="${TIME%% *}"; SECS="${TIME##* }"
if [ "$CODE" != "200" ]; then echo "✗ HTTP $CODE"; cat "$OUT"; echo; exit 1; fi

echo "Yanıt:"; cat "$OUT"; echo
# content alanı JSON içinde kaçışlı gelir (\"kind\"); kaçışlar kaldırılıp aranır
if tr -d '\\' < "$OUT" | grep -q '"kind"[[:space:]]*:[[:space:]]*"module"'; then
  echo "✓ Şema kısıtlı çıktı çalışıyor (enum değeri 'module' bekleniyordu)."
else
  echo "✗ Çıktı şemaya uymuyor: sunucunuz response_format=json_schema desteklemiyor olabilir (güncelleyin)."
fi
TOK=$(grep -o '"completion_tokens"[[:space:]]*:[[:space:]]*[0-9]*' "$OUT" | grep -o '[0-9]*$' | head -1 || true)
if [ -n "${TOK:-}" ] && [ "$TOK" -gt 0 ]; then
  awk -v t="$TOK" -v s="$SECS" 'BEGIN { printf "≈ %.1f token/sn (%d token, %.1f sn; ilk istek model yükleme süresini içerebilir)\n", t/s, t, s }'
  echo "  Bir ARCVORA taslağı genelde birkaç bin çıktı token'ıdır: süre ≈ token / (token/sn)."
fi
