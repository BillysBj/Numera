#!/usr/bin/env bash
set -Eeuo pipefail

export TZ=UTC
export LC_ALL=C
backup_dir=/backups
partial_file=

log() { printf '[%s] %s\n' "$(date -u +%FT%TZ)" "$*"; }

prune_backups() {
    local record modified file day week keep
    local daily_count=0 weekly_count=0
    local -A days=() weeks=()

    # Newest mtime first; NUL delimiters also handle spaces in file paths.
    while IFS= read -r -d '' record; do
        modified=${record%% *}
        file=${record#* }
        day=$(date -u -d "@${modified%.*}" +%F)
        week=$(date -u -d "@${modified%.*}" +%G-%V)
        keep=false
        if (( daily_count < BACKUP_KEEP_DAILY )) && [[ ! ${days[$day]+seen} ]]; then
            days[$day]=1
            daily_count=$((daily_count + 1))
            keep=true
        fi
        if (( weekly_count < BACKUP_KEEP_WEEKLY )) && [[ ! ${weeks[$week]+seen} ]]; then
            weeks[$week]=1
            weekly_count=$((weekly_count + 1))
            keep=true
        fi
        if [[ $keep == false ]]; then
            rm -- "$file"
            log "Pruned $file"
        fi
    done < <(find "$backup_dir" -maxdepth 1 -type f -name 'numera-*.dump' \
        -printf '%T@ %p\0' | sort -z -nr)
}

backup_once() {
    local dump="$backup_dir/numera-$(date -u +%Y%m%d-%H%M%S).dump"
    partial_file="${dump}.partial"
    log "Starting backup: $dump"
    if pg_dump --host=db --username=numera --dbname=numera -Fc --file="$partial_file"; then
        # Publish only complete dumps; failed dumps never trigger retention pruning.
        mv -- "$partial_file" "$dump"
        partial_file=
        log "Backup complete: $dump"
        prune_backups
    else
        log "ERROR: Backup failed; keeping existing dumps and retrying next interval"
        rm -f -- "$partial_file"
        partial_file=
    fi
}

cleanup() {
    if [[ -n $partial_file ]]; then rm -f -- "$partial_file"; fi
}

main() {
    : "${DB_PASSWORD:?DB_PASSWORD must be set}"
    BACKUP_INTERVAL_HOURS=${BACKUP_INTERVAL_HOURS:-24}
    BACKUP_KEEP_DAILY=${BACKUP_KEEP_DAILY:-14}
    BACKUP_KEEP_WEEKLY=${BACKUP_KEEP_WEEKLY:-8}
    if [[ ! $BACKUP_INTERVAL_HOURS =~ ^[1-9][0-9]*$ ]] ||
       [[ ! $BACKUP_KEEP_DAILY =~ ^(0|[1-9][0-9]*)$ ]] ||
       [[ ! $BACKUP_KEEP_WEEKLY =~ ^(0|[1-9][0-9]*)$ ]] ||
       (( BACKUP_KEEP_DAILY == 0 && BACKUP_KEEP_WEEKLY == 0 )); then
        log "ERROR: Interval must be a positive integer; retention must be nonnegative integers with at least one nonzero"
        exit 1
    fi
    export PGPASSWORD="$DB_PASSWORD"
    umask 077
    mkdir -p "$backup_dir"
    trap cleanup EXIT
    trap 'exit 0' INT TERM
    log "Waiting for db to accept connections"
    until pg_isready --host=db --username=numera --dbname=numera; do
        sleep 5 & wait "$!"
    done
    while true; do
        backup_once
        sleep "$((BACKUP_INTERVAL_HOURS * 3600))" & wait "$!"
    done
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then main "$@"; fi
