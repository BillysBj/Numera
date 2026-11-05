# Rechtstexte — Arbeitsstand (A1)

> ⚠️ **ENTWURF.** Alle Dokumente hier sind eine strukturierte **Zuarbeit für den Fachanwalt /
> Datenschützer**, keine rechtlich geprüften Texte. Vor dem öffentlichen Verkauf muss eine
> fachkundige Person sie prüfen und finalisieren. Platzhalter in `[…]` sind durch die echten
> Betreiber-Angaben zu ersetzen.

## Was wo liegt

| Dokument | Ort | Status |
|---|---|---|
| Impressum | App: `web/src/pages/legal/LegalPages.tsx` (`/impressum`) | Entwurf, in der App gerendert |
| Datenschutzerklärung | App: `web/src/pages/legal/LegalPages.tsx` (`/datenschutz`) | Entwurf, in der App gerendert |
| AGB (B2B) | App: `web/src/pages/legal/LegalPages.tsx` (`/agb`) | Entwurf, in der App gerendert |
| **AV-Vertrag (Art. 28)** | [`avv-auftragsverarbeitung.md`](avv-auftragsverarbeitung.md) | Entwurf (nur Dokument, keine App-Seite) |
| **TOMs** | [`toms.md`](toms.md) | Entwurf, aus dem Code abgeleitet |
| Datenfluss-/Subprozessor-Zuarbeit | [`../datenschutz-avv-zuarbeit.md`](../datenschutz-avv-zuarbeit.md) | Zuarbeit für den Anwalt |

Impressum/Datenschutz/AGB werden bewusst **direkt in der App** als Entwurf gepflegt (eine Quelle,
sofort sichtbar unter `/impressum`, `/datenschutz`, `/agb`). Der Anwalt kann den Text dort prüfen;
die finale Fassung wird in `LegalPages.tsx` eingesetzt.

## Versionierung der Zustimmung (A2)

Die Registrierung erzwingt die Zustimmung zur **aktuellen** Fassung und speichert sie
nachweisbar (Mandant: `legal_accepted_version` + `legal_accepted_at`).

- Frontend-Konstante: `web/src/lib/legal.ts` → `LEGAL_VERSION`
- Backend-Konstante: `src/Numera.Api/Legal/LegalDocuments.cs` → `CurrentVersion`

**Bei jeder materiellen Textänderung beide Konstanten (ISO-Datum) hochsetzen.** Dann ist belegbar,
welcher Nutzer welche Fassung akzeptiert hat; eine erneute Zustimmung kann künftig erzwungen werden.

## Checkliste bis „verkaufsfertig" (A1)

- [ ] Betreiber-Stammdaten einsetzen: Anschrift, Geschäftsführer:in, Handelsregister/HRB, USt-IdNr., Kontakt.
- [ ] Zuständige Datenschutz-Aufsichtsbehörde eintragen.
- [ ] Oracle-Cloud-Region (EU/Frankfurt?) bestätigen → Drittlandkapitel final.
- [ ] Subprozessor-Liste + deren AVV/SCC bestätigen (Stripe, finAPI, SMTP-Anbieter, Oracle).
- [ ] Backup-Verschlüsselung + Aufbewahrung in den TOMs bestätigen.
- [ ] Preise/Abrechnungsmodell in den AGB konkretisieren (sobald Stripe-Live-Preise stehen).
- [ ] Haftungs- und Verfügbarkeits-/SLA-Klauseln anwaltlich prüfen.
- [ ] Alles durch Fachanwalt/Datenschützer freigeben, dann `LEGAL_VERSION` final setzen.
