# Datenschutz-Zuarbeit für Anwalt/Datenschützer (AVV, TOMs, Datenflüsse)

> **Zweck:** Technische Faktenbasis für die Erstellung von Datenschutzerklärung, AV-Vertrag (DSGVO Art. 28) und TOM-Dokument (Art. 32) durch einen Fachanwalt/Datenschützer.
> **Dies ist keine Rechtsberatung und kein fertiger Rechtstext** — es beschreibt den technischen Ist-Stand, damit die Rechtstexte daraus erstellt werden können.
> Stand: zum Zeitpunkt der Code-Prüfung. Mit **⚠️ zu bestätigen** markierte Punkte bitte verifizieren.

---

## 1. Rollen (datenschutzrechtlich)
- **Numera-Betreiber (du)** = **Auftragsverarbeiter** (Art. 28) für die personenbezogenen Daten, die der Kunde in Numera verarbeitet.
- **Kunde (das zahlende Unternehmen)** = **Verantwortlicher** für die Daten seiner eigenen Geschäftspartner/Kunden und seiner Mitarbeiter-Nutzer.
- Folge: Pro Kunde wird ein **AV-Vertrag** benötigt; die eingesetzten **Subprozessoren** (Abschnitt 4) sind darin zu benennen und freizugeben.

## 2. Kategorien betroffener Personen
- **Nutzer des Kunden** (Mitarbeiter/Owner): Name, E-Mail, Login, Rolle.
- **Geschäftspartner/Kunden des Kunden** (natürliche Personen): Name, Anschrift, Kontaktdaten, USt-IdNr., Rechnungs-/Zahlungsdaten, **Bankverbindung/IBAN**, Belege.
- Mittelbar in Belegen/Dokumenten enthaltene Dritte.

## 3. Kategorien verarbeiteter Daten
- **Stammdaten**: Firmen-/Partnerdaten, Kontakte, Artikel/Katalog.
- **Buchhaltungs-/Finanzdaten**: Angebote, Rechnungen, Gut-/Lastschriften, offene Posten, Zahlungen, Journal/Konten.
- **Bankdaten**: Kontoumsätze, IBAN, Kontoverbindungen (nur bei Nutzung des Banking-Moduls).
- **Belege/Dokumente**: PDFs, E-Rechnungs-XML, eingehende Belege, Kundenakte-Dateien, Logo.
- **Technische Daten**: Logs, **Audit-Trail** (wer hat wann was geändert).

## 4. Subprozessoren / externe Empfänger (Art. 28 Abs. 4)
Nur diese externen Dienste erhalten ggf. personenbezogene Daten:

| Dienst | Zweck | Übermittelte Daten | Wann aktiv | Hinweis |
|---|---|---|---|---|
| **finAPI GmbH** (München, DE) | Kontoanbindung/Transaktionsabruf (PSD2) | Bankzugang, Kontoumsätze, IBAN | nur wenn Kunde Banking nutzt | BaFin-reguliert; AVV einholen |
| **Stripe Payments Europe Ltd** (Dublin, IE) | Abo-Zahlung **des Kunden an dich** (nicht Zahlungen der Endkunden) | Firmenname, E-Mail, Zahlungs-/Rechnungsdaten | bei kostenpflichtigem Abo | mögl. Drittlandtransfer (US) → SCC/Angemessenheit prüfen |
| **Oracle Cloud Infrastructure (OCI)** | Hosting/Infrastruktur (VM, Speicher, Backups) | **alle** Daten (at rest) | immer | ⚠️ **Region bestätigen** (muss EU sein); AVV von Oracle einholen |

**Nicht** als externe Subprozessoren einzustufen (laufen auf eigener Infrastruktur unter OCI):
- **Keycloak** (Authentifizierung), **PostgreSQL** (Datenbank), **KoSIT-Validator** (E-Rechnungsprüfung), **Worker** (Hintergrundjobs) — alles selbst gehostete Container.

**Besonderheit E-Mail-Versand:** Dokument-Mails werden über den **vom Kunden selbst konfigurierten SMTP-Server** versendet (`TenantEmailSettings`, Passwort feldverschlüsselt). Der Mailanbieter ist also eine Wahl des Kunden, **kein Subprozessor des Betreibers**. Die Plattform selbst versendet aktuell **keine** System-Mails (Welcome-Mail ist nur ein Stub).

**Gesetzlicher Empfänger (kein Auftragsverarbeiter):** ELSTER/Finanzverwaltung bei Umsatzsteuer-Voranmeldung — sobald dieses Feature live ist (derzeit noch offen).

## 5. Datenflüsse (Kurzüberblick)
1. **Browser → Caddy (HTTPS/TLS) → API/Keycloak** — sämtlicher Zugriff läuft TLS-verschlüsselt über den Reverse-Proxy.
2. **API → PostgreSQL** — alle Geschäftsdaten, mandantengetrennt per Row-Level-Security.
3. **API → finAPI** (optional) — Kontoanbindung; Credentials feldverschlüsselt gespeichert.
4. **API → Stripe** — Abo-/Zahlungsabwicklung des Kunden.
5. **Worker → SMTP des Kunden** — Versand von Rechnungs-/Dokument-Mails.
6. **Backup-Skript → Backup-Ablage** — regelmäßige DB-Dumps mit Aufbewahrungsregel.

