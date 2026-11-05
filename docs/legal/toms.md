# Technische und organisatorische Maßnahmen (TOMs) — ENTWURF

> ⚠️ **ENTWURF** nach Art. 32 DSGVO, aus dem tatsächlichen System abgeleitet (Anlage zum AVV).
> Vor Freigabe vom Betreiber bestätigen/ergänzen; `[…]` prüfen.

## 1. Vertraulichkeit

**Zutrittskontrolle (physisch):** Betrieb bei Oracle Cloud Infrastructure; physische Sicherheit
nach den Rechenzentrums-Zertifizierungen des Anbieters. Region `[bestätigen — EU/Frankfurt?]`.

**Zugangskontrolle (System):** Authentifizierung über Keycloak (OIDC). Die SPA ist ein Public
Client ohne Token im Browser; die Session läuft serverseitig über ein HttpOnly-Cookie (BFF-Muster).
Kein direkter Zugriff auf DB/Keycloak aus dem Netz — nur der Reverse Proxy (Caddy) ist öffentlich
(Ports 80/443), alle übrigen Dienste sind containerintern. Rate-Limiting auf Registrierung/Login
und teure Endpunkte.

**Zugriffskontrolle (Daten):** Strikte Mandantentrennung über PostgreSQL **Row-Level-Security**
(FORCE RLS); die Laufzeit-DB-Rolle besitzt kein BYPASSRLS. Rollen-/Rechtemodell je Nutzer
(Owner/Mitarbeiter/Steuerberater) mit bereichsgenauen Berechtigungen. Jede Anfrage setzt den
Mandantenkontext; ohne gültigen Kontext keine Datensicht.

## 2. Integrität

**Weitergabekontrolle:** Transportverschlüsselung per TLS (automatische Zertifikate, HSTS).
Security-Header inkl. Content-Security-Policy am Edge. Externe Dienste (Stripe, finAPI) werden per
Redirect angebunden, nicht im Browser eingebettet.

**Eingabekontrolle:** Unveränderbares Audit-Log (append-only, DB-Trigger gegen UPDATE/DELETE).
GoBD-feste Belegkette; Korrekturen nur als Storno/Gutschrift. Rechnungs-/Belegdaten werden zum
Festschreibungszeitpunkt als unveränderlicher Snapshot eingefroren.

## 3. Verfügbarkeit und Belastbarkeit

**Backups:** Automatische tägliche Datenbank-Sicherungen (`pg_dump`, komprimiert) mit Tages-/
Wochen-Retention. **Offsite-Kopie:** `[einzurichten/bestätigen — Pflicht für echte DR]`.
Backup-Verschlüsselung: `[bestätigen]`.

**Wiederherstellbarkeit:** Restore-Skript + Runbook vorhanden; dokumentierte Restore-Probe:
`[durchführen/bestätigen]`.

**Schlüsselverwaltung:** DataProtection-Schlüssel (u. a. für verschlüsselte SMTP-Zugangsdaten)
persistiert auf einem separaten Volume; geteilt zwischen API und Worker.

## 4. Verfahren zur Überprüfung und Bewertung

**Auftragskontrolle:** Unterauftragsverarbeiter nur mit AVV/SCC (siehe AVV §6).

**Datenschutzfreundliche Voreinstellungen:** Nur technisch notwendige Cookies, kein Tracking.
Datenminimierung im Lösch-Log (keine personenbezogenen Daten der Betroffenen).

**Löschung:** Konto-Löschung mit 30-tägiger Wiederherstellungsfrist, danach unwiderrufliche
Löschung aller Mandantendaten inkl. Blobs (PDFs/XML/Belege/Kundenakte/Logo) und externer
Identitäten (Keycloak, finAPI-Sub-User); für aufbewahrungspflichtige Geschäftspartner
Anonymisierung statt Löschung.

**Monitoring/Protokollierung:** `[Error-Tracking/Alerting + strukturiertes Logging — Phase C,
noch offen; hier nach Umsetzung ergänzen.]`

## Offene Bestätigungen (vor Freigabe)

- Oracle-Region (EU?) · Backup-Offsite + -Verschlüsselung · Restore-Probe durchgeführt ·
  Monitoring aktiv (Phase C) · SMTP-Anbieter benannt.
