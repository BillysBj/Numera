# Requirements: Numera

**Defined:** 2026-07-09
**Core Value:** Ein Unternehmen kann seine komplette Auftrags- und Finanzverwaltung — von der Rechnung (inkl. E-Rechnung) bis zur Buchhaltung — rechtskonform (GoBD, E-Rechnungspflicht) an einem Ort erledigen, auf jedem Gerät.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Plattform & Mandanten (PLAT)

- [ ] **PLAT-01**: Nutzer kann sich registrieren, eine Firma (Mandant) anlegen und sich sicher anmelden (E-Mail/Passwort, Session bleibt erhalten)
- [ ] **PLAT-02**: Alle Daten sind strikt pro Mandant isoliert (Postgres RLS + App-Filter); Cross-Tenant-Zugriff ist durch Tests abgesichert
- [ ] **PLAT-03**: Nutzer kann Teammitglieder einladen; Rollen: Inhaber (Vollzugriff), Mitarbeiter (eingeschränkt), Steuerberater (lesender Zugriff auf Belege/Auswertungen)
- [ ] **PLAT-04**: Tarif-Feature-Gates (S/M/L/XL) steuern Funktionsumfang pro Mandant (ohne Zahlungsabwicklung)
- [ ] **PLAT-05**: Jede finanzrelevante Änderung wird in einem unveränderbaren Audit-Log erfasst (Wer/Was/Wann)
- [ ] **PLAT-06**: UI ist vollständig zweisprachig (Deutsch + Englisch), umschaltbar pro Nutzer
- [ ] **PLAT-07**: App läuft als responsive PWA auf Desktop und Smartphone und ist installierbar
- [ ] **PLAT-08**: Nutzer kann alle Daten seines Mandanten exportieren (DSGVO-Portabilität)
- [ ] **PLAT-09**: Geldbeträge werden systemweit exakt gerechnet (decimal/numeric, nie float); Rundung folgt EN-16931-Regeln

### Kunden, Lieferanten & CRM (CRM)

- [ ] **CRM-01**: Nutzer kann Kunden und Lieferanten mit Stammdaten anlegen, bearbeiten und archivieren (Anschrift, USt-ID, Zahlungsbedingungen, Kontakte)
- [ ] **CRM-02**: Nutzer sieht pro Kunde eine Historie aller Belege und Aktivitäten
- [ ] **CRM-03**: Nutzer kann Notizen an Kunden/Lieferanten anheften
- [ ] **CRM-04**: Nutzer kann Aufgaben mit Erinnerungen/Fälligkeiten zu Kunden anlegen
- [ ] **CRM-05**: Nutzer kann Dateien am Kunden ablegen (Kundenakte)

### Produktkatalog (CATL)

- [ ] **CATL-01**: Nutzer kann Standardprodukte und -services mit Preis, Einheit und USt-Satz verwalten
- [ ] **CATL-02**: Nutzer kann Katalogeinträge als Positionen in Belege übernehmen

### Belegkette & Dokumente (DOCS)

- [ ] **DOCS-01**: Nutzer kann Angebote erstellen und in Auftragsbestätigungen, Lieferscheine und Rechnungen überführen (Belegkette mit Statusverfolgung)
- [ ] **DOCS-02**: Nutzer kann Belege als PDF mit eigenem Logo/Briefpapier erzeugen (Layouts in Deutsch und Englisch)
- [ ] **DOCS-03**: Nutzer kann Belege direkt per E-Mail an Kunden versenden
- [ ] **DOCS-04**: Entwürfe sind frei bearbeitbar; finalisierte Belege sind unveränderbar (GoBD) — Korrekturen erzeugen Storno-/Korrekturbelege

### Rechnungen (INV)

- [ ] **INV-01**: Nutzer kann Rechnungen mit allen Pflichtangaben nach §14 UStG erstellen
- [ ] **INV-02**: Rechnungsnummern werden pro Mandant automatisch, eindeutig und race-sicher bei Finalisierung vergeben (konfigurierbares Nummernformat)
- [ ] **INV-03**: Nutzer kann finalisierte Rechnungen stornieren (Stornorechnung) und Gutschriften erstellen
- [ ] **INV-04**: USt-Behandlung deckt ab: 19 %/7 %/0 %, Kleinunternehmer §19 (inkl. Pflichthinweis), Reverse-Charge §13b (inkl. Bauleistungen), innergemeinschaftliche Lieferung — modelliert als EN-16931-Steuerkategorien mit korrekten Pflichttexten
- [ ] **INV-05**: Nutzer kann Rechnungen in Fremdwährung erstellen (EUR-Umrechnung für USt-Ausweis)
- [ ] **INV-06**: Nutzer kann Serienrechnungen anlegen, die automatisch nach Zeitplan erzeugt werden
- [ ] **INV-07**: Nutzer kann Abschlags- und Schlussrechnungen erstellen (Anzahlungslogik mit Verrechnung)

