#!/usr/bin/env bash
# macOS/Linux: стартира MySQL, API и уеб интерфейса; Ctrl+C спира всичко.
cd "$(dirname "$0")"
command -v docker >/dev/null && command -v dotnet >/dev/null && command -v npm >/dev/null \
  || { echo "Нужни са docker, dotnet (9 SDK) и npm."; exit 1; }

echo "> MySQL (Docker, порт 3307)"
docker compose up -d db || exit 1
until docker compose exec -T db mysqladmin ping -h localhost -proot --silent >/dev/null 2>&1; do sleep 2; done

[ -d frontend/node_modules ] || (cd frontend && npm install --no-audit --no-fund) || exit 1

trap 'trap - INT TERM; echo; echo "> Спиране…"; docker compose stop db >/dev/null; kill 0' INT TERM
(cd backend/Porachki.Api && dotnet run) &
(cd frontend && npm run dev) &

echo "> Изчакване на API-то (първия път компилира проекта)…"
until curl -sf http://localhost:5080/health >/dev/null; do sleep 3; done
(open http://localhost:5173 2>/dev/null || xdg-open http://localhost:5173 2>/dev/null) &
echo "Готово: http://localhost:5173  (Ctrl+C за спиране)"
wait
