#!/usr/bin/env bash
# Uçtan uca testleri tek komutla çalıştırır: geçici veritabanı + API (+ web) (+ sahte yerel model) açar, testleri koşar, temizler.
#   scripts/e2e.sh              -> buluşsal üretici: API yolculuğu + tarayıcı yolculuğu
#   scripts/e2e.sh --local      -> sahte yerel model (Ai__Provider=local): API yolculuğu, istek biçimi, onarım turu, tarayıcı
#   scripts/e2e.sh --prod       -> ÜRETİM yapılandırması: yayınlanmış uygulama + wwwroot (tek süreç, aynı origin) + DATABASE_URL (postgres://)
#   scripts/e2e.sh --no-ui      -> tarayıcı testini atlar (Playwright gerekmez)
# Gerekir: .NET 8 SDK, Node 20+, çalışan PostgreSQL (docker compose -f docker-compose.dev.yml up -d). Tarayıcı testi için:
#   cd web && npm i -D playwright && npx playwright install chromium
# Ortam: PGHOST/PGUSER/PGPASSWORD (varsayılan localhost/postgres/postgres), E2E_API_PORT, E2E_WEB_PORT, E2E_MOCK_PORT, E2E_JOB_TIMEOUT_MS.
set -uo pipefail
cd "$(dirname "$0")/.."

MODE=heuristic; UI=1
for a in "$@"; do
  case "$a" in
    --local) MODE=local ;;
    --prod) MODE=prod ;;
    --no-ui) UI=0 ;;
    -h|--help) sed -n 2,9p "$0"; exit 0 ;;
    *) echo "Bilinmeyen seçenek: $a (--local, --prod, --no-ui, --help)"; exit 2 ;;
  esac
done

need() { command -v "$1" >/dev/null 2>&1 || { echo "✗ '$1' bulunamadı. $2"; exit 2; }; }
need dotnet ".NET 8 SDK kurun."
need node "Node.js 20+ kurun."
need curl "curl gerekli."

export PGHOST="${PGHOST:-localhost}" PGUSER="${PGUSER:-postgres}" PGPASSWORD="${PGPASSWORD:-postgres}"
DB="arcvora_e2e_$$"
API_PORT="${E2E_API_PORT:-5290}"; WEB_PORT="${E2E_WEB_PORT:-5291}"; MOCK_PORT="${E2E_MOCK_PORT:-11599}"
MODEL="qwen-mock"
LOGS="$(mktemp -d)"
PIDS=()

# Alt süreçleri de kapat (npx/dotnet bir çocuk süreç başlatabilir); aksi halde sonraki koşu eski sunucuya bağlanır.
stop_pid() { pkill -P "$1" 2>/dev/null || true; kill "$1" 2>/dev/null || true; wait "$1" 2>/dev/null || true; }
cleanup() {
  for p in "${PIDS[@]:-}"; do [ -n "$p" ] && stop_pid "$p"; done
  if command -v psql >/dev/null 2>&1; then
    for d in "$DB" "${DB}_cancel" "${DB}_timeout" "${DB}_ratelimit" "${DB}_forwarded"; do psql -Atc "drop database if exists \"$d\" with (force)" >/dev/null 2>&1 || true; done
  fi
  echo; echo "Kayıtlar: $LOGS"
}
trap cleanup EXIT

port_free() { # port, ad: bu portta eski bir sunucu varsa yanlış sunucuyu sınamamak için dur
  if curl -s -m 1 -o /dev/null "http://127.0.0.1:$1/"; then echo "✗ $2 portu ($1) dolu: eski bir süreç çalışıyor olabilir. Kapatın veya E2E_*_PORT ile başka port verin."; return 1; fi
}
wait_http() { # url, ad, deneme
  for _ in $(seq 1 "${3:-90}"); do curl -fs -m 2 -o /dev/null "$1" && return 0; sleep 1; done
  echo "✗ $2 açılmadı ($1). Kayıt: $LOGS"; return 1
}

echo "→ PostgreSQL denetleniyor ($PGHOST)…"
if command -v psql >/dev/null 2>&1; then
  psql -Atc "select 1" >/dev/null 2>&1 || { echo "✗ PostgreSQL'e bağlanılamadı (PGHOST/PGUSER/PGPASSWORD). 'docker compose -f docker-compose.dev.yml up -d' ile başlatın."; exit 2; }
