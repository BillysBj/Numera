# Milestones

## v1.0 Rechnungen & E-Rechnung (Shipped: 2026-08-02)

**Phases completed:** 9 phases, 61 plans
**Timeline:** 2026-07-09 → 2026-08-02 (25 days)
**Scope:** ~52k LOC C# (.NET 10 modular monolith) + ~11.8k LOC TypeScript (React 19 PWA)
**Verification:** Solution build 0/0 · Integration 180 · Platform 131 · Web vitest 56 — all green
**Archive:** [v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md) · [v1.0-REQUIREMENTS.md](milestones/v1.0-REQUIREMENTS.md)

**Delivered:** Ein deutsches Unternehmen kann seine komplette Auftrags- und Finanzverwaltung von der §14-Rechnung inkl. gesetzlicher E-Rechnung bis zum Mahnwesen rechtskonform (GoBD, EN 16931) an einem Ort erledigen — als Multi-Tenant-PWA auf jedem Gerät.

**Key accomplishments:**
- **Multi-Tenant-Plattformkern** — Postgres-RLS-Isolation (durch Cross-Tenant-Sicherheitssuite bewiesen), exakte decimal-Geldarithmetik mit EN-16931-Rundung, unveränderbares Audit-Log, Keycloak-BFF-Auth, Tarif-Feature-Gates S/M/L/XL, React-19-PWA mit DE/EN-i18n.
- **Belegkette & §14-Rechnungskern** — Angebot→AB→Lieferschein→Rechnung mit DB-erzwungener GoBD-Unveränderbarkeit, race-sicherer Nummernvergabe, EN-16931-USt-Kategorien (19/7/0 %, §19, §13b, i.g.) und Storno/Gutschrift.
- **PDF & E-Rechnung-Engine** — QuestPDF-§14-Belege (DE/EN) über Worker-Tier + E-Mail-Versand; XRechnung (UBL+CII) und ZUGFeRD (PDF/A-3) erzeugt und **live gegen den KoSIT-Validator** geprüft; Empfang/Parsing/Zuordnung eingehender E-Rechnungen.
- **Geld-Kreislauf & erweiterte Rechnungstypen** — Zahlungserfassung (Teilzahlungen) + mehrstufiges Mahnwesen; Fremdwährung, Serienrechnungen (Hangfire) und Abschlags-/Schlussrechnungen mit Anzahlungsverrechnung.
- **CRM-Ausbau & Steuerberater-Zugang** — Aufgaben, Kundenakte (append-only), Team-Einladungen und eine durchgesetzt lesende Steuerberater-Rolle.
- **v1-Feinschliff & Compliance** — DSGVO-Datenexport (streamed ZIP), durchgängige Tarif-Gate-UX (Upgrade-Hinweise statt Fehler), PWA-Offline-Degradation, GoBD-Verfahrensdokumentation (Entwurf) + „nie zertifiziert"-Wording-Guard.

---
