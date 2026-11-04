# Roadmap: Verkaufsfertig (SaaS Go-to-Market)

> Ziel: Numera als **gehostete Multi-Tenant-SaaS an Fremdkunden (B2B) verkaufen und betreiben** dürfen.
> Stand der Prüfung: Kern (Multi-Tenancy, Billing, Compliance) steht; es fehlen Recht, ein paar Code-Blocker und Betriebsreife.

## Annahmen (bei Bedarf korrigieren)
1. **Zielkunden = Unternehmer (B2B).** Eine Buchhaltungs-SaaS richtet sich an Gewerbetreibende → kein Verbraucher-Widerrufsrecht nötig, aber AGB/Datenschutz/Impressum/AVV trotzdem Pflicht. Falls auch Verbraucher: Widerrufsbelehrung + Button-Lösung ergänzen.
2. **Nur gehostet (SaaS bei uns).** Self-Hosted-Verkauf beim Kunden ist ein **separater, optionaler Track** (Ablauf-Lizenz-Modul) und nicht Teil dieses Meilensteins.

## Rollen
- **Codex** = Implementierung (nach Review/Plan durch Claude).
- **Claude** = Planung, Review, Zuarbeit (Datenflüsse/TOMs-Entwurf, Testpläne).
- **Extern** = Fachanwalt/Datenschützer (Recht), Steuerberater (fachliche Abnahme), ggf. ELSTER.

## Sequenzierung (Kurzfassung)
1. **A1 (Recht, extern) SOFORT anstoßen** — längste Kalender-Durchlaufzeit (1–3 Wochen).
2. Parallel dazu **B (Code-Blocker)** und **C (Betrieb)** umsetzen.
3. **A2 (Rechtstexte in die App)** sobald A1-Inhalte vorliegen (Gerüst vorher).
4. **D (GTM-Feinschliff)** vor dem ersten echten Kunden.

---

## Phase A — Rechtliche Pflicht-Basis  ⛔ Blocker
**Warum:** In DE ohne Impressum/Datenschutz/AGB ab Tag 1 abmahnfähig; als Hoster wirst du Auftragsverarbeiter (DSGVO Art. 28).

### A1 — Rechtstexte erstellen lassen  · *extern + Claude-Zuarbeit* · Kalenderzeit 1–3 Wo
- Impressum (§5 DDG), Datenschutzerklärung (DSGVO), AGB (B2B), AV-Vertrag (Art. 28) + TOMs-Dokument.
- Claude-Zuarbeit: Liste der **Datenflüsse & Subprozessoren** (Stripe, Keycloak, FinTS/Banking-Provider, Oracle Cloud, Mail/SMTP, KoSIT) + **TOMs-Entwurf** aus dem Code (RLS, Verschlüsselung, Zugriff, Backup, Löschung).
- **Abhängigkeit:** A2 braucht die finalen Texte.

### A2 — Rechtstexte in die App  · *Codex* · ~1–2 Tage
- Statische Seiten: `/impressum`, `/datenschutz`, `/agb` + Footer-Links (auch auf Landing/Login erreichbar).
- Registrierung: Pflicht-Checkbox „AGB + Datenschutz akzeptiert" → **Zustimmung mit Zeitstempel + Dokumentversion persistieren** (Nachweisbarkeit).
- Versionierung der Texte (bei Änderung erneute Zustimmung anfordern).

---

## Phase B — Code-Blocker  ⛔ Blocker · *Codex* · gesamt ~1 Woche

### B1 — DSGVO-Löschung / Offboarding (Art. 17)  · ~2–3 Tage
- Vollständiges Löschen eines Mandanten: alle RLS-Daten + Blobs (PDFs, XML, Belege, Kundenakte, Logo) + Keycloak-User/Org.
- Owner-initiiert mit harter Bestätigung **+ erzwungenem Export vorher**; Admin-seitig auslösbar; Audit-Eintrag.
- GoBD-Abwägung: Soft-Delete/Grace-Fenster + Hinweis auf Aufbewahrungspflichten; endgültige Löschung nach Frist.

### B2 — Registrierung transaktional machen  · ~1–2 Tage
- Orphan-Bug fixen: Keycloak-User + Org + DB-Mandant + Membership atomar bzw. mit Kompensation/Rollback bei Teil-Fehler.
- Idempotenz (Retry führt nicht zu Doppelanlage), sauberer Fehlerpfad + Logging.

### B3 — Rate-Limiting  · ~1 Tag
- `AddRateLimiter`/`UseRateLimiter`: strenge Limits auf `/api/auth/register` + Login-Challenge, moderate auf teure Endpoints (Export, Upload, Render).
- IP- **und** Account-basiert; 429 mit Retry-After.

