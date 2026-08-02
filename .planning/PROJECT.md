# Numera

## What This Is

Numera ist eine cloudbasierte Buchhaltungs- und Rechnungssoftware für den deutschen Markt — ein Konkurrent zu Lexware Office und sevDesk. Sie richtet sich an Freiberufler, Kleinunternehmen und Kapitalgesellschaften (GmbH/UG) und läuft als Multi-Tenant-SaaS mit responsiver PWA auf Laptop, PC und Smartphone (inkl. Belegscan per Kamera).

## Core Value

Ein Unternehmen kann seine komplette Auftrags- und Finanzverwaltung — von der Rechnung (inkl. gesetzlich verpflichtender E-Rechnung) bis zur Buchhaltung — rechtskonform (GoBD, E-Rechnungspflicht seit 01.01.2025) an einem Ort erledigen, auf jedem Gerät.

## Current Milestone: v2.0 — Buchhaltung, Banking & Belege

**Goal:** Aus der Rechnungs-Software wird die komplette Finanzverwaltung: Numera schließt den Kreis von der Ausgangsrechnung über Bankdaten und Eingangsbelege bis zur Buchhaltung und zur echten Tarif-Abrechnung.

**Target features (v2.0):**
- **Buchhaltung** — Kontenrahmen SKR03/SKR04, Buchungslogik, elektronisches Kassenbuch, EÜR/GuV/BWA-Berichte, USt-Voranmeldung (Berechnung + Export)
- **Banking** — Multibanking über Bank-API (finAPI): Kontoumsätze, automatischer Zahlungsabgleich mit offenen Posten, SEPA-Überweisungen
- **Belege & Ausgaben** — Belegscan (Kamera/Upload) + OCR-Belegprüfung, automatischer Belegempfang per E-Mail, GoBD-konformes revisionssicheres Langzeitarchiv
- **Monetarisierung** — Stripe-Abo-Abrechnung: Self-Service-Tarifwechsel (Upgrade/Downgrade), Trial, Zahlungsverwaltung

## Requirements

### Validated

**v1.0 — Rechnungen & E-Rechnung (SHIPPED 2026-08-02, Phases 1–9):**
- ✓ Multi-Tenant-Grundgerüst: Registrierung, Login, Firmen-Accounts, Nutzerverwaltung + Rollen (Inhaber/Mitarbeiter/Steuerberater) — v1.0
- ✓ Strikte Mandanten-Isolation (Postgres RLS + App-Filter), durch Cross-Tenant-Tests abgesichert — v1.0
- ✓ Kunden/Lieferanten (Stammdaten, Historie, Notizen, Aufgaben, Kundenakte-Dateien) — v1.0
- ✓ Belegkette Angebot→AB→Lieferschein→Rechnung mit GoBD-Unveränderbarkeit + Storno/Gutschrift — v1.0
- ✓ §14-Rechnungen, race-sichere Nummernvergabe, EN-16931-USt-Kategorien (19/7/0, §19, §13b, i.g.) — v1.0
- ✓ Erweiterte Rechnungstypen: Fremdwährung, Serienrechnungen, Abschlags-/Schlussrechnungen — v1.0
- ✓ PDF-Belege (eigenes Briefpapier, DE/EN) über Worker-Tier + E-Mail-Versand — v1.0
- ✓ E-Rechnung erstellen: XRechnung (UBL+CII) und ZUGFeRD (PDF/A-3), live gegen KoSIT validiert — v1.0
- ✓ E-Rechnung empfangen: Upload, validieren, menschenlesbar anzeigen, Lieferant zuordnen — v1.0
- ✓ Offene Posten + Zahlungserfassung (Teilzahlungen) + mehrstufiges Mahnwesen — v1.0
- ✓ Produktkatalog (Preis, Einheit, USt) als Belegpositionen — v1.0
- ✓ Tarif-Feature-Gates S/M/L/XL mit UX (Upgrade-Hinweise statt Fehler), ohne Zahlungsabwicklung — v1.0
- ✓ Responsive PWA (Desktop + Mobile, installierbar, Offline-Shell) — v1.0
- ✓ UI zweisprachig DE/EN — v1.0
- ✓ Exakte Geldarithmetik (decimal, EN-16931-Rundung), unveränderbares Audit-Log — v1.0
- ✓ DSGVO-Datenexport + GoBD-Verfahrensdokumentation (Entwurf) — v1.0

