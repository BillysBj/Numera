# Roadmap: Numera

## Overview

Numera wird von den nicht-nachrüstbaren Fundamenten her aufgebaut: Zuerst der Plattform-Kern (Multi-Tenant mit RLS, exakte Geldarithmetik, unveränderbares Audit-Log, Auth, Tarif-Gates, i18n), dann Stammdaten (CRM + Produktkatalog) als einfache Validierung der Mandanten-Isolation. Darauf folgt das Herz von v1 — die Belegkette mit Rechnungskern (Unveränderbarkeit, race-sichere Nummernvergabe, EN-16931-Steuerkategorien, offene Posten) — und anschließend PDF/Briefpapier über einen Worker-Tier. Die E-Rechnung-Engine (XRechnung, ZUGFeRD, KoSIT-Validierung, Empfang) ist der strategische Compliance-Kern und landet früh. Offene Posten & Mahnwesen schließen den Geld-Kreislauf. Danach kommen erweiterte Rechnungstypen (Fremdwährung, Serien, Abschlag), der CRM-Ausbau samt Steuerberater-Zugang und zum Abschluss der v1-Feinschliff (PWA, Feature-Gates, DSGVO/GoBD-Compliance-Pass).

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Plattform-Kern** - Multi-Tenant-Fundament: Auth, RLS-Isolation, Geldtyp, Audit-Log, Tarif-Gates, i18n, PWA-Shell ✓ 2026-07-10
- [x] **Phase 2: Stammdaten** - Kunden/Lieferanten (Stammdaten, Historie, Notizen) und Produktkatalog ✓ 2026-07-12
- [ ] **Phase 3: Belegkette & Rechnungskern** - Angebot→Rechnung-Kette, Unveränderbarkeit, Nummernvergabe, USt-Kategorien, offene Posten
- [ ] **Phase 4: PDF & Versand** - Worker-Tier, PDF-Belege mit eigenem Briefpapier, E-Mail-Versand
- [ ] **Phase 5: E-Rechnung-Engine** - XRechnung + ZUGFeRD erzeugen, KoSIT-Validierung, E-Rechnungen empfangen/anzeigen
- [ ] **Phase 6: Offene Posten & Mahnwesen** - Zahlungserfassung, OP-Zuordnung, mehrstufige Mahnungen
- [ ] **Phase 7: Erweiterte Rechnungstypen** - Fremdwährung, Serienrechnungen, Abschlags-/Schlussrechnungen
- [ ] **Phase 8: CRM-Ausbau & Steuerberater-Zugang** - Aufgaben/Erinnerungen, Kundenakte, Steuerberater-Rolle (lesend)
- [ ] **Phase 9: v1-Feinschliff & Compliance** - PWA-Installation/Offline, DSGVO-Export, GoBD-Konformitätspass, Tarif-Gate-UX

## Phase Details

### Phase 1: Plattform-Kern
**Goal**: Ein Multi-Tenant-Fundament, auf dem jede finanzrelevante Funktion sicher und rechtskonform aufsetzen kann — alle nicht-nachrüstbaren Weichen (Mandanten-Isolation, exakte Geldarithmetik, Audit) sind gestellt.
**Depends on**: Nothing (first phase)
**Requirements**: PLAT-01, PLAT-02, PLAT-04, PLAT-05, PLAT-06, PLAT-07, PLAT-09
**Success Criteria** (what must be TRUE):
  1. Nutzer kann sich registrieren, eine Firma (Mandant) anlegen, sich anmelden und bleibt über Browser-Sitzungen angemeldet
  2. Daten sind strikt pro Mandant isoliert — ein automatisierter Cross-Tenant-Test weist nach, dass kein Zugriff über Mandantengrenzen möglich ist
  3. Jede finanzrelevante Änderung landet in einem unveränderbaren Audit-Log (Wer/Was/Wann), das nicht editiert werden kann
  4. Geldbeträge werden systemweit exakt (decimal/numeric, nie float) gerechnet, belegt durch Golden-File-Tests zur EN-16931-Rundung
  5. Nutzer kann die UI zwischen Deutsch und Englisch umschalten; die App ist als PWA auf Desktop und Smartphone installierbar
  6. Tarif-Feature-Gates (S/M/L/XL) blenden Funktionen pro Mandant sichtbar ein/aus (ohne Zahlungsabwicklung)