### B4 — Sicherheits-Header & CORS-Review  · ~0,5 Tag
- HSTS, CSP, X-Frame-Options, Referrer-Policy, X-Content-Type-Options. **Erst prüfen, ob Caddy das schon setzt** — sonst App-Middleware.
- CORS-Konfiguration gegen BFF-Modell gegenchecken (same-origin → eng halten).

---

## Phase C — Betriebsreife  ⛔ Pflicht-Minimum für zahlende Kunden · gesamt ~3–5 Tage

### C1 — Error-Tracking + Alerting  · *Codex* · ~1 Tag
- Sentry (o. ä.) in API + Worker + Web; Alert bei unbehandelten Fehlern. Du musst mitbekommen, wenn es bei einem Kunden brennt.

### C2 — Strukturiertes Logging + Readiness  · *Codex* · ~1 Tag
- Serilog-Sink (strukturiert, korreliert per Request-/Tenant-Id — **ohne PII**).
- `/health` erweitern um **Readiness** (DB + Keycloak erreichbar); externer Uptime-Monitor (z. B. UptimeRobot).

### C3 — Backup-Restore-Probe + Runbook  · *du + Claude* · ~0,5–1 Tag
- Backups existieren — jetzt einen **echten Restore proben** (frische VM/Container, Daten einspielen, App hochfahren) und als Runbook dokumentieren. Ein ungetestetes Backup ist kein Backup.

### C4 — Prod-Secrets/Config-Review für SaaS  · *du + Claude* · ~0,5 Tag
- Go-Live-Hardening gegen Mehrkunden-Betrieb gegenchecken (Keycloak-Admin, Stripe-Live-Keys, SMTP, Backup-Verschlüsselung).

---

## Phase D — Go-to-Market-Feinschliff  ⚠️ vor dem ersten echten Kunden

### D1 — ELSTER-XSD / UStVA-Übermittlung abschließen  · *Codex, ggf. extern* · eigener größerer Block
- Aus Go-Live-Prep offen. Kunden erwarten funktionierende Umsatzsteuer-Voranmeldung. Umfang groß genug für eine **eigene Unterphase** — separat planen.

### D2 — Steuerberater-Freigabe  · *extern*
- Fachliche Abnahme der Buchführungs-/Kontenlogik vor Verkauf.

### D3 — Abo-Lifecycle E2E verifizieren  · *Claude + du* · ~1 Tag
- Durchgehender Test: Signup → Trial → Zahlung → `past_due` (Zugriff bleibt) → `unpaid`/`canceled` (read-only) → Reaktivierung → Kündigung → Löschung. Als Checkliste + möglichst Integrationstest.

### D4 — Preise/Pläne live schalten  · *du* · ~0,5 Tag
- Stripe-Live-Preise je Plan anlegen; PlanCapabilityMap gegen die Verkaufsargumente gegenchecken.

---

## Optional / separate Tracks (keine Blocker)
- **Operator-/Admin-Konsole** (zuvor verworfen): ab ~5 Kunden sinnvoll für Support/Sperren/Plan-Override + Audit. Bis dahin via DB machbar.
- **Self-Hosted-Verkauf** mit signierter Ablauf-Lizenz (Degradierung auf read/export bei Ablauf): nur wenn ein Kunde Datenhoheit verlangt.

---

## Aufwandsübersicht
| Block | Zuständig | Aufwand | Art |
|---|---|---|---|
| A1 Rechtstexte | extern + Claude-Zuarbeit | 1–3 Wo Kalenderzeit | ⛔ |
| A2 Texte in App | Codex | 1–2 Tage | ⛔ |
| B1 Löschung | Codex | 2–3 Tage | ⛔ |
| B2 Registrierung transaktional | Codex | 1–2 Tage | ⛔ |
| B3 Rate-Limiting | Codex | 1 Tag | ⛔ |
| B4 Security-Header | Codex | 0,5 Tag | ⛔ |
| C1 Error-Tracking | Codex | 1 Tag | ⛔ |
| C2 Logging/Readiness | Codex | 1 Tag | ⛔ |
| C3 Restore-Probe | du+Claude | 0,5–1 Tag | ⛔ |
| C4 Secrets-Review | du+Claude | 0,5 Tag | ⚠️ |
| D1 ELSTER | Codex/extern | groß (separat) | ⚠️ |
| D2 Steuerberater | extern | — | ⚠️ |
| D3 Abo-Lifecycle-Test | Claude+du | 1 Tag | ⚠️ |
| D4 Preise live | du | 0,5 Tag | ⚠️ |

**Interner Code-Aufwand (A2+B+C1/C2): ~2–2,5 Wochen.** Kritischer Pfad ist die **externe Rechts-Durchlaufzeit (A1)** — deshalb zuerst anstoßen.