### Active

**Nächstes Milestone (v1.1+ — noch zu definieren via /gsd:new-milestone). Kandidaten:**
- [ ] Belegerfassung mit Belegscanner (Kamera/Upload) und Belegprüfung
- [ ] GoBD-konformes Langzeitbelegarchiv (revisionssicher, Verfahrensdokumentation)
- [ ] Buchhaltung: Kontenrahmen SKR03/SKR04, Buchungslogik, elektronisches Kassenbuch
- [ ] USt-Voranmeldung & Zusammenfassende Meldung (Berechnung + Export; ERiC-Direktversand später)
- [ ] EÜR- und GuV-Berichte, BWA
- [ ] Doppelte Buchführung / Bilanz für GmbH/UG
- [ ] Anlagenverwaltung (AfA)
- [ ] Multibanking über Bank-API (Anbieter in Research klären), automatischer Zahlungsabgleich, Überweisungen
- [ ] Automatischer Belegempfang per E-Mail
- [ ] Serienrechnungen und besondere Rechnungstypen (Abschlagsrechnungen, EU-Rechnungen, Bauleistungen §13b, englische Rechnungen)
- [ ] Steuerberater-Zugang (eigene Rolle) + DATEV-Export / elektronische Pendelakte
- [ ] Online-Kundenportal, Umsatzstatistiken & Reports, Kundenakte mit Datei-Uploads
- [ ] Planung & Prognose, automatische Umsatzsteuerprognose
- [ ] Public API
- [ ] Stripe-Abo-Abrechnung für Tarife (Trial, Upgrade/Downgrade)

### Out of Scope

- Lohn & Gehalt — stark reguliert (ITSG-Zertifizierung, SV-Meldeverfahren); bewusst ausgeklammert, bis das Kernprodukt steht
- ERiC-Direktübermittlung in v1 — formale Hürden (Herstellerregistrierung, C-Bibliothek); zunächst Berechnung + manueller Übertrag ins ELSTER-Portal
- Native iOS/Android-Apps — PWA deckt Mobile ab; native Apps nur, falls PWA-Grenzen (z.B. Push auf iOS) relevant werden
- Zahlungsabwicklung (Stripe) in v1 — Feature-Gates reichen, bis echte Kunden zahlen sollen

## Context

- Vorbild ist die Funktionsmatrix von Lexware Office (Tarife S/M/L/XL): E-Rechnung, Auftrag & Buchhaltung, Steuerberater-Zugang, CRM, Banking & Finanzen
- E-Rechnungspflicht (Empfang) gilt in Deutschland seit 01.01.2025 — zentrales Verkaufsargument und Pflichtfeature
- Relevante Standards: EN 16931, XRechnung (UBL/CII), ZUGFeRD/Factur-X, GoBD, DSGVO
- Zielmarkt Deutschland; UI Deutsch + Englisch
- Greenfield-Projekt, leeres Repository (D:\Dev\Privat\Numera)
- Offene Punkte für später: Hosting-Präferenz (DSGVO-freundlich, z.B. Hetzner vs. Hyperscaler), Branding/Design-Richtung, Tiefe des Steuerberater-Zugangs, Wahl des Bank-API-Anbieters

## Constraints