**Plans**: 8 plans

Plans:
- [x] 01-01-PLAN.md — Solution scaffold + platform tenancy/DB kernel (interceptor) + inert ledger + docker
- [x] 01-02-PLAN.md — Tenant/Membership model + RLS migration (enable/force/policies) + least-privilege DB roles
- [x] 01-03-PLAN.md — Money value object + EN-16931 per-category rounding (TDD golden files)
- [x] 01-04-PLAN.md — Immutable append-only audit log (REVOKE + trigger + RLS) + synchronous writer
- [x] 01-05-PLAN.md — Tier entitlements (S/M/L/XL plan→capability map + FeatureManagement filter)
- [x] 01-06-PLAN.md — Keycloak BFF auth + registration (org→tenant mirror) + tenant middleware + /me endpoints
- [x] 01-07-PLAN.md — React 19 PWA shell + DE/EN i18n + BFF cookie API client
- [x] 01-08-PLAN.md — Testcontainers cross-tenant/audit-immutability suites + CI gate + human-verify

### Phase 2: Stammdaten
**Goal**: Nutzer kann seine Geschäftspartner und sein Leistungsangebot pflegen — die Datenbasis, aus der sich später jeder Beleg speist.
**Depends on**: Phase 1
**Requirements**: CRM-01, CRM-02, CRM-03, CATL-01, CATL-02
**Success Criteria** (what must be TRUE):
  1. Nutzer kann Kunden und Lieferanten mit Stammdaten (Anschrift, USt-ID, Zahlungsbedingungen, Kontakte) anlegen, bearbeiten und archivieren
  2. Nutzer sieht pro Kunde/Lieferant eine Historie der zugehörigen Belege und Aktivitäten
  3. Nutzer kann Notizen an Kunden/Lieferanten anheften
  4. Nutzer kann Standardprodukte und -services mit Preis, Einheit und USt-Satz verwalten und als Positionen in Belege übernehmen
**Plans**: 7 plans

Plans:
- [x] 02-01-PLAN.md — EF Core 10 named query filters (Tenant + NotArchived) + IArchivable marker in Platform.Db
- [x] 02-02-PLAN.md — Numera.Modules.Crm (BusinessPartner + contacts/notes/activities) + _Crm migration with per-table RLS + RLS/VAT-ID tests
- [x] 02-03-PLAN.md — Numera.Modules.Catalog (CatalogItem + UN/ECE Rec 20 units) + _Catalog migration with RLS + per-tenant unique article-number + RLS tests
- [x] 02-04-PLAN.md — Partner backend API (CRUD + archive + contacts + notes + activity timeline) + FluentValidation (CRM-01/02/03)
- [x] 02-05-PLAN.md — Catalog backend API (CRUD + archive + CatalogLineItem picker seam) (CATL-01/02)
- [x] 02-06-PLAN.md — Frontend UI stack (Tailwind v4/shadcn/TanStack Table/RHF/zod) + partner list/form/detail + partners i18n
- [x] 02-07-PLAN.md — Catalog frontend (list + form with UN/ECE unit dropdown) + catalog i18n

### Phase 3: Belegkette & Rechnungskern
**Goal**: Nutzer kann rechtskonforme, unveränderbare Rechnungen mit korrekter USt-Behandlung erzeugen — das Herz von v1, inklusive der zweiten „jetzt oder nie"-Naht (Unveränderbarkeit + Nummernvergabe).
**Depends on**: Phase 2
**Requirements**: DOCS-01, DOCS-04, INV-01, INV-02, INV-03, INV-04, OPDN-01
**Success Criteria** (what must be TRUE):
  1. Nutzer kann Angebote erstellen und über Auftragsbestätigung und Lieferschein in Rechnungen überführen (Belegkette mit Statusverfolgung)
  2. Entwürfe sind frei bearbeitbar; finalisierte Belege sind unveränderbar — Korrekturen erzeugen Storno-/Korrekturbelege (GoBD, DB-seitig erzwungen)
  3. Rechnungen enthalten alle Pflichtangaben nach §14 UStG; Rechnungsnummern werden bei Finalisierung race-sicher, eindeutig und im konfigurierten Format vergeben
  4. USt wird als EN-16931-Kategorie modelliert und deckt 19/7/0 %, Kleinunternehmer §19, Reverse-Charge §13b und innergemeinschaftliche Lieferung mit korrekten Pflichttexten ab
  5. Nutzer kann finalisierte Rechnungen stornieren und Gutschriften erstellen; jede finalisierte Rechnung erzeugt einen offenen Posten mit Fälligkeit in der OP-Übersicht
