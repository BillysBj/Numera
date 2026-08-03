# Requirements: Numera — Milestone v2.0

**Defined:** 2026-08-02
**Milestone:** v2.0 — Buchhaltung, Banking, Belege & Ausgaben, Monetarisierung
**Core Value:** Ein Unternehmen erledigt seine komplette Auftrags- und Finanzverwaltung — von der Rechnung inkl. E-Rechnung bis zur Buchhaltung — rechtskonform (GoBD, DSGVO) an einem Ort, auf jedem Gerät.

> Builds on shipped v1.0 (multi-tenant + RLS, roles, CRM, catalog, invoice chain + §14 + EN-16931 VAT, e-invoice create/receive + KoSIT, open items + payments + dunning, S/M/L/XL server-authoritative gates [no billing], PWA DE/EN, exact decimal money, immutable audit log, DSGVO export). v2.0 adds four domains; v1 capabilities are dependencies, not re-specified.

## v2.0 Requirements

Committed scope for this milestone. Each maps to a roadmap phase (numbering continues from v1 → **Phase 10+**).

### Buchhaltung (ACCT) — the foundation

- [ ] **ACCT-01**: Nutzer wählt beim Setup einen Kontenrahmen (SKR03 oder SKR04); Numera führt einen mandantenspezifischen aktiven Kontensatz
- [ ] **ACCT-02**: Doppische Buchungs-Engine mit unveränderbarem, ausgeglichenem Soll/Haben-Journal (GoBD-fest, DB-erzwungen); Korrekturen entstehen als Storno-Buchung, nie durch Bearbeiten
- [ ] **ACCT-03**: Finalisierte Ausgangsrechnungen werden automatisch verbucht (Konto- + Steuerschlüssel-Zuordnung je Kontenrahmen, EN-16931-USt-Kategorien)
- [ ] **ACCT-04**: Erfasste Zahlungen werden automatisch verbucht und gleichen den offenen Posten aus (Bank ↔ Forderung)
- [ ] **ACCT-05**: Eingangsrechnungen/Ausgaben werden ins Journal gebucht (Aufwand + Vorsteuer) und speisen EÜR/USt-VA
- [ ] **ACCT-06**: Nutzer sieht Buchungsjournal und Kontoauszug je Konto (revisionssichere Lesesichten)
- [ ] **ACCT-07**: EÜR-Bericht (Struktur der Anlage EÜR, Zufluss/Abfluss-Prinzip) für Freiberufler/Kleinunternehmer
- [ ] **ACCT-08**: USt-Voranmeldung — Kennziffern-Berechnung (Kz 81/86/35/66/83/41/89/46/47/61) + Prüfansicht + Export als ELSTER-XML/Druck (kein ERiC-Direktversand)
- [ ] **ACCT-09**: Buchungsperioden lassen sich sperren (Festschreibung); gesperrte Perioden sind unveränderbar

### Banking (BANK) — Multibanking & Abgleich

- [ ] **BANK-01**: Nutzer verbindet Bankkonten über finAPI (AIS) und verwaltet die PSD2-Einwilligung inkl. periodischer Re-Authentifizierung/SCA
- [ ] **BANK-02**: Kontoumsätze werden regelmäßig, idempotent und dublettenfrei synchronisiert (pro Mandant, RLS)
- [ ] **BANK-03**: Umsätze werden automatisch offenen Posten zugeordnet (Betrag + Verwendungszweck/Referenz + Gegenpartei), mit Konfidenz-Score und Prüf-Queue
- [ ] **BANK-04**: Bestätigte Zuordnung erfasst die Zahlung und verbucht Bank ↔ Forderung/Verbindlichkeit (nutzt die v1-Zahlungslogik)
- [ ] **BANK-05**: Nutzer kann eine Transaktion manuell zuordnen, korrigieren oder auf mehrere Rechnungen/Konten splitten
- [ ] **BANK-06**: Import-Fallback für Kontoumsätze per CSV/MT940/CAMT (falls PSD2 nicht verfügbar)

### Belege & Ausgaben (BELEG)

