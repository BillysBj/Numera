# Roadmap: Numera

## Milestones

- ✅ **v1.0 Rechnungen & E-Rechnung** — Phasen 1–9 (shipped 2026-08-02) → [Archiv](milestones/v1.0-ROADMAP.md)
- 🚧 **v2.0 Buchhaltung, Banking & Belege** — Phasen 10–14 (in Arbeit)

## Phases

<details>
<summary>✅ v1.0 Rechnungen & E-Rechnung (Phasen 1–9) — SHIPPED 2026-08-02</summary>

Full phase details are archived in [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md).

- [x] Phase 1: Plattform-Kern (8/8) — 2026-07-10
- [x] Phase 2: Stammdaten (7/7) — 2026-07-12
- [x] Phase 3: Belegkette & Rechnungskern (11/11) — 2026-07-13
- [x] Phase 4: PDF & Versand (5/5) — 2026-07-14
- [x] Phase 5: E-Rechnung-Engine (5/5) — 2026-07-14
- [x] Phase 6: Offene Posten & Mahnwesen (5/5) — 2026-07-28
- [x] Phase 7: Erweiterte Rechnungstypen (9/9) — 2026-07-28
- [x] Phase 8: CRM-Ausbau & Steuerberater-Zugang (5/5) — 2026-07-28
- [x] Phase 9: v1-Feinschliff & Compliance (6/6) — 2026-08-02

</details>

### 🚧 v2.0 Buchhaltung, Banking & Belege (Phasen 10–14)

- [x] **Phase 10: Buchhaltungs-Fundament** — SKR03/04-Kontenrahmen, doppische unveränderbare Buchungs-Engine, Steuerschlüssel-Mapping, Auto-Buchung aus Rechnungen & Zahlungen, Journal/Konto-Sichten, Periodensperre — ✓ 2026-08-03
- [ ] **Phase 11: Berichte & USt-Voranmeldung** — EÜR + USt-Voranmeldung (Kennziffern-Berechnung + ELSTER-XML/Druck-Export, kein ERiC-Direktversand)
- [ ] **Phase 12: Belege & Ausgaben** — Belegscan (Kamera/Upload) + OCR + Buchungsvorschlag, strukturierte E-Rechnungs-Lesung, GoBD-Langzeitarchiv, Lieferanten-Match, E-Mail-Eingang
- [ ] **Phase 13: Banking & Zahlungsabgleich** — finAPI-Anbindung, Umsatz-Sync, automatischer Abgleich mit offenen Posten (+ Buchung), manuelle Zuordnung/Split, CSV/CAMT-Fallback
- [ ] **Phase 14: Monetarisierung (Stripe)** — Stripe Checkout, Webhook-Berechtigung → `tenants.plan`-Gate, Customer-Portal-Self-Service, Trial, Abo-Mahnwesen/Degradation, USt-korrekte Eigen-Abrechnung

## Phase Details

### Phase 10: Buchhaltungs-Fundament
**Goal**: Numera bekommt ein echtes doppisches Buchungs-Fundament — jede finalisierte Rechnung und jede Zahlung wird automatisch, unveränderbar und ausgeglichen verbucht. Das Fundament, aus dem sich alle Berichte und die USt-VA speisen.
**Depends on**: Phase 9 (v1: Rechnungen, offene Posten, Zahlungen, EN-16931-USt-Kategorien, GoBD-Muster)
**Requirements**: ACCT-01, ACCT-02, ACCT-03, ACCT-04, ACCT-05, ACCT-06, ACCT-09
**Success Criteria**:
1. Nutzer wählt beim Setup SKR03 oder SKR04; ein mandantenspezifischer aktiver Kontensatz existiert (RLS-isoliert).
2. Eine finalisierte Rechnung erzeugt automatisch eine ausgeglichene Soll/Haben-Buchung mit korrektem Konto + Steuerschlüssel; eine erfasste Zahlung bucht Bank↔Forderung und schließt den offenen Posten; eine Eingangsrechnung/Ausgabe bucht Aufwand + Vorsteuer.
3. Gebuchte Journalzeilen sind DB-seitig unveränderbar (REVOKE + Trigger, ausgeglichen erzwungen); eine Korrektur entsteht nur als Storno-Buchung.
4. Nutzer sieht Buchungsjournal und Kontoauszug je Konto; eine festgeschriebene (gesperrte) Periode nimmt keine neuen Buchungen mehr an.

