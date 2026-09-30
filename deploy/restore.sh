#!/usr/bin/env bash
set -Eeuo pipefail

printf '%s\n' 'WARNING: Restoring overwrites current data in the numera database.' \
    'Stop the API and Worker (and scheduled backups) before restoring.'
if [[ $# != 2 || ${2:-} != --yes ]]; then
    printf 'Usage: bash deploy/restore.sh <dump-file> --yes\n' >&2
    exit 1
fi
if [[ ! -f $1 || ! -r $1 || ! -s $1 ]]; then
    printf 'ERROR: Dump must be a readable, nonempty file: %s\n' "$1" >&2
    exit 1
fi

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
dump_file=$(cd -- "$(dirname -- "$1")" && printf '%s/%s' "$PWD" "$(basename -- "$1")")

# Run on the Compose network with PostgreSQL 18 tools and DB_PASSWORD from .env.
# --no-deps prevents this command from starting any services automatically.
docker compose --project-directory "$repo_dir" -f "$repo_dir/docker-compose.prod.yml" \
    run --rm --no-deps -T --volume "$dump_file:/restore.dump:ro" \
    --entrypoint /bin/bash backup -c '
        set -Eeuo pipefail
        export PGPASSWORD="${DB_PASSWORD:?DB_PASSWORD must be set}"
        exec pg_restore --host=db --username=numera --dbname=numera \
            --clean --if-exists --exit-on-error --single-transaction /restore.dump
    '
printf '%s\n' 'Restore complete. You can now start the API, Worker, and backup service.'
