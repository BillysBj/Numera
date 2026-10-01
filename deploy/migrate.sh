#!/usr/bin/env bash
set -Eeuo pipefail

: "${DB_PASSWORD:?DB_PASSWORD must be set}"

readonly repo_dir=/workspace
readonly build_dir=/tmp/numera-migrate
readonly superuser_connection=(--host=db --port=5432 --username=numera --dbname=numera)

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install --yes --no-install-recommends postgresql-client
rm -rf /var/lib/apt/lists/*

export PGPASSWORD="$DB_PASSWORD"

echo "Applying database roles and default privileges as the PostgreSQL superuser..."
psql "${superuser_connection[@]}" --set=ON_ERROR_STOP=1 --file="$repo_dir/scripts/db-roles.sql"

# db-roles.sql deliberately carries local-development placeholders. Production uses
# the deployment secret for both login roles; psql's :'role_password' syntax quotes
# the value as a SQL literal instead of interpolating it into executable SQL.
psql "${superuser_connection[@]}" \
    --set=ON_ERROR_STOP=1 \
    --set=role_password="$DB_PASSWORD" <<'SQL'
ALTER ROLE numera_migrator PASSWORD :'role_password';
ALTER ROLE numera_app PASSWORD :'role_password';
SQL

restore_migrator_rls() {
    echo "Restoring NOBYPASSRLS on numera_migrator..."
    psql "${superuser_connection[@]}" \
        --set=ON_ERROR_STOP=1 \
        --command='ALTER ROLE numera_migrator NOBYPASSRLS;'
}

echo "Temporarily granting BYPASSRLS to numera_migrator for EF DDL..."
psql "${superuser_connection[@]}" \
    --set=ON_ERROR_STOP=1 \
    --command='ALTER ROLE numera_migrator BYPASSRLS;'
trap restore_migrator_rls EXIT

# Build away from the read-only bind mount so EF's obj/bin output never changes the
# VM checkout. Migrations use Api as the startup project so every module
# contributing to the shared model is loaded.
rm -rf "$build_dir"
mkdir -p "$build_dir"
cp "$repo_dir/Directory.Build.props" "$repo_dir/global.json" "$build_dir/"
cp -R "$repo_dir/src" "$build_dir/src"
cd "$build_dir"

dotnet tool install --global dotnet-ef --version '10.0.*'
export PATH="$PATH:/root/.dotnet/tools"

# `dotnet ef` does not restore implicitly, and this is a fresh copy with no obj/.
# Restore the startup project (pulls every referenced module, incl. Platform.Db).
echo "Restoring NuGet packages..."
dotnet restore src/Numera.Api/Numera.Api.csproj
dotnet restore src/platform/Numera.Platform.Db/Numera.Platform.Db.csproj

echo "Applying EF migrations as numera_migrator..."
dotnet ef database update \
    --project src/platform/Numera.Platform.Db \
    --startup-project src/Numera.Api \
    --configuration Release \
    --connection "Host=db;Port=5432;Database=numera;Username=numera_migrator;Password=${DB_PASSWORD}"

restore_migrator_rls
trap - EXIT
echo "Database migration completed with numera_migrator back on NOBYPASSRLS."