**Plans:** 6 plans (5 waves) — ✓ all complete 2026-08-03
- [x] 10-01-PLAN.md — Ledger-Schema-Fundament + GoBD-DB-Enforcement (RLS, Unveränderbarkeit, Balance-, Periodensperre-Trigger) [ACCT-02]
- [x] 10-02-PLAN.md — SKR03/04-Seed + Setup-Endpoint (mandantenspezifischer aktiver Kontensatz) [ACCT-01]
- [x] 10-03-PLAN.md — Buchungs-Engine: AccountResolver + IPostingSource (Rechnung/Zahlung/Ausgabe) + Golden-File-Tests [ACCT-02, ACCT-05]
- [x] 10-04-PLAN.md — Auto-Buchung Ausgangsrechnung (inline in FinalizeCore) + Storno-Generalumkehr [ACCT-03]
- [x] 10-05-PLAN.md — Auto-Buchung Zahlung (Bank↔Forderung inline in PaymentService) + Reversal [ACCT-04]
- [x] 10-06-PLAN.md — Journal- + Kontoauszug-Sichten + Festschreibung (lückenlose Journalnummern, Periodensperre) [ACCT-06, ACCT-09]

### Phase 11: Berichte & USt-Voranmeldung
**Goal**: Nutzer kann seine steuerlichen Pflichtauswertungen erzeugen — die EÜR und die USt-Voranmeldung — direkt und korrekt aus den Buchungen.
**Depends on**: Phase 10
**Requirements**: ACCT-07, ACCT-08
**Success Criteria**:
1. Nutzer erzeugt für einen Zeitraum eine EÜR in der Struktur der Anlage EÜR (Zufluss/Abfluss), die den Buchungen entspricht.
2. Nutzer erzeugt eine USt-Voranmeldung; die Kennziffern (81/86/35/66/83/41/89/46/47/61) werden korrekt aus den Buchungen berechnet und in einer Prüfansicht angezeigt.
3. Die USt-VA lässt sich als ELSTER-konformes XML und als Druck exportieren (für den manuellen ELSTER-Upload); ein Direktversand via ERiC erfolgt bewusst nicht.

### Phase 12: Belege & Ausgaben
**Goal**: „Belege fotografieren, sie buchen sich selbst" — Eingangsbelege werden erfasst, per OCR ausgelesen, geprüft gebucht und GoBD-konform revisionssicher archiviert.
**Depends on**: Phase 10 (Buchungs-Engine für die Ausgabenbuchung); v1 (PWA-Kamera, Lieferanten-CRM, E-Rechnungs-Parser, GoBD-Muster)
**Requirements**: BELEG-01, BELEG-02, BELEG-03, BELEG-04, BELEG-05, BELEG-06, BELEG-07
**Success Criteria**:
1. Nutzer erfasst einen Beleg per Kamera oder Upload; OCR schlägt Lieferant/Datum/Beträge/USt vor; eine bereits empfangene E-Rechnung (XRechnung/ZUGFeRD) wird ohne OCR strukturiert gelesen.
2. Nutzer bestätigt einen Buchungsvorschlag (Aufwandskonto + Vorsteuer-Steuerschlüssel); die Ausgabe wird gebucht und fließt in EÜR/USt-VA (Vorsteuer) — nie ohne Prüfung.
3. Das Originaldokument liegt unveränderbar (WORM) im GoBD-Archiv, indiziert, mit der Buchung verknüpft, 10 Jahre aufbewahrt.
4. An die mandanteneigene E-Mail-Eingangsadresse weitergeleitete Belege werden automatisch erfasst; der Lieferant wird zugeordnet (v1-CRM), Dubletten werden erkannt.