**Plans**: 10 plans

Plans:
- [ ] 03-01-PLAN.md — Sales module + company_profile table + migration#1 + RLS + GET/PUT company-profile API
- [ ] 03-02-PLAN.md — sales_documents schema (6 tables) + migration#2 + immutability triggers + RLS test
- [ ] 03-03-PLAN.md — VatCalculationService (TDD, EN-16931 BG-23 + Pflichttexte)
- [ ] 03-04-PLAN.md — sales-document HTTP surface: draft CRUD + convert + list/detail + OP list endpoint
- [ ] 03-05-PLAN.md — finalize transaction (numbering + snapshots + breakdown + open item + audit + event)
- [ ] 03-06-PLAN.md — Storno (384) + Gutschrift (381) correction documents
- [ ] 03-07-PLAN.md — document frontend: list + RHF/zod draft editor with catalog-picker line array
- [ ] 03-08-PLAN.md — integration tests: finalize side-effects, concurrent numbering, immutability, Storno
- [ ] 03-09-PLAN.md — document detail page + finalize/storno/gutschrift/convert action bar (frontend)
- [ ] 03-10-PLAN.md — OP-Übersicht page + company-profile settings form (frontend)

### Phase 4: PDF & Versand
**Goal**: Nutzer kann Belege als professionelle PDFs im eigenen Layout erzeugen und direkt versenden — Rendering läuft asynchron, damit die Finalisierung schnell bleibt.
**Depends on**: Phase 3
**Requirements**: DOCS-02, DOCS-03
**Success Criteria** (what must be TRUE):
  1. Nutzer kann Belege als PDF mit eigenem Logo/Briefpapier erzeugen, in deutschem und englischem Layout
  2. Nutzer kann einen Beleg direkt per E-Mail an den Kunden versenden (mit PDF-Anhang)
  3. PDF-Erzeugung läuft über einen Worker-Tier und blockiert die Finalisierung nicht
**Plans**: TBD

Plans:
- [ ] 04-01: TBD (verfeinert in plan-phase)

### Phase 5: E-Rechnung-Engine
**Goal**: Nutzer kann gesetzlich verpflichtende E-Rechnungen EN-16931-konform erzeugen, validieren und empfangen — der strategische Compliance-Kern von Numera.
**Depends on**: Phase 4
**Requirements**: EINV-01, EINV-02, EINV-03, EINV-04, EINV-05
**Success Criteria** (what must be TRUE):
  1. Nutzer kann eine Rechnung als XRechnung (UBL und CII, EN 16931) erzeugen und per Download oder E-Mail übermitteln
  2. Nutzer kann eine Rechnung als ZUGFeRD (PDF/A-3 mit eingebettetem XML) erzeugen; PDF- und XML-Werte stimmen exakt überein
  3. Jede ausgehende E-Rechnung wird vor Finalisierung gegen den KoSIT-Validator geprüft; Fehler blockieren den Versand und werden verständlich erklärt
  4. Nutzer kann empfangene E-Rechnungen (XRechnung/ZUGFeRD) hochladen, validieren und menschenlesbar anzeigen
  5. Empfangene E-Rechnungen werden als Eingangsbelege dem Lieferanten zugeordnet und abgelegt
**Plans**: TBD

Plans:
- [ ] 05-01: TBD (verfeinert in plan-phase)

### Phase 6: Offene Posten & Mahnwesen
**Goal**: Nutzer kann den Geld-Kreislauf schließen — von der offenen Forderung über die erfasste Zahlung bis zur mehrstufigen Mahnung.
**Depends on**: Phase 5
**Requirements**: OPDN-02, OPDN-03
**Success Criteria** (what must be TRUE):
  1. Nutzer kann Zahlungen manuell erfassen und offenen Posten zuordnen; Teilzahlungen aktualisieren den Zahlungsstatus korrekt
  2. Nutzer kann Zahlungserinnerungen und mehrstufige Mahnungen mit konfigurierbaren Stufen, Fristen und Gebühren erzeugen und versenden
  3. Fällige offene Posten werden für Mahnläufe erkannt und in der OP-Übersicht sichtbar priorisiert
