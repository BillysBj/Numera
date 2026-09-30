# Numera single-VM deployment

This package runs the .NET 10 BFF, worker, React SPA, PostgreSQL 18, Keycloak 26.2,
and Caddy on one Oracle Cloud Always-Free Ampere (ARM64) VM. Caddy is the only
public container and provides automatic TLS for the single origin.

## 1. Create and prepare the VM

Create an Oracle Cloud Always-Free Ampere A1 VM with Ubuntu 22.04. Give it a
reserved public IPv4 address if possible. In the subnet VCN security list (or its
network security group), allow inbound TCP 80 and 443. Also allow both ports in the
guest firewall; Oracle images may have iptables rules in addition to UFW:

```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
sudo iptables -I INPUT -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT -p tcp --dport 443 -j ACCEPT
```

Persist any direct iptables changes using the firewall mechanism chosen for the VM
(for example `iptables-persistent`). Do not open PostgreSQL, Keycloak, or API ports.

Install Docker Engine and the Compose plugin from Docker's official Ubuntu
repository, then verify them:

```bash
docker --version
docker compose version
```

## 2. Point DNS at the VM

Create an A record such as `numera.example.com` pointing to the VM's public IPv4
address and wait until it resolves publicly. A normal domain works with Let's
Encrypt. For a quick test, a wildcard DNS name such as `<PUBLIC_IP>.sslip.io` or
`<PUBLIC_IP>.nip.io` can be used as `DOMAIN`; Caddy still needs that name to resolve
publicly before it can obtain a certificate.

## 3. Configure Numera

```bash
git clone <NUMERA_REPOSITORY_URL> Numera
cd Numera
cp .env.example .env
nano .env
```

Fill the four required values: `DOMAIN`, `DB_PASSWORD`,
`KEYCLOAK_ADMIN_PASSWORD`, and `KEYCLOAK_CLIENT_SECRET`. The last value must
exactly match the `numera-bff` client's `secret` in
`keycloak/realm-numera.json` (the repository default is development-only; replace
both with the same strong secret before first start). The realm import expands its
`${NUMERA_DOMAIN}` production callback and origin entries from `DOMAIN`; keep
`DOMAIN` free of `https://` and paths.

Leave Stripe and finAPI values blank for the built-in no-network stubs, or use
Stripe TEST credentials and finAPI sandbox credentials. Protect `.env`:

```bash
chmod 600 .env
```

## 4. Build and start

```bash
docker compose -f docker-compose.prod.yml up -d --build
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs -f migrate keycloak api caddy
```

On a new database, PostgreSQL's init hook creates the separate `keycloak` database.
The one-shot `migrate` service then creates/updates the least-privilege roles,
temporarily grants `numera_migrator` `BYPASSRLS`, applies EF migrations as that
role, and restores `NOBYPASSRLS`. It exits successfully and remains stopped. On its
first start Keycloak imports `realm-numera.json`; later starts skip the already
existing realm because Keycloak persists it in PostgreSQL.

Open `https://<DOMAIN>` after DNS and certificate issuance complete. Useful checks:

```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs --tail=200 migrate keycloak api caddy
curl -I "https://<DOMAIN>/"
curl -I "https://<DOMAIN>/auth/realms/numera/.well-known/openid-configuration"
```

For an initial trial, Stripe TEST mode and the finAPI sandbox are appropriate.
OCR/Azure Document Intelligence, ClamAV, outbound e-mail, and the KoSIT validator
may remain unconfigured/off; the repository's safe stubs or no-op adapters cover
those optional integrations. Back up the `db_data`, `keys`, and
`caddy_data` Docker volumes before upgrades.

## Backups & Restore

The `backup` service waits for PostgreSQL, takes a dump immediately on startup,
then repeats every `BACKUP_INTERVAL_HOURS` (default `24`). PostgreSQL 18 `pg_dump -Fc`
produces compressed custom-format dumps of the `numera` database in the host's
`./backups/numera-YYYYMMDD-HHMMSS.dump` directory (UTC timestamps). Files are private
to their owner; Docker may create them as root, so use `sudo` when needed to read or
copy them. Watch successful backups, failures, and each prune with:

```bash
docker compose -f docker-compose.prod.yml logs -f backup
```

After each successful dump, retention sorts completed dumps by modification time
and keeps the newest dump from each of the last `BACKUP_KEEP_DAILY` distinct UTC
days (default `14`), plus the newest dump from each of the last
`BACKUP_KEEP_WEEKLY` distinct ISO weeks (default `8`, Monday through Sunday).
The two sets overlap, so defaults keep at most 22 dumps. These are days/weeks with
available dumps, not strict age limits; older recovery points survive gaps in
backups. A retention tier may be `0`, but not both. Failed dumps do not prune
existing backups; only a successful dump is renamed from `.partial` to `.dump`.
Interrupted containers may leave `.partial` files, which are not restore points
and can be removed once the backup service is stopped.

**Copy `./backups` offsite automatically, for example with rsync or rclone to
another server or object storage. A backup on the same VM/disk does not survive
VM loss, disk failure, or deletion.** Copy only completed `*.dump` files, preserve
modification times, protect access to production data, and set independent offsite
retention/versioning so local pruning does not erase all remote recovery points.
Monitor backup logs and the age of the newest offsite dump.

These dumps provide disaster recovery, separate from the in-app GoBD 10-year
retention through the immutable ledger and WORM archive. A `numera` dump does not
include the separate `keycloak` database, cluster roles, the `keys` volume, external
document/archive storage, or `.env`; protect those separately for complete recovery.

Run restores from the VM checkout using Bash and Docker Compose. The script mounts
the selected dump read-only into a temporary PostgreSQL 18 container on the Compose
network, obtains `DB_PASSWORD` from the Compose environment, and connects to `db`
as `numera`. It requires `--yes` and runs `pg_restore --clean --if-exists` in one
transaction, stopping on errors. **This overwrites current data. Stop the API and
Worker throughout the restore.** Also stop scheduled backups to avoid capturing
the database being recovered. Keep PostgreSQL running and select a completed dump:

```bash
docker compose -f docker-compose.prod.yml stop api worker backup
bash deploy/restore.sh ./backups/numera-20260930-020000.dump --yes
# Only after the restore succeeds:
docker compose -f docker-compose.prod.yml start api worker backup
```

On a replacement VM, restore the checkout/configuration and install Docker first.
Start `db` and provision the roles using the deployment migration service before
restoring (`pg_dump` does not include roles):

```bash
docker compose -f docker-compose.prod.yml up -d db
docker compose -f docker-compose.prod.yml run --rm migrate
bash deploy/restore.sh /path/to/offsite/numera-20260930-020000.dump --yes
# Only after restoring successfully and recovering the other required data:
docker compose -f docker-compose.prod.yml up -d --build
```

Test a restore periodically in an isolated environment, verify application data
and tenant access, and record how long recovery takes. A successful dump alone
does not prove that recovery works.
