#!/usr/bin/env bash
# Runs Headroom locally with fresh demo data and Mailpit for the login emails.
# Stop it with Ctrl+C. The containers and their data are then removed.
set -euo pipefail

# Use the local Docker daemon, never the "nas" context: test containers crashed the NAS.
export DOCKER_HOST=unix:///var/run/docker.sock

db=headroom-preview-db
mail=headroom-preview-mail
password='Dev_Passw0rd!' # the same as in src/Headroom/appsettings.Development.json

for port in 5432 1025 8025 5002; do
  if ss -ltn | grep -q ":$port "; then
    echo "Port $port is in use. Stop what uses it, then run this script again." >&2
    exit 1
  fi
done

cleanup() { docker rm -f "$db" "$mail" >/dev/null 2>&1 || true; }
trap cleanup EXIT

docker run -d --name "$db" -e "POSTGRES_PASSWORD=$password" -p 5432:5432 postgres:17 >/dev/null
docker run -d --name "$mail" -p 1025:1025 -p 8025:8025 axllent/mailpit:v1.27 >/dev/null

echo "Waiting for PostgreSQL..."
ready=false
for _ in $(seq 60); do
  docker exec "$db" pg_isready -U postgres >/dev/null 2>&1 && ready=true && break
  sleep 2
done
if [ "$ready" = false ]; then
  docker logs --tail 20 "$db" >&2
  echo "PostgreSQL did not start within 2 minutes." >&2
  exit 1
fi

echo "App:     http://localhost:5002"
echo "Mailpit: http://localhost:8025"
echo "Log in as planner@example.test or person@example.test. The codes arrive in Mailpit."

cd "$(dirname "$0")/.."
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5002 Smtp__Host=localhost \
  dotnet run --project src/Headroom --no-launch-profile -- --seed
