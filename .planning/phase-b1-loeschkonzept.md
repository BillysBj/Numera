# Phase B1 — Löschkonzept (DSGVO Art. 17 ↔ GoBD/§147 AO)

> Ziel: Numera kann Kundendaten rechtskonform löschen — ohne die GoBD-Unveränderbarkeit und die 10-jährige Aufbewahrungspflicht zu verletzen.
> Blocker für den Verkauf: ein Kunde muss sich **abmelden** können und seine Daten müssen danach **nachweislich gelöscht** werden.

## Kernproblem
- **DSGVO Art. 17** verlangt Löschung auf Verlangen.
- **GoBD + §147 AO** verlangen 10 Jahre Aufbewahrung von Buchungsbelegen **und** Unveränderbarkeit (DB-Trigger blockieren UPDATE/DELETE finalisierter Belege; `numera_app` hat gar kein DELETE-Recht darauf).
- Auflösung: **Zwei getrennte Szenarien** mit unterschiedlichem Mechanismus.

---

## Szenario 1 — Mandanten-Offboarding (vollständige Löschung eines Kunden) ⬅ Haupt-Deliverable
Der Kunde kündigt / der Vertrag endet. Der **Kunde ist Verantwortlicher** und hat seine eigene Aufbewahrungspflicht; als **Auftragsverarbeiter löschst du auf seine Weisung**, nachdem er seine Daten gesichert hat. Das ist rechtlich sauber.