else
  (exec 3<>"/dev/tcp/$PGHOST/5432") 2>/dev/null || { echo "✗ PostgreSQL'e ulaşılamadı ($PGHOST:5432)."; exit 2; }
fi

port_free "$API_PORT" "API" || exit 2
[ "$UI" = 1 ] && [ "$MODE" != prod ] && { port_free "$WEB_PORT" "web" || exit 2; }
[ "$MODE" = local ] && { port_free "$MOCK_PORT" "sahte model" || exit 2; }

echo "→ API derleniyor…"
dotnet build src/Arcvora.Api --nologo -v q -c Debug >"$LOGS/build.log" 2>&1 || { echo "✗ Derleme başarısız:"; tail -20 "$LOGS/build.log"; exit 1; }
if [ "$MODE" = prod ]; then
  # Docker imajının yapacağının aynısı: arayüzü derle, API'yi yayınla, derlenmiş arayüzü wwwroot'a koy.
  [ -d web/node_modules ] || { echo "→ npm ci (ilk kurulum)…"; (cd web && npm ci >"$LOGS/npm.log" 2>&1) || { tail -20 "$LOGS/npm.log"; exit 1; }; }
  echo "→ Arayüz derleniyor ve API yayınlanıyor (Release)…"
  (cd web && npm run build >"$LOGS/web-build.log" 2>&1) || { echo "✗ Web derlemesi başarısız:"; tail -20 "$LOGS/web-build.log"; exit 1; }
  dotnet publish src/Arcvora.Api -c Release -o "$LOGS/pub" --nologo -v q >"$LOGS/publish.log" 2>&1 || { echo "✗ Yayınlama başarısız:"; tail -20 "$LOGS/publish.log"; exit 1; }
  cp -r web/dist "$LOGS/pub/wwwroot"
fi

start_mock() { # fault ("" veya repair)
  MOCK_LLM_PORT="$MOCK_PORT" MOCK_LLM_MODEL="$MODEL" MOCK_LLM_FAULT="${1:-}" node scripts/e2e/mock-llm.mjs >"$LOGS/mock-llm.log" 2>&1 &
  MOCK_PID=$!
  wait_http "http://127.0.0.1:$MOCK_PORT/v1/models" "sahte model" 20
}

API_ENV=(ASPNETCORE_ENVIRONMENT=Development "ASPNETCORE_URLS=http://127.0.0.1:$API_PORT" RateLimit__AuthPerMinute=1000
  "ConnectionStrings__Arcvora=Host=$PGHOST;Database=$DB;Username=$PGUSER;Password=$PGPASSWORD")
if [ "$MODE" = local ]; then
  start_mock "" || exit 1; PIDS+=("$MOCK_PID")
  API_ENV+=(Ai__Provider=local "Ai__Local__BaseUrl=http://127.0.0.1:$MOCK_PORT" "Ai__Local__Model=$MODEL" Ai__TimeoutSeconds=120)
fi

echo "→ API başlatılıyor (mod: $MODE, veritabanı: $DB)…"
if [ "$MODE" = prod ]; then
  # Üretim: Production ortamı, DATABASE_URL (postgres:// adresi), uygulama içi worker, proxy başlıkları; geliştirme ayarı yok.
  ( cd "$LOGS/pub" && exec env ASPNETCORE_ENVIRONMENT=Production "ASPNETCORE_URLS=http://127.0.0.1:$API_PORT" \
      Jwt__Key="e2e-uretim-anahtari-en-az-32-karakter-0123456789abcdef" \
      "DATABASE_URL=postgres://$PGUSER:$PGPASSWORD@$PGHOST:5432/$DB?sslmode=disable" \
      Database__MigrateOnStartup=true Worker__InProcess=true ForwardedHeaders__Enabled=true RateLimit__AuthPerMinute=1000 \
      dotnet Arcvora.Api.dll >"$LOGS/api.log" 2>&1 ) &
else
  ( cd src/Arcvora.Api && exec env "${API_ENV[@]}" dotnet bin/Debug/net8.0/Arcvora.Api.dll >"$LOGS/api.log" 2>&1 ) &
fi
PIDS+=("$!")
wait_http "http://127.0.0.1:$API_PORT/health" "API" 120 || { tail -20 "$LOGS/api.log"; exit 1; }