- [ ] **BELEG-01**: Nutzer erfasst Belege per Kamera und Datei-Upload (PDF/Bild)
- [ ] **BELEG-02**: OCR extrahiert strukturierte Felder (Lieferant, Datum, Beträge, USt, Rechnungsnummer) mit Konfidenz + Prüfung
- [ ] **BELEG-03**: Bereits empfangene E-Rechnungen (XRechnung/ZUGFeRD) werden strukturiert gelesen (ohne OCR, wiederverwendet v1-Parser)
- [ ] **BELEG-04**: GoBD-konformes revisionssicheres Langzeitarchiv (WORM, Originalformat, indiziert, 10 Jahre, mit der Buchung verknüpft)
- [ ] **BELEG-05**: Numera erzeugt einen Buchungsvorschlag (Aufwandskonto + Vorsteuer-Steuerschlüssel); nach Bestätigung wird gebucht — nie ohne Prüfung
- [ ] **BELEG-06**: Beleg wird einem Lieferanten zugeordnet (v1-CRM) und Dubletten werden erkannt
- [ ] **BELEG-07**: Pro Mandant eine eindeutige E-Mail-Eingangsadresse; weitergeleitete Belege/Anhänge werden automatisch erfasst (mandantengetrennt)

### Monetarisierung (BILL) — Stripe-Abrechnung (unabhängiger Track)

- [ ] **BILL-01**: Nutzer startet einen bezahlten Tarif über Stripe Checkout (gehostet, keine Kartendaten in Numera)
- [ ] **BILL-02**: Tarif-Berechtigung wird per signierten, idempotenten Stripe-Webhooks gesetzt (Stripe = Quelle der Wahrheit) und steuert das bestehende `tenants.plan`-Gate
- [ ] **BILL-03**: Self-Service Upgrade/Downgrade über das Stripe Customer Portal (inkl. Proration)
- [ ] **BILL-04**: Testphase (Trial) mit vollem Zugriff, danach automatische Umstellung auf Bezahlung
- [ ] **BILL-05**: Mahnwesen für das eigene Abo + geordnete Degradation bei fehlgeschlagener Zahlung
- [ ] **BILL-06**: Nutzer verwaltet Zahlungsmethode und sieht eigene Abo-Rechnungen (Customer Portal)
- [ ] **BILL-07**: USt-korrekte Abrechnung des eigenen Abos (inkl. Reverse-Charge für EU-B2B)

## Deferred Requirements (v2.1+)

Anerkannt, aber nicht in diesem Milestone. Auslöser jeweils in Klammern.

