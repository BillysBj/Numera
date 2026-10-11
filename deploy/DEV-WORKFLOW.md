# Entwicklungs-Workflow (lokal testen → dann Prod)

Ziel: Änderungen werden **erst lokal** gebaut, getestet und durchgeklickt und erst
nach bestandenem Test auf die Produktions-VM gebracht. `master` enthält immer nur
geprüften Stand.

```
 dev  ──(lokal testen)──▶  master  ──(Deploy)──▶  Prod-VM (https://…nip.io)
```

## 1. Branch-Modell

| Branch   | Rolle                                                        |
| -------- | ------------------------------------------------------------ |
| `dev`    | Arbeitsstand. Hier wird committet und lokal getestet.        |
| `master` | Produktion. Nur per Merge aus `dev`, nachdem lokal alles ok. |

```bash
git switch dev          # hier arbeiten
# … committen …
```

## 2. Lokal starten (1-Klick)

Doppelklick auf **`Start-Numera.cmd`** im Projekt-Root. Das Skript
(`scripts/start-numera.ps1`) startet:

1. Docker-Infra: Postgres (5432), Keycloak (8080), Mailpit (8025), KoSIT (8081)
2. API (`http://localhost:5080`) + Worker + Web-App je in eigenem Fenster
3. öffnet den Browser auf **`http://localhost:5173`**

Anmeldung an Keycloak wie in Prod (du meldest dich selbst an).

Stoppen: **`Stop-Numera.cmd`** (Daten bleiben im Docker-Volume erhalten).

> Erststart / nach neuen Migrationen: Schema anlegen als Migrator-Rolle
> (siehe `scripts/db-roles.sql`):
>
> ```bash
> "$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" ef database update --project src/Numera.Api
> ```

## 3. Vor dem Merge: Test-Gate

Immer mit dem **user-lokalen SDK** bauen (der PATH-Default ist .NET 8 und schlägt fehl):

```bash
# .NET: Build + Tests
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test

# Web: Lint (tsc) + Unit-Tests + Prod-Build
cd web && npm run lint && npm run test && npm run build
```

Dazu die betroffenen Seiten lokal im Browser durchklicken (z. B. Beleg anlegen,
PDF erzeugen). Erst wenn alles grün ist und die Oberfläche passt → Schritt 4.

## 4. Nach Prod bringen

```bash
git switch master
git merge --no-ff dev
git push                     # origin/master
```

Dann auf der VM (`/home/ubuntu/Numera`) deployen:

```bash
git pull

# Migrationen anwenden (läuft auf dem gemounteten Quellcode)
docker compose -f docker-compose.prod.yml run --rm migrate

# Images neu bauen und Container ersetzen.
# WICHTIG: Das Frontend steckt im `caddy`-Image (kein eigener web-Service),
#          und ohne --force-recreate übernimmt `up` neue Images nicht zuverlässig.
docker compose -f docker-compose.prod.yml build api worker caddy
docker compose -f docker-compose.prod.yml up -d --force-recreate api worker caddy

docker compose -f docker-compose.prod.yml ps
```

Danach `https://<DOMAIN>/` im Browser prüfen.

## 5. Warum lokal (statt zweiter VM)

Für ein 2-Personen-Setup fängt der lokale Stack praktisch alle Fehler ab (Build,
Migrationen, App-Start, Optik) – ohne eine zweite VM patchen, sichern und mit
Secrets/TLS/Keycloak pflegen zu müssen. Eine eigene Staging-VM lohnt sich erst,
wenn jemand Nicht-Technisches per URL abnehmen soll oder die volle Prod-Topologie
(echte Domain, Caddy-TLS) vor jedem Release gespiegelt werden muss. Lässt sich
jederzeit nachrüsten – `docker-compose.prod.yml` ist dafür schon parametrisiert.