if [ "$UI" = 1 ] && [ "$MODE" = prod ]; then
  WEB_PORT="$API_PORT"   # üretimde arayüz API ile aynı süreçten, aynı origin'den sunulur
elif [ "$UI" = 1 ]; then
  [ -d web/node_modules ] || { echo "→ npm ci (ilk kurulum)…"; (cd web && npm ci >"$LOGS/npm.log" 2>&1) || { tail -20 "$LOGS/npm.log"; exit 1; }; }
  echo "→ Web başlatılıyor…"
  ( cd web && VITE_API_TARGET="http://127.0.0.1:$API_PORT" exec node node_modules/vite/bin/vite.js --port "$WEB_PORT" --host 127.0.0.1 --strictPort >"$LOGS/web.log" 2>&1 ) &
  PIDS+=("$!")
  wait_http "http://127.0.0.1:$WEB_PORT" "web" 60 || { tail -20 "$LOGS/web.log"; exit 1; }
fi

FAIL=0
export ARCVORA_API_URL="http://127.0.0.1:$API_PORT" ARCVORA_WEB_URL="http://127.0.0.1:$WEB_PORT" MOCK_URL="http://127.0.0.1:$MOCK_PORT" MOCK_MODEL="$MODEL"
step() { local t="$1"; shift; echo; echo "━━ $t"; "$@" || FAIL=1; }

api_extras() {
  step "Roller ve yetkiler" node scripts/e2e/api-roles.mjs
  step "Eşzamanlılık (paralel kabul, çakışan düzenleme)" node scripts/e2e/api-concurrency.mjs
  step "Yaşam döngüsü (düzenleme, yama, açık soru, denetim, silme)" node scripts/e2e/api-lifecycle.mjs
  step "Uç durumlar (kimlik, doğrulama, güvenlik, sürüm, dışa aktarma, öneri durumları)" node scripts/e2e/api-edge.mjs
  step "Akış uygulamaları (kullanıcı/rol, şablon, onay akışı, muhasebe webhook'u, CSV, yalıtım)" node scripts/e2e/apps-journey.mjs
}

if [ "$MODE" = local ]; then
  step "API yolculuğu (yerel model)" env EXPECT_PROVIDER=local node scripts/e2e/api-journey.mjs
  api_extras
  step "Yerel sağlayıcı: istek biçimi ve token kaydı" node scripts/e2e/local-llm.mjs
  stop_pid "$MOCK_PID"; start_mock repair || exit 1; PIDS+=("$MOCK_PID")
  step "Yerel sağlayıcı: bozuk taslak → onarım turu" env EXPECT_REPAIR=1 node scripts/e2e/local-llm.mjs
  # Yavaş model aşaması: ayrı API + ayrı gecikmeli sahte model + ayrı veritabanı (işçiler birbirinin işini almasın)
  slow_phase() { # ad, API portu, sahte model portu, gecikme ms, zaman aşımı sn, başlık, betik
    local name="$1" aport="$2" mport="$3" delay="$4" tmo="$5" title="$6" script="$7"
    port_free "$aport" "yavaş-faz API" && port_free "$mport" "yavaş-faz sahte model" || { FAIL=1; return; }
    MOCK_LLM_PORT="$mport" MOCK_LLM_MODEL="$MODEL" MOCK_LLM_DELAY_MS="$delay" node scripts/e2e/mock-llm.mjs >"$LOGS/mock-$name.log" 2>&1 &
    local mp=$!; PIDS+=("$mp")
    ( cd src/Arcvora.Api && exec env ASPNETCORE_ENVIRONMENT=Development "ASPNETCORE_URLS=http://127.0.0.1:$aport" RateLimit__AuthPerMinute=1000 \
        Ai__Provider=local "Ai__Local__BaseUrl=http://127.0.0.1:$mport" "Ai__Local__Model=$MODEL" "Ai__TimeoutSeconds=$tmo" \
        "ConnectionStrings__Arcvora=Host=$PGHOST;Database=${DB}_$name;Username=$PGUSER;Password=$PGPASSWORD" \
        dotnet bin/Debug/net8.0/Arcvora.Api.dll >"$LOGS/api-$name.log" 2>&1 ) &
    local ap=$!; PIDS+=("$ap")
    if wait_http "http://127.0.0.1:$aport/health" "yavaş-faz API ($name)" 120; then
      step "$title" env ARCVORA_API_URL="http://127.0.0.1:$aport" node "$script"
    else FAIL=1; tail -10 "$LOGS/api-$name.log"; fi
    stop_pid "$ap"; stop_pid "$mp"
  }
  slow_phase cancel "$((API_PORT + 10))" "$((MOCK_PORT + 1))" 4000 120 "Yavaş model: çalışan iş iptali" scripts/e2e/local-cancel.mjs
  slow_phase timeout "$((API_PORT + 11))" "$((MOCK_PORT + 2))" 6000 2 "Yavaş model: zaman aşımı" scripts/e2e/local-timeout.mjs