### Phase 13: Banking & Zahlungsabgleich
**Goal**: Numera trifft die Realität — echte Kontoumsätze über finAPI, automatisch abgeglichen mit den offenen Posten und ins Journal gebucht.
**Depends on**: Phase 10 (Buchung Bank↔Forderung/Verbindlichkeit); v1 (offene Posten, Zahlungslogik, CRM-Stammdaten)
**Requirements**: BANK-01, BANK-02, BANK-03, BANK-04, BANK-05, BANK-06
**Success Criteria**:
1. Nutzer verbindet ein Bankkonto über finAPI (Web Form/SCA); Kontoumsätze werden regelmäßig, idempotent und dublettenfrei synchronisiert; der PSD2-Consent-Ablauf (Re-Auth) wird gehandhabt.
2. Eingehende Umsätze werden offenen Posten automatisch zugeordnet (Betrag + Referenz + Gegenpartei) mit Konfidenz-Score; niedrige Konfidenz landet in einer Prüf-Queue.
3. Eine bestätigte Zuordnung erfasst die Zahlung und verbucht Bank↔Forderung/Verbindlichkeit; der offene Posten wird geschlossen (nutzt v1-Zahlungslogik).
4. Nutzer kann eine Transaktion manuell zuordnen, korrigieren oder splitten; der CSV/MT940/CAMT-Import funktioniert als Fallback.

### Phase 14: Monetarisierung (Stripe)
**Goal**: Die Tarife S/M/L/XL werden echt verkaufbar — Self-Service-Abo über Stripe, das serverautoritative Gate wird von Stripe-Webhooks getrieben. (Unabhängiger Track — kann bei Bedarf vorgezogen/parallelisiert werden.)
**Depends on**: v1 (serverautoritatives `tenants.plan`-Gate + Upgrade-Hinweis-UX). Unabhängig von den Buchhaltungs-Phasen.
**Requirements**: BILL-01, BILL-02, BILL-03, BILL-04, BILL-05, BILL-06, BILL-07
**Success Criteria**:
1. Nutzer startet einen bezahlten Tarif über Stripe Checkout (gehostet); ein signierter, idempotenter Webhook setzt die Berechtigung und schaltet die Features frei (serverautoritativ, kein Re-Login nötig).
2. Nutzer wechselt im Stripe Customer Portal den Tarif (Upgrade/Downgrade mit Proration); das Gate reagiert; Zahlungsmethode und Abo-Rechnungen sind einsehbar.
3. Eine Testphase gewährt vollen Zugriff und stellt danach automatisch auf Bezahlung um; eine fehlgeschlagene Zahlung führt zu Smart-Retries und geordneter Degradation.
4. Numera rechnet das eigene Abo USt-korrekt ab (inkl. Reverse-Charge für EU-B2B).

## Progress

**Execution Order:** v2.0 baut in numerischer Reihenfolge auf: 10 → 11 → 12 → 13 → 14. Phase 14 (Monetarisierung) ist unabhängig und kann parallel/vorgezogen werden.

| Phase | Milestone | Plans | Status | Completed |
|-------|-----------|-------|--------|-----------|
| 1.–9. (v1.0) | v1.0 | 61/61 | ✓ Complete | 2026-08-02 |
| 10. Buchhaltungs-Fundament | v2.0 | 6/6 | ✓ Complete | 2026-08-03 |
| 11. Berichte & USt-Voranmeldung | v2.0 | 0/? | Next | - |
| 12. Belege & Ausgaben | v2.0 | 0/? | Pending | - |
| 13. Banking & Zahlungsabgleich | v2.0 | 0/? | Pending | - |
| 14. Monetarisierung (Stripe) | v2.0 | 0/? | Pending | - |