### E-Rechnung (EINV)

- [ ] **EINV-01**: Nutzer kann Rechnungen als XRechnung (UBL und CII, EN 16931) erzeugen und übermitteln (Download + E-Mail-Versand)
- [ ] **EINV-02**: Nutzer kann Rechnungen als ZUGFeRD (PDF/A-3 mit eingebettetem XML) erzeugen; PDF- und XML-Werte stimmen exakt überein
- [ ] **EINV-03**: Jede ausgehende E-Rechnung wird vor Finalisierung gegen den KoSIT-Validator geprüft; Fehler blockieren den Versand und werden verständlich erklärt
- [ ] **EINV-04**: Nutzer kann empfangene E-Rechnungen (XRechnung/ZUGFeRD) hochladen, validieren und menschenlesbar anzeigen
- [ ] **EINV-05**: Empfangene E-Rechnungen werden als Eingangsbelege dem Lieferanten zugeordnet und abgelegt

### Offene Posten & Mahnwesen (OPDN)

- [ ] **OPDN-01**: Finalisierte Rechnungen erzeugen offene Posten; Nutzer sieht eine OP-Übersicht mit Fälligkeiten und Zahlungsstatus
- [ ] **OPDN-02**: Nutzer kann Zahlungen manuell erfassen und offenen Posten zuordnen (Teilzahlungen möglich)
- [ ] **OPDN-03**: Nutzer kann Zahlungserinnerungen und mehrstufige Mahnungen erzeugen und versenden (konfigurierbare Mahnstufen, Fristen, Gebühren)

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Belege & Buchhaltung

- **BOOK-01**: Belegerfassung mit Scanner/Kamera-Upload und OCR-Belegprüfung
- **BOOK-02**: Automatischer Belegempfang per E-Mail (eigene Inbox-Adresse)
- **BOOK-03**: GoBD-Langzeitbelegarchiv (10 Jahre, revisionssicher) mit Verfahrensdokumentation
- **BOOK-04**: Buchungslogik mit SKR03/SKR04, elektronisches Kassenbuch
- **BOOK-05**: EÜR- und GuV-Berichte, BWA
- **BOOK-06**: Doppelte Buchführung / Bilanz für GmbH/UG
- **BOOK-07**: Anlagenverwaltung mit AfA
- **BOOK-08**: USt-Voranmeldung & Zusammenfassende Meldung (Berechnung + Export; ERiC-Direktversand später)
- **BOOK-09**: Automatische Umsatzsteuerprognose

### Banking & Finanzen

- **BANK-01**: Multibanking über Bank-API-Anbieter (finAPI; 180-Tage-Re-Consent)
- **BANK-02**: Automatischer Zahlungsabgleich (Transaktionen ↔ offene Posten)
- **BANK-03**: Überweisungen auslösen (SEPA/PIS)
- **BANK-04**: Planung & Liquiditätsprognose

### Steuerberater & Integrationen

- **STB-01**: DATEV-Export (Buchungsstapel, EXTF-Format, SKR03/04-Mapping)
- **STB-02**: Elektronische Pendelakte
- **API-01**: Public API + Webhooks
- **PORT-01**: Online-Kundenportal (Rechnungen einsehen/bezahlen)

### Monetarisierung

- **PAY-01**: Stripe-Abo-Abrechnung für Tarife (Trial, Upgrade/Downgrade)

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Lohn & Gehalt | ITSG/GKV-Zertifizierung + SV-Meldeverfahren = eigenes Produkt; ggf. Partner-Integration später |
| Volle Finanzbuchhaltung für Kanzleien (Jahresabschluss) | Konkurriert mit DATEV Kanzlei-Rechnungswesen; falsche Zielgruppe — vorbereitende Buchhaltung + DATEV-Export genügt |
| Warenwirtschaft / ERP (Lager, Bestände) | Tiefes eigenes Produktfeld; Katalog + Lieferscheine genügen, Integration via API |
| Eigene FinTS/HBCI-Konnektoren | Lizenz- und Wartungsaufwand; lizenzierter Aggregator (finAPI) stattdessen |
| Eigene OCR-Engine | ML-Produkt für sich; OCR wird eingekauft |
| Eigene ELSTER-Protokoll-Implementierung | ERiC ist die zertifizierte Bibliothek; nichts selbst erfinden |
| In-App-Steuerberatung | Rechtlich reguliert (StBerG); nur Daten bereitstellen |
| Echtzeit-Co-Editing von Belegen | Widerspricht GoBD-Unveränderbarkeit; kein Buchhaltungsmehrwert |
| "GoBD-zertifiziert"-Claim | Existiert nicht; abmahnfähig — nur "GoBD-konform" kommunizieren |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| (populated during roadmap creation) | | |

**Coverage:**
- v1 requirements: 33 total
- Mapped to phases: 0
- Unmapped: 33 ⚠️

---
*Requirements defined: 2026-07-09*
*Last updated: 2026-07-09 after initial definition*