elif [ "$MODE" = prod ]; then
  step "API yolculuğu (üretim yapılandırması, tek süreç)" env EXPECT_PROVIDER=heuristic node scripts/e2e/api-journey.mjs
  api_extras
  step "Arayüz üretimde sunuluyor (SPA yedeği, /api JSON 404)" node scripts/e2e/prod-serving.mjs
else
  step "API yolculuğu (buluşsal üretici)" env EXPECT_PROVIDER=heuristic node scripts/e2e/api-journey.mjs
  api_extras
fi
# Hız sınırı ve proxy başlıkları: dakikada 3 kimlik isteği ile AYRI API'ler (ana API'nin sınırı diğer testleri engellemesin diye 1000'e ayarlı)
limit_phase() { # ad, port farkı, başlık, betik, ek ortam değişkenleri...
  local name="$1" aport="$((API_PORT + $2))" title="$3" script="$4"; shift 4
  port_free "$aport" "$name API" || { FAIL=1; return; }
  ( cd src/Arcvora.Api && exec env ASPNETCORE_ENVIRONMENT=Development "ASPNETCORE_URLS=http://127.0.0.1:$aport" RateLimit__AuthPerMinute=3 "$@" \
      "ConnectionStrings__Arcvora=Host=$PGHOST;Database=${DB}_$name;Username=$PGUSER;Password=$PGPASSWORD" \
      dotnet bin/Debug/net8.0/Arcvora.Api.dll >"$LOGS/api-$name.log" 2>&1 ) &
  local ap=$!; PIDS+=("$ap")
  if wait_http "http://127.0.0.1:$aport/health" "$name API" 120; then
    step "$title" env ARCVORA_API_URL="http://127.0.0.1:$aport" node "$script"
  else FAIL=1; tail -10 "$LOGS/api-$name.log"; fi
  stop_pid "$ap"
}
limit_phase ratelimit 12 "Hız sınırı (kimlik uçları, dakikada 3)" scripts/e2e/api-ratelimit.mjs
limit_phase forwarded 13 "Proxy arkasında istemci başına hız sınırı (X-Forwarded-For)" scripts/e2e/api-forwarded.mjs ForwardedHeaders__Enabled=true

[ "$UI" = 1 ] && step "Tarayıcı yolculuğu" env E2E_SHOTS_DIR="$LOGS/ekran-goruntuleri" node scripts/e2e/ui-journey.mjs
[ "$UI" = 1 ] && step "Tarayıcı uç durumları (hata yolları, oturum, XSS, klavye, ekran boyutları)" env E2E_SHOTS_DIR="$LOGS/ekran-goruntuleri" node scripts/e2e/ui-edge.mjs
[ "$UI" = 1 ] && step "Tarayıcı: akış uygulamaları (rol bazlı menü, tasarlayıcı, talep, onay, mobil)" env E2E_SHOTS_DIR="$LOGS/ekran-goruntuleri" node scripts/e2e/apps-ui.mjs
[ "$UI" = 1 ] && step "Tarayıcı: mobil (telefon ekranı, taşma) ve PWA (manifest, service worker)" env E2E_SHOTS_DIR="$LOGS/ekran-goruntuleri" E2E_PROD="$([ "$MODE" = prod ] && echo 1 || echo 0)" node scripts/e2e/apps-mobile.mjs

echo
if [ "$FAIL" = 0 ]; then echo "✓ Tüm uçtan uca testler geçti ($MODE)."; else echo "✗ Bazı uçtan uca testler başarısız ($MODE). Kayıtlar: $LOGS"; fi
exit "$FAIL"