**Plans**: TBD

Plans:
- [ ] 06-01: TBD (verfeinert in plan-phase)

### Phase 7: Erweiterte Rechnungstypen
**Goal**: Nutzer kann über die Standardrechnung hinausgehende, praxisrelevante Rechnungstypen erstellen — Fremdwährung, wiederkehrende Rechnungen und Abschlagslogik.
**Depends on**: Phase 6
**Requirements**: INV-05, INV-06, INV-07
**Success Criteria** (what must be TRUE):
  1. Nutzer kann Rechnungen in Fremdwährung erstellen; der USt-Ausweis erfolgt korrekt in EUR-Umrechnung
  2. Nutzer kann Serienrechnungen anlegen, die automatisch nach Zeitplan erzeugt werden
  3. Nutzer kann Abschlags- und Schlussrechnungen erstellen; geleistete Anzahlungen werden in der Schlussrechnung korrekt verrechnet
**Plans**: TBD

Plans:
- [ ] 07-01: TBD (verfeinert in plan-phase)

### Phase 8: CRM-Ausbau & Steuerberater-Zugang
**Goal**: Nutzer kann Kundenbeziehungen aktiv managen und seinen Steuerberater kontrolliert einbinden — die letzten funktionalen v1-Bausteine der Lexware-Feature-Matrix.
**Depends on**: Phase 7
**Requirements**: CRM-04, CRM-05, PLAT-03
**Success Criteria** (what must be TRUE):
  1. Nutzer kann Aufgaben mit Erinnerungen/Fälligkeiten zu Kunden anlegen und verfolgen
  2. Nutzer kann Dateien am Kunden ablegen (Kundenakte)
  3. Nutzer kann Teammitglieder einladen und Rollen vergeben (Inhaber, Mitarbeiter, Steuerberater)
  4. Ein Steuerberater-Nutzer hat lesenden Zugriff auf Belege und Auswertungen, kann aber nichts verändern
**Plans**: TBD

Plans:
- [ ] 08-01: TBD (verfeinert in plan-phase)

### Phase 9: v1-Feinschliff & Compliance
**Goal**: Numera ist launch-reif — PWA, Tarif-Gates und Compliance-Artefakte sind poliert und nachweislich rechtskonform.
**Depends on**: Phase 8
**Requirements**: PLAT-08
**Success Criteria** (what must be TRUE):
  1. Nutzer kann alle Daten seines Mandanten exportieren (DSGVO-Portabilität)
  2. PWA-Installation und Offline-Entwurfsverhalten funktionieren zuverlässig; Finalisierung bleibt online-gebunden
  3. Tarif-Feature-Gates sind durchgängig mit klarer UX umgesetzt (Upgrade-Hinweise statt Fehler)
  4. Ein GoBD-Konformitätspass ist abgeschlossen: Verfahrensdokumentation als Entwurf vorhanden, „GoBD-konform"-Formulierungen geprüft (kein „zertifiziert"-Claim), Cross-Tenant-Sicherheitssuite grün
**Plans**: TBD

Plans:
- [ ] 09-01: TBD (verfeinert in plan-phase)

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Plattform-Kern | 8/8 | ✓ Complete | 2026-07-10 |
| 2. Stammdaten | 7/7 | ✓ Complete | 2026-07-12 |
| 3. Belegkette & Rechnungskern | 0/TBD | Next | - |
| 4. PDF & Versand | 0/TBD | Not started | - |
| 5. E-Rechnung-Engine | 0/TBD | Not started | - |
| 6. Offene Posten & Mahnwesen | 0/TBD | Not started | - |
| 7. Erweiterte Rechnungstypen | 0/TBD | Not started | - |
| 8. CRM-Ausbau & Steuerberater-Zugang | 0/TBD | Not started | - |
| 9. v1-Feinschliff & Compliance | 0/TBD | Not started | - |