## 6. Technische & organisatorische Maßnahmen (Art. 32) — Ist-Stand aus dem Code

**Vertraulichkeit**
- **Mandantentrennung:** PostgreSQL **Row-Level-Security** auf jeder Tabelle (`app.current_tenant`); Laufzeit-DB-Rolle ohne BYPASSRLS; zusätzlicher EF-Query-Filter (Defense-in-Depth); dedizierte **Cross-Tenant-Isolationstests**.
- **Zugangskontrolle:** Keycloak (OIDC) mit **BFF-Cookie-Session**; serverseitige Session, kein Token im Browser-JS.
- **Zugriffskontrolle:** Rollenmodell (z. B. Owner-only für sensible Aktionen), **feingranulare Bereichsrechte** (AreaPermissionGuard), read-only-Write-Guard bei Abo-Degradierung.
- **Verschlüsselung sensibler Secrets at rest:** ASP.NET **DataProtection** (`IDataProtector`) für finAPI-Zugangsdaten und SMTP-Passwörter — Klartext wird nie gespeichert.
- **Transportverschlüsselung:** Caddy mit automatischem HTTPS/TLS.

**Integrität**
- **GoBD-Unveränderbarkeit:** finalisierte Belege sind DB-seitig immutabel (Festschreibung).
- **Audit-Trail:** `audit_events` (append-only, mandantengescoped) protokolliert Änderungen.

**Verfügbarkeit & Belastbarkeit**
- **Backups:** automatisierte PostgreSQL-Dumps mit **Aufbewahrungsregel** (täglich/wöchentlich, Pruning) — `deploy/backup.sh`.
- **Restore:** Wiederherstellungs-Skript vorhanden — `deploy/restore.sh`.
- **Health-Check-Endpoint** vorhanden.

**Verfahren / Betroffenenrechte**
- **Datenübertragbarkeit (Art. 20):** vollständiger Self-Service-Datenexport (ZIP aus JSON + Originaldateien), Owner-only, auditiert.
- **GoBD-Verfahrensdokumentation** vorhanden (`docs/gobd-verfahrensdokumentation.md`).

## 7. Offene Punkte / Empfehlungen (vor Verkauf zu klären)
Ehrliche Lücken, die der Anwalt/Datenschützer bzw. du adressieren sollte:

1. **Löschkonzept (Art. 17):** Vollständige Mandanten-/Account-Löschung ist **noch nicht implementiert** (geplant als Phase B1). Für den AVV wird ein Löschkonzept + Löschfristen benötigt (inkl. GoBD-Aufbewahrungspflichten).
2. **DB-at-rest-Verschlüsselung:** Nur einzelne Secrets sind feldverschlüsselt. Verschlüsselung der **gesamten Datenbank/Volumes** ist nicht im Code — ⚠️ auf OCI-Ebene prüfen/aktivieren.
3. **Backup-Verschlüsselung & -Zugriffsschutz:** `backup.sh` zeigt keine Verschlüsselung der Dumps — ⚠️ Ablageort, Zugriffsrechte und Verschlüsselung der Backups klären.
4. **DataProtection-Keys:** müssen auf einem **geschützten, gesicherten** Volume persistiert werden (bei Verlust sind verschlüsselte Secrets unlesbar) — ⚠️ Persistenz/Backup der Keys bestätigen.
5. **E-Mail-Verifizierung bei Registrierung:** aktuell keine (nur Stub) — relevant für Identitäts-/Missbrauchsschutz.
6. **Sicherheits-Header** (HSTS/CSP u. a.) fehlen in der Caddy-Konfiguration (geplant als Phase B4).
7. **Rate-Limiting** auf Registrierung/Login fehlt (geplant als Phase B3).
8. **AVV mit jedem Subprozessor** (finAPI, Stripe, Oracle) einholen und ablegen; **Drittlandtransfer** (Stripe/US, OCI-Region) bewerten (SCC/Angemessenheitsbeschluss).
9. **Verzeichnis von Verarbeitungstätigkeiten (Art. 30)** formalisieren (dieses Dokument ist die Grundlage dafür).

---

### Anhang: belegende Code-Stellen (für Rückfragen)
- Mandantentrennung/RLS: `src/platform/Numera.Platform.Db/Sql/rls_policies.sql`, `NumeraDbContext.cs`
- Secret-Feldverschlüsselung: `src/Numera.Api/Program.cs` (AddDataProtection), `Services/FinApi/IBankCredentialProtector.cs`, `SmtpPasswordProtector`
- Datenexport (Art. 20): `src/Numera.Api/Services/TenantExportService.cs`, `Endpoints/ExportEndpoints.cs`
- Abo-Degradierung/Write-Guard: `src/Numera.Api/Auth/BillingDegradationWriteGuardMiddleware.cs`
- Backups/Restore/TLS: `deploy/backup.sh`, `deploy/restore.sh`, `deploy/Caddyfile`
