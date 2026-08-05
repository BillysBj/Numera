4. Nutzer kann eine Transaktion manuell zuordnen, korrigieren oder splitten; der CSV/MT940/CAMT-Import funktioniert als Fallback.

**Plans:** 7 plans (5 waves)
- [ ] 13-01-PLAN.md — Banking-Modul: Entities + eine RLS-Migration (dedupe-Index) + Ports + Stub-Provider (Fundament)
- [ ] 13-02-PLAN.md — Reconciliation-Scoring (Betrag + Verwendungszweck + Gegenpartei) + Konfidenz-Tier (BANK-03)
- [ ] 13-03-PLAN.md — Datei-Import CSV + MT940 + CAMT.053 hinter einem Importer-Port (BANK-06)
- [ ] 13-04-PLAN.md — Idempotenter, dublettenfreier Sync: Hangfire-Worker-Job + Ingest/Dedupe (BANK-02)
- [ ] 13-05-PLAN.md — finAPI-Live (Sandbox) hinter dem Port: Web Form 2.0 + Consent/Re-Auth + verschlüsselte Creds (BANK-01)
- [ ] 13-06-PLAN.md — Endpoints: Connect/Import + Prüf-Queue + Confirm→PaymentService-Buchung + Split/Un-Match (BANK-04, BANK-05)
- [ ] 13-07-PLAN.md — Banking-Frontend + Human-Verify-Checkpoint (connect→sync→reconcile→confirm→book)