- **Compliance**: GoBD-Konformität (Unveränderbarkeit, Nachvollziehbarkeit, Archivierung) und DSGVO — Pflicht für Buchhaltungssoftware im deutschen Markt
- **Standards**: E-Rechnungen müssen EN 16931 erfüllen (XRechnung + ZUGFeRD), sonst rechtlich wertlos
- **Plattform**: Muss auf Laptop, PC und Handy laufen → responsive PWA, ein Codebase
- **Architektur**: Multi-Tenant von Anfang an (SaaS für viele Kunden), Tarifmodell S/M/L/XL als Feature-Gates
- **Korrektheit**: Finanzdaten — Rundung, Steuersätze (19 %, 7 %, §13b, innergemeinschaftlich) und Summenbildung müssen exakt sein (keine Float-Arithmetik für Geldbeträge)

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Multi-Tenant-SaaS statt Einzellösung | Produkt für viele Kunden wie sevDesk | ✓ Good — RLS-Isolation durch Cross-Tenant-Suite bewiesen (v1.0) |
| Zielgruppe inkl. GmbH/UG (Bilanz) | Größerer Markt; doppelte Buchführung als spätere Ausbaustufe | — Pending (Buchhaltung = v2) |
| PWA statt nativer Apps | Ein Codebase für Desktop + Mobile, Belegscan per Kamera möglich | ✓ Good — installierbar + Offline-Shell (v1.0) |
| Direkte Bank-API (statt nur CSV-Import) | Multibanking ist Kernfeature; Anbieterwahl in Research | — Pending (Banking = v2, finAPI vorgemerkt) |
| v1 = Rechnungen + E-Rechnung | Schnellster Weg zu echtem Nutzerwert; Pflichtfeature seit 2025 | ✓ Good — v1.0 ausgeliefert |
| USt-VA erst als Berechnung/Export | ERiC-Integration zu aufwendig für v1 | — Pending (v2) |
| Stripe-Abrechnung verschoben | Feature-Gates in v1 reichen | ✓ Good — Tarif-Gates server-autoritativ (v1.0) |
| UI Deutsch + Englisch | Wunsch des Gründers; i18n von Anfang an | ✓ Good — DE/EN durchgängig (v1.0) |
| Geld als decimal, EN-16931-Rundung (nie float) | Rechtliche Korrektheit von Steuer/Summen | ✓ Good — Golden-File-Tests grün (v1.0) |
| E-Rechnung gegen echten KoSIT-Validator prüfen | „konform" muss der Staat bestätigen, nicht wir | ✓ Good — Live-KoSIT akzeptiert alle Szenarien in UBL+CII (v1.0) |
| EInvoicing + Dunning erst ab Tarif L | Preisdifferenzierung; E-Rechnungspflicht-Risiko vom Nutzer akzeptiert | ⚠️ Revisit — E-Rechnung ist Pflicht; L-Gate ggf. lockern |
| „GoBD-konform", nie „zertifiziert" | Zertifizierung existiert nicht; abmahnfähig | ✓ Good — Build-brechender Wording-Guard erzwingt es (v1.0) |

## Current State

**Shipped: v1.0 — Rechnungen & E-Rechnung (2026-08-02).** 9 Phasen, 61 Pläne, ~52k LOC C# (.NET 10, modularer Monolith: Platform-Kern + Module Crm/Catalog/Sales/Ledger + Api/Worker) + ~11.8k LOC TypeScript (React 19 PWA, Tailwind/shadcn, TanStack Query). Postgres 18 mit hand-geschriebener RLS je Tabelle; Keycloak-BFF-Auth (HttpOnly-Cookie); Hangfire-Worker für PDF/E-Mail/E-Rechnung/Serienrechnung; QuestPDF; ZUGFeRD-csharp 18; KoSIT-Sidecar; Mailpit. Tests: Integration 180 + Platform 131 + Web-Vitest 56, alle grün; Solution-Build 0/0.

Bekannte Grenzen / Tech-Debt: keine WebApplicationFactory → Owner-only/TaxAdvisor-Middleware + Endpoint-403s sind auf Policy-/Prädikat-Ebene getestet, nicht end-to-end HTTP. Verfahrensdokumentation ist ein Entwurf. veraPDF nicht in CI (PDF/A-3-Marker stattdessen geprüft). Buchhaltung, Banking, Belegscan/OCR, DATEV-Export, Stripe-Abrechnung = v2.

## Next Milestone Goals

Via `/gsd:new-milestone` zu definieren. Naheliegende Richtungen aus dem v2-Backlog: Belegerfassung + OCR, Buchhaltung (SKR03/04, EÜR/GuV/BWA, USt-VA), Multibanking (finAPI) + Zahlungsabgleich, DATEV-Export/Pendelakte, Stripe-Abo-Abrechnung.

---
*Last updated: 2026-08-02 — started milestone v2.0 (Buchhaltung, Banking, Belege, Monetarisierung)*