### Ablauf (3 Stufen)
**Stufe 1 — Löschung anfordern (Owner-Self-Service)**
- Owner-only Aktion „Konto löschen" in den Einstellungen.
- **Harte Bestätigung** (Firmenname eintippen) + **Zwang zum Export vorher** (Art.-20-Export anbieten/erzwingen, Bestätigung „Daten gesichert").
- Setzt `tenant.deletion_requested_at`, `deletion_requested_by`; **Stripe-Abo kündigen**.

**Stufe 2 — Grace-Periode (30 Tage, konfigurierbar)**
- Bei Anforderung: `deletion_scheduled_for = heute + 30 Tage`; Mandant wird sofort **read-only** (vorhandenen `BillingDegradationWriteGuard`-Mechanismus bzw. einen `deletion_pending`-Status wiederverwenden) — Login + Export bleiben möglich, keine Schreibzugriffe.
- **Abbruch (Undo):** Owner **oder** Operator setzt `deletion_scheduled_for = null` → wieder beschreibbar.
- **Beschleunigen:** Operator setzt `deletion_scheduled_for = jetzt`.

**Purge-Zeitpunkt:** ein **täglicher Sweep-Job** (Hangfire-Cron) sucht Mandanten mit `deletion_scheduled_for <= jetzt` und führt Stufe 3 aus. Robuster als ein einzelner verzögerter Job (überlebt Worker-Neustarts).

**Stufe 3 — Hard-Purge (privilegierter, auditierter Wartungsjob)**
1. **finAPI** (falls Banking genutzt): Sub-User/Connection bei finAPI löschen (Best-Effort) — sonst verwaiste Kontofreigabe extern.
2. **Keycloak**: alle Org-Mitglieder-User löschen + die Organization löschen (vorhandenen Admin-Client aus `InvitationService` wiederverwenden; Org-ID = Tenant-ID).
3. **Datenbank**: alle Zeilen des Mandanten über **alle** Tabellen + In-DB-Blobs löschen. Weil Immutabilitäts-Trigger das sonst blockieren:
   - Läuft als **privilegierte Wartungs-Rolle** (nicht `numera_app`), in **einer Transaktion**, mit `SET session_replication_role = replica` (deaktiviert User-Trigger für den Purge — bewusst und nur hier).
   - Lösch-Reihenfolge: Kinder→Eltern (FK-sicher) oder reflektive Tabellen-Enumeration wie in `TenantExportService`, gefiltert auf `tenant_id`; zuletzt die `tenants`-Zeile.
   - Strikt gekapselt: nur über Wartungsjob/Operator erreichbar, **niemals** aus dem normalen Request-Flow eines Mandanten.
4. **Purge-Nachweis** in ein **mandanten-unabhängiges Operator-Log** schreiben (append-only, außerhalb jeder Tenant-RLS — die `audit_events` des Mandanten werden ja mitgelöscht): Tenant-ID, Name, Anforderer, Zeitpunkt, Zeilenzahlen. **Ohne** personenbezogene Daten der Betroffenen — nur der Nachweis, dass gelöscht wurde (Art. 5 Abs. 2 Rechenschaftspflicht).

### Wichtige Hinweise
- **Backups:** Die Daten verbleiben in bestehenden DB-Backups bis diese nach der Aufbewahrungsregel (`backup.sh`) auslaufen. Löschung wirkt **nicht** rückwirkend in Backups — das ist DSGVO-konform, **muss aber dokumentiert sein** (begrenzte Backup-Retention).
- **Operator-Pfad zwingend:** Da der Purge Trigger umgeht und RLS verlässt, ist er sicherheitskritisch — eigene privilegierte DB-Rolle, kein Tenant-Zugriff, volle Protokollierung.

---

## Szenario 2 — Betroffenen-Löschung in einem aktiven Mandanten (Art. 17 Einzelperson)
Ein einzelner Geschäftspartner/eine Person verlangt Löschung, erscheint aber auf **finalisierten Belegen** (Aufbewahrungspflicht).

### Korrekte Behandlung: NICHT hart löschen
- **Art. 17 Abs. 3 b** nimmt Daten aus, die einer gesetzlichen Aufbewahrungspflicht unterliegen → statt Löschung **Einschränkung der Verarbeitung (Art. 18) + Anonymisierung** der nicht aufbewahrungsrelevanten Felder.
- Umsetzung:
  - Partner-**Archivierung** existiert bereits (Soft-Delete) → raus aus der aktiven Nutzung.
  - Neue Aktion **„Partner anonymisieren"**: blankt die Stammdaten-Kontaktfelder (Name/Anschrift/E-Mail/Telefon) in der Partner-Zeile, **lässt aber die auf finalisierten Belegen eingefrorenen Snapshots unangetastet** (die sind ohnehin immutabel und aufbewahrungspflichtig).
  - Optionales Restriction-Flag + Vermerk im Audit-Trail.
- Endgültige Löschung der belegbezogenen Daten erst **nach Ablauf der §147-Frist** (10 Jahre) — als späterer, zeitgesteuerter Retention-Purge (eigenes Thema, **nicht** Teil von B1).

### Scope-Entscheidung
- **In B1 enthalten:** Partner-Anonymisierungs-Aktion (klein, baut auf vorhandener Archivierung) + Dokumentation des Prozesses.
- **Nicht in B1 (eigene spätere Phase B1b):** automatischer, fristgesteuerter Retention-Purge über abgelaufene Belege.

---

## Datenmodell-Änderungen
- `tenants`: `deletion_requested_at`, `deletion_requested_by`, `deletion_scheduled_for`, `status`-Erweiterung (z. B. `PendingDeletion`).
- Neue Tabelle **`platform_deletion_log`** (tenant-unabhängig, kein RLS, append-only): Nachweis der Purges.
- `partners`: optionales `anonymized_at` / Restriction-Flag (falls nicht schon über `IArchivable` abbildbar).

## Betroffene Komponenten
- **Neu:** `TenantDeletionService` (Orchestrierung Stufen 1–3), `TenantPurgeService` (privilegierter DB-Purge), Hangfire-Job `TenantPurgeJob`, Operator-/Owner-Endpoints, Web-UI „Konto löschen".
- **Wiederverwenden:** Keycloak-Admin-Client (`InvitationService`), Export (`TenantExportService`), Write-Guard (Read-only), finAPI-Provider (Sub-User-Löschung).
- **DB:** Migration für Felder + `platform_deletion_log` + privilegierte Wartungs-Rolle.

## Sicherheit / Risiken
- Purge umgeht Trigger + RLS → **nur** über privilegierte Rolle + Wartungsjob, nie aus Tenant-Request. Wie die Admin-Konsole die heikelste Stelle.
- **Idempotenz**: Purge-Job muss wiederholbar sein (Teilabbruch → Resume), da Keycloak/finAPI/DB nicht in einer Transaktion liegen.
- Grace-Fenster + Undo verhindern versehentliche irreversible Löschung.

## Tests
- Integrationstest: Mandant anlegen → Daten (inkl. finalisierter Belege, Blobs) → Purge → **alle** Tabellen für den Tenant leer, `tenants`-Zeile weg, Keycloak-User/Org weg, `platform_deletion_log`-Eintrag da.
- Cross-Tenant-Sicherheit: Purge von Tenant A lässt Tenant B **vollständig** unberührt (erweitert die vorhandenen Isolationstests).
- Immutabilitäts-Trigger greifen außerhalb des Purge weiterhin (kein genereller Trigger-Off).
- Grace/Undo: Abbruch stellt Schreibzugriff wieder her.
- Partner-Anonymisierung: Kontaktfelder leer, Beleg-Snapshots unverändert.

## Umsetzungsstand
- **Scheibe 1 (fertig, committet `bd2b00d`):** Datenmodell (`deletion_*`, `last_export_at`), Owner-Self-Service-Anforderung/Undo, Export-Zwang, 30-Tage-Grace, Read-only-Write-Guard, Endpoints, 22 Tests.
- **Scheibe 2 (fertig, getestet, committet — Purge `0a5bc95`, finAPI-Automatisierung Folgecommit):** echter Hard-Purge.
  - `platform_deletion_log` (append-only, **kein** RLS, **nicht** im EF-Modell → kein Export-Leak/kein Selbst-Purge); explizites `REVOKE ALL FROM numera_app`.
  - `TenantPurgeService`: privilegierter DB-Purge über **Maintenance-Connection** (`ConnectionStrings:Maintenance` → Fallback `Hangfire` = Postgres-Superuser; **keine neue DB-Rolle nötig**), eine Transaktion mit `SET LOCAL session_replication_role = replica` (Trigger-Bypass nur hier), reflektive Löschung aller `ITenantEntity`-Tabellen + `tenants`-Zeile, atomarer Log-Insert.
  - `TenantPurgeOrchestrator`: Stripe-Kündigung + finAPI-Sub-User-Löschung + Keycloak-Org/User-Löschung (`KeycloakTenantPurger`, idempotent/404-sicher) als **Best-Effort**, Ergebnis je Schritt im Log; dann DB-Purge.
  - **finAPI-Löschung automatisiert:** `IBankConnectionProvider.DeleteProviderDataAsync` → finAPI `DELETE /api/v1/users` (Sub-User-Löschung kaskadiert auf alle Connections/Konten/Transaktionen/Consents); Dedup pro Sub-User, 404-tolerant/idempotent; Log `deleted:Nusers` bzw. `deleted:x;failed:y` (Best-Effort, blockiert DB-Purge nie).
  - `PurgeDueTenantsFanOutJob` (täglicher Hangfire-Cron 03:00, `AccountDeletion:PurgeSweepCron`) + `PurgeTenantJob` (per Tenant).
  - Tests: Purge löscht auch immutable `audit_events`, Cross-Tenant unberührt, Trigger außerhalb Purge weiterhin aktiv, Log append-only, Stripe-Cancel + Idempotenz. (4 Tests, grün; Suite 442/443, 1 Flake unabhängig.)
- **Scheibe 3 (fertig, getestet, committet — Backend `ebb6961`, Frontend `1631b5a`):**
  - **Partner-Anonymisierung (Szenario 2):** `PartnerAnonymizationService` überschreibt alle PII-Stammfelder, behält Partnernummer/Rollen/DATEV-Konten, setzt `AnonymizedAt` + archiviert, härte-löscht PII-Kinder (Kontakte/Notizen/Aufgaben/Timeline) + schreibt einen neutralen „Anonymized"-Eintrag, Audit `partner.anonymized`. Ausgestellte Rechnungen bleiben unberührt (eingefrorener `RecipientSnapshot`). Endpoint `POST /api/partners/{id}/anonymize` (Owner-only, idempotent); PUT auf anonymisierten Partner → 409. 2 Tests.
  - **Web-UI „Konto löschen"** (`/settings/account`, Owner-only): Status/Grace, Export-Zwang (Art. 20), Namens-Bestätigung, Undo; 3 Tests. **Partner-„Anonymisieren"-Button** (Owner-only) auf der Detailseite + Badge/Hinweis. i18n de/en, Routen-Guard.
- **Offen / bewusst entschieden:**
  - **Stripe-Kündigung erfolgt beim Purge (T+30)**, nicht bei der Anforderung → Abo läuft in der Grace-Periode weiter. **Bewusst so:** der Account bleibt in der Grace-Periode per Undo wiederherstellbar; würde man bei Anforderung kündigen, müsste ein Undo das Abo neu anlegen. (Falls doch früher gewünscht: `cancel_at_period_end` bei Anforderung.)
  - **Migrationen** `20261104152142_PlatformDeletionLog` + `20261104185359_AddPartnerAnonymizedAt` müssen deployt werden (prod via `migrate.sh`; dev beim nächsten Lauf).
  - **Noch nicht nach master gemergt / nicht auf der Prod-VM deployt** (dev-Branch; lokaler Smoke-Test + Merge/Deploy ausstehend).

## Produkt-Entscheidungen (entschieden)
1. **Auslöser:** Owner-Self-Service-Anforderung + Auto-Purge nach Grace; Operator kann abbrechen/beschleunigen. ✅
2. **Grace-Periode:** **30 Tage**, konfigurierbar. ✅
3. **Export-Zwang vor Löschung:** **erzwingen** — Löschung erst möglich, nachdem der Owner den Export heruntergeladen/bestätigt hat. ✅
4. **Purge-Zeitpunkt:** `deletion_requested_at + 30 Tage`, ausgeführt vom täglichen Sweep-Job (siehe Stufe 2/3). ✅

## Aufwand (Schätzung)
- Szenario 1 (Offboarding, Kern): ~2–3 Tage (Codex).
- Szenario 2 (Partner-Anonymisierung): ~0,5–1 Tag.
- Tests: in den Schätzungen enthalten.
