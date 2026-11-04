#!/usr/bin/env bash
# Yerel geliştirme: PostgreSQL (Docker varsa) + API (+ worker, API içinde) + web arayüzü tek komutla.
#   scripts/dev.sh                 -> Docker ile PostgreSQL, API :5217, web :5173
#   ARCVORA_SKIP_DB=1 scripts/dev.sh   -> PostgreSQL'i kendiniz yönetiyorsanız (Docker kullanmaz)
# Ctrl+C ile hepsi kapanır. Windows'ta Git Bash veya WSL ile çalışır; PowerShell için README'deki adımları izleyin.
set -euo pipefail
cd "$(dirname "$0")/.."

need() { command -v "$1" >/dev/null 2>&1 || { echo "✗ '$1' bulunamadı. $2"; exit 1; }; }
need dotnet ".NET 8 SDK kurun: https://dotnet.microsoft.com/download"
need node   "Node.js 20 veya üzerini kurun: https://nodejs.org"
need npm    "Node.js ile birlikte gelir."

API_URL="${ARCVORA_API_URL:-http://localhost:5217}"
PG_HOST="${ARCVORA_PG_HOST:-localhost}"
PG_PORT="${ARCVORA_PG_PORT:-5432}"

# 1) PostgreSQL
if [ -z "${ARCVORA_SKIP_DB:-}" ]; then
  if command -v docker >/dev/null 2>&1; then
    echo "→ PostgreSQL başlatılıyor (docker compose)…"
    docker compose -f docker-compose.dev.yml up -d
  else
    echo "! Docker bulunamadı. PostgreSQL 16'yı kendiniz başlatın (kullanıcı/parola: postgres/postgres) ve ARCVORA_SKIP_DB=1 ile yeniden çalıştırın."
    exit 1
  fi
fi
echo -n "→ PostgreSQL bekleniyor ($PG_HOST:$PG_PORT) "
for i in $(seq 1 40); do
  if (exec 3<>"/dev/tcp/$PG_HOST/$PG_PORT") 2>/dev/null; then echo "✓"; break; fi
  echo -n "."; sleep 1
  [ "$i" = 40 ] && { echo; echo "✗ PostgreSQL'e ulaşılamadı."; exit 1; }
done

# 2) web bağımlılıkları
if [ ! -d web/node_modules ]; then echo "→ npm ci (ilk kurulum, birkaç dakika sürebilir)…"; (cd web && npm ci); fi

# 3) API + web; çıkışta ikisini de kapat
trap 'echo; echo "Kapatılıyor…"; kill 0' EXIT INT TERM
(cd src/Arcvora.Api && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$API_URL" dotnet run --no-launch-profile) &
(cd web && VITE_API_TARGET="$API_URL" npm run dev -- --host 127.0.0.1) &

echo
echo "  Arayüz : http://localhost:5173   (kayıt olun, 'Örnek projeyi incele')"
echo "  API    : $API_URL/health"
echo "  Durdurmak için Ctrl+C"
wait -n
