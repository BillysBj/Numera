# Numera

## What This Is

Numera ist eine cloudbasierte Buchhaltungs- und Rechnungssoftware für den deutschen Markt — ein Konkurrent zu Lexware Office und sevDesk. Sie richtet sich an Freiberufler, Kleinunternehmen und Kapitalgesellschaften (GmbH/UG) und läuft als Multi-Tenant-SaaS mit responsiver PWA auf Laptop, PC und Smartphone (inkl. Belegscan per Kamera).

## Core Value

Ein Unternehmen kann seine komplette Auftrags- und Finanzverwaltung — von der Rechnung (inkl. gesetzlich verpflichtender E-Rechnung) bis zur Buchhaltung — rechtskonform (GoBD, E-Rechnungspflicht seit 01.01.2025) an einem Ort erledigen, auf jedem Gerät.

## Requirements

### Validated

(None yet — ship to validate)

### Active

**v1-Kern (Rechnungen + E-Rechnung):**
- [ ] Multi-Tenant-Grundgerüst: Registrierung, Login, Firmen-Accounts (Mandanten), Nutzerverwaltung
- [ ] Kunden und Lieferanten verwalten (Stammdaten, Historie, Notizen)
- [ ] Angebote, Auftragsbestätigungen, Lieferscheine, Rechnungen erstellen (PDF, eigenes Layout/Briefpapier)
- [ ] E-Rechnung erstellen: XRechnung (UBL/CII) und ZUGFeRD, konform zu EN 16931
- [ ] E-Rechnung empfangen/erfassen: XRechnung- und ZUGFeRD-Dateien einlesen, validieren, anzeigen
- [ ] Offene-Posten-Übersicht (Forderungen, Fälligkeiten, Zahlungsstatus)
- [ ] Mahnwesen: Zahlungserinnerungen und Mahnstufen
- [ ] Standardprodukte und -services (Artikelverwaltung)
- [ ] Tarif-Feature-Gates (S/M/L/XL) ohne Zahlungsabwicklung
- [ ] Responsive PWA (Desktop + Mobile, installierbar)
- [ ] UI zweisprachig: Deutsch + Englisch (i18n von Anfang an)

**Spätere Ausbaustufen (bereits geplant):**
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
| Multi-Tenant-SaaS statt Einzellösung | Produkt für viele Kunden wie sevDesk | — Pending |
| Zielgruppe inkl. GmbH/UG (Bilanz) | Größerer Markt; doppelte Buchführung als spätere Ausbaustufe | — Pending |
| PWA statt nativer Apps | Ein Codebase für Desktop + Mobile, Belegscan per Kamera möglich | — Pending |
| Direkte Bank-API (statt nur CSV-Import) | Multibanking ist Kernfeature; Anbieterwahl in Research | — Pending |
| v1 = Rechnungen + E-Rechnung | Schnellster Weg zu echtem Nutzerwert; Pflichtfeature seit 2025 | — Pending |
| USt-VA erst als Berechnung/Export | ERiC-Integration zu aufwendig für v1 | — Pending |
| Stripe-Abrechnung verschoben | Feature-Gates in v1 reichen | — Pending |
| UI Deutsch + Englisch | Wunsch des Gründers; i18n von Anfang an | — Pending |

---
*Last updated: 2026-07-09 after initialization*
