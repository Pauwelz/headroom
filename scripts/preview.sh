#!/usr/bin/env bash
# Runs Headroom locally with fresh demo data and a mock OpenID Connect provider that stands in for Entra ID.
# Stop it with Ctrl+C. The containers and their data are then removed.
set -euo pipefail

# Use the local Docker daemon, never the "nas" context: test containers crashed the NAS.
export DOCKER_HOST=unix:///var/run/docker.sock

db=headroom-preview-db
oidc=headroom-preview-oidc
tenant=11111111-1111-1111-1111-111111111111
password='Dev_Passw0rd!' # the same as in src/Headroom/appsettings.Development.json

for port in 5432 5002 8080; do
  if ss -ltn | grep -q ":$port "; then
    echo "Port $port is in use. Stop what uses it, then run this script again." >&2
    exit 1
  fi
done

cleanup() { docker rm -f "$db" "$oidc" >/dev/null 2>&1 || true; }
trap cleanup EXIT

docker run -d --name "$db" -e "POSTGRES_PASSWORD=$password" -p 5432:5432 postgres:17 >/dev/null
# The login form of this provider accepts any claims, so a developer can log in as anyone.
docker run -d --name "$oidc" -p 8080:8080 -e 'JSON_CONFIG={"interactiveLogin":true}' ghcr.io/navikt/mock-oauth2-server:2.1.10 >/dev/null

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

echo "App: http://localhost:5002"
echo "Log in: select Log in with Microsoft. At the login form of the mock provider, enter any user name and these claims:"
echo "  {\"tid\":\"$tenant\",\"oid\":\"1\",\"preferred_username\":\"planner@example.test\",\"name\":\"Pat Planner\"}"
echo "The tid must be $tenant. Use oid and preferred_username as the identity: planner@example.test or person@example.test."

cd "$(dirname "$0")/.."
# The mock provider has no real tenant. EntraId__Authority points the login at it, and a client secret replaces the managed identity.
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5002 BaseUri=http://localhost:5002 \
  EntraId__Authority=http://localhost:8080/default EntraId__TenantId="$tenant" EntraId__ClientId=headroom EntraId__ClientSecret=preview \
  dotnet run --project src/Headroom --no-launch-profile -- --seed