### Buchhaltung
- **ACCT-D1**: BWA (DATEV-Stil, Monatsreport + Vormonatsvergleich) — günstig über der fertigen Engine (v2.1)
- **ACCT-D2**: GuV + einfache Bilanz für GmbH/UG (Periodenabgrenzung) — großer Scope, GmbH/UG-Markt (v3)
- **ACCT-D3**: Geführte USt-VA-Checkliste („was hat sich geändert", Plausibilitätswarnungen) (v2.1)
- **ACCT-D4**: Dauerfristverlängerung / Sondervorauszahlung (v2.1)
- **ACCT-D5**: „Für den Steuerberater vorbereiten"-Periodenpaket (v2.1); DATEV-EXTF-Export (v2.1)
- **ACCT-D6**: Elektronisches Kassenbuch (GoBD-konform, ohne TSE) (v2.1, bei Bargeld-Bedarf)
- **ACCT-D7**: Direkter ERiC-Versand der USt-VA (v3 — Herstellerregistrierung + C-Bibliothek)
- **ACCT-D8**: Anlagenverwaltung / AfA (v3 — eigene Sub-Domäne)

### Banking
- **BANK-D1**: SEPA-Überweisungen ausgehend (Lieferantenrechnungen bezahlen, PIS + SCA) (v2.1)
- **BANK-D2**: Gelernte Buchungsregeln (geteilt mit Belege) (v2.1)
- **BANK-D3**: Fremdwährungs-Abgleich (nutzt v1-FX) (v2.1)
- **BANK-D4**: Liquiditäts-/Cashflow-Sicht über echte Salden (v2.1)
- **BANK-D5**: SEPA-Lastschrifteinzug (Mandatsverwaltung) (v3)

### Belege
- **BELEG-D1**: Gelernte Lieferanten-Buchungsregeln (v2.1)
- **BELEG-D2**: Drei-Wege-Abgleich Beleg ↔ Umsatz ↔ Verbindlichkeit (v2.1, wenn Banking+Belege stabil)
- **BELEG-D3**: Positions-genaue Extraktion (Line-Items) (v2.1+)

### Monetarisierung
- **BILL-D1**: Jahrespreise + In-App-Upgrade-Nudges (v2.1)
- **BILL-D2**: Nutzungsabhängige Downgrade-Leitplanken (v2.1)
- **BILL-D3**: In-App-Tarifvergleich am Gate-Trefferpunkt (v2.1)

## Out of Scope

Bewusst ausgeschlossen (mit Begründung, gegen Scope-Creep).

| Feature | Reason |
|---------|--------|
| TSE-zertifiziertes Kassensystem / POS (KassenSichV) | Eigenes reguliertes Produkt (TSE, DSFinV-K, Belegausgabepflicht); das Kassen*buch* braucht keine TSE |
| Eigene PSD2-Aggregation / Screen-Scraping / Bankzugangsdaten speichern | Unter PSD2 unzulässig + Haftung; finAPI ist die lizenzierte XS2A-Lösung |
| Eigenes FinTS/HBCI oder handgebautes pain.001 | Lizenz-/Wartungsaufwand; finAPI PIS stattdessen |
| GoCardless/Nordigen als Aggregator | Für Neukunden geschlossen (Research verifiziert) |
| Vollständiger HGB-Jahresabschluss + eBilanz + Anhang/Lagebericht | Riesige regulierte Taxonomie; eigenes Milestone |
| Freies manuelles Buchen als primäre UX | Fehlbuchungs-/GoBD-Risiko; Standardflüsse automatisieren, manuell nur als gated „Advanced"-Notausgang |
| Vollautomatisches Buchen ohne Prüf-Queue (Banking/Belege) | Eine falsche Buchung verunreinigt das unveränderbare Journal; nur hoch-konfidente Auto-Freigabe |
| Eigenes Billing-UI statt Stripe Customer Portal / Kartendaten lokal speichern | Erfindet Proration/Steuer/SCA/Dunning neu; PCI-Scope; lokal = Berechtigung, nicht Preis |
| Metered/nutzungsbasiertes Billing jetzt | Tarife sind seat-/feature-basiert (S/M/L/XL); Metering erst bei klarer Nachfrage |
| Client-seitiger Gate-Status als Wahrheit | Umgehbar → Umsatzleck; Gate bleibt server-autoritativ |
| Eigenes OCR-Modell in-house trainieren | Hoher Aufwand; reife IDP-Dienste vorhanden (Azure DI, EU-Region) |
| Bearbeiten/Löschen archivierter Originale | Bricht GoBD-Unveränderbarkeit; Archiv ist WORM, Korrekturen als neue Version |

## Traceability

Wird bei der Roadmap-Erstellung befüllt (jede Anforderung → genau eine Phase, ab Phase 10).

| Requirement | Phase | Status |
|-------------|-------|--------|
| ACCT-01, ACCT-02, ACCT-03, ACCT-04, ACCT-05, ACCT-06, ACCT-09 | Phase 10 — Buchhaltungs-Fundament | Pending |
| ACCT-07, ACCT-08 | Phase 11 — Berichte & USt-Voranmeldung | Pending |
| BELEG-01, BELEG-02, BELEG-03, BELEG-04, BELEG-05, BELEG-06, BELEG-07 | Phase 12 — Belege & Ausgaben | Pending |
| BANK-01, BANK-02, BANK-03, BANK-04, BANK-05, BANK-06 | Phase 13 — Banking & Zahlungsabgleich | Pending |
| BILL-01, BILL-02, BILL-03, BILL-04, BILL-05, BILL-06, BILL-07 | Phase 14 — Monetarisierung (Stripe) | Pending |

**Coverage:**
- v2.0 requirements: 29 total (ACCT 9, BANK 6, BELEG 7, BILL 7)
- Mapped to phases: 29 (Phasen 10–14)
- Unmapped: 0 ✓

---
*Requirements defined: 2026-08-02 for milestone v2.0*
*Derived from .planning/research/{FEATURES,STACK,ARCHITECTURE,PITFALLS,SUMMARY}.md*
