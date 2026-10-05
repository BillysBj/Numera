# Soll-Ist-Abgleich: UG-Anforderungen vs. Numera

**Grundlage:** Steuerliche/buchhalterische Klärung NOEMA Essentials UG (Stand 05.10.2026)
**Zweck:** Was erfüllt Numera bereits, was fehlt, was ist nur Konfiguration, was muss extern laufen.
**Hinweis:** Keine Steuerberatung. Code-Stand geprüft am 05.10.2026.

---

## Kernbefund

Das Papier geht davon aus, Numera sei eine „reine EÜR-Software". **Das stimmt nicht.**
Unter der Oberfläche existiert bereits eine **echte doppelte Buchführung**:

- Kontenklassen Aktiva / Passiva / Eigenkapital / Ertrag / Aufwand (`AccountType`)
- Soll/Haben-Buchungssätze (`JournalEntry` + `Posting`)
- **SKR03 und SKR04** inkl. Automatikkonten, Steuerschlüssel, USt-VA-Kennziffer je Konto
- Festschreibung, Stornobuchung, Fiskalperioden, lückenlose Journalnummer
- Umschalter **EÜR ↔ Bilanz** (`Gewinnermittlungsart`) und **Soll/Ist** (`Besteuerungsart`)
- §19 Kleinunternehmer wird **auf Unternehmensebene** in der Buchungslogik behandelt (nicht als Kundenschalter)
- **Getrennte** Felder Steuernummer und USt-IdNr. im Firmenstamm

Die EÜR ist nur eine *Auswertung* auf diesem Ledger. Der doppische Unterbau ist also vorhanden.

**Was fehlt, ist nicht die Buchführung, sondern die Jahresabschluss-Schicht** (Bilanz/GuV-Ausgabe, Anlagevermögen/AfA, Rückstellungen/RAP, Abschlussbuchungen, UG-Rücklage, E-Bilanz) sowie die Steuererklärungen (KSt/GewSt) und die Offenlegung.

---

## A) Bereits erfüllt ✅

| Doc-Punkt | Numera |
|---|---|
| Doppelte Buchführung (Konten, Soll/Haben, Debitor/Kreditor) | ✅ Ledger-Modul vorhanden |
| SKR03 **und** SKR04 | ✅ beide, umschaltbar |
| Steuerschlüssel / BU-Schlüssel | ✅ `Steuerschluessel`, nur Standard-BU exportiert |
| USt-VA Kennziffern-Ermittlung (Prüfansicht) | ✅ entspricht genau dem empfohlenen Prozess „Software → Prüfansicht → Mein ELSTER" |
| §19 auf **Unternehmensebene** | ✅ `isKleinunternehmer` in der Buchungslogik, nicht Kundenschalter |
| Soll-/Ist-Versteuerung als Einstellung | ✅ `Besteuerungsart` |
| Reverse Charge pro Geschäftsvorfall (nicht Kundenschalter) | ✅ über Steuerkategorie/TaxCategory, nicht pauschal je Kunde |
| Steuernummer **und** USt-IdNr. getrennt | ✅ getrennte Felder + Finanzamt/Bundesland |
| Fortlaufende Rechnungsnummer, Storno, Gutschrift | ✅ |
| E-Rechnung XRechnung/ZUGFeRD + **XML-Archivierung** | ✅ strukturierte Originaldaten werden gespeichert, nicht nur PDF |
| GoBD: Unveränderbarkeit, Änderungsprotokoll, Festschreibung, Storno statt Überschreiben | ✅ |
| DATEV-Export mit Soll/Haben/Gegenkonto/Steuerschlüssel/Belegdaten | ✅ (Feld-Abnahme siehe C) |

---

## B) Fehlt – Jahresabschluss-Ebene 🔴 (das eigentliche To-do)

| Doc-Punkt | Status | Bemerkung |
|---|---|---|
| **Bilanz-Auswertung** | 🔴 | Ledger liefert die Daten, aber es gibt keinen Bilanz-Report |
| **GuV-Auswertung** | 🔴 | dito |
| **Anlagevermögen / Anlagenverzeichnis** | 🔴 | kein Asset-Modul |
| **AfA / Abschreibungen** | 🔴 | keine AfA-Berechnung/-Buchung |
| **Anlagenspiegel** | 🔴 | für E-Bilanz nötig |
| **Rückstellungen** | 🔴 | kein Konto-/Buchungsworkflow |
| **Rechnungsabgrenzung (RAP)** | 🔴 | — |
| **Abschlussbuchungen / Jahresüberträge** | 🔴 | kein Abschluss-Workflow |
| **Gesetzliche UG-Rücklage §5a GmbHG (25 %)** | 🔴 | keine Eigenkapitalposition/Logik |
| **E-Bilanz (XBRL via ELSTER/ERiC)** | 🔴 | DATEV-Export ersetzt sie **nicht** |
| **Körperschaftsteuererklärung** | 🔴 | separater ELSTER-Vorgang |
| **Gewerbesteuererklärung** | 🔴 | separater ELSTER-Vorgang (kein 24.500 €-Freibetrag für UG) |
| **Umsatzsteuer-Jahreserklärung** | 🔴 | nur USt-VA vorhanden |
| **Offenlegung/Hinterlegung Unternehmensregister** | 🔴 | separater Vorgang |

---

## C) Nur Konfiguration / Abnahme 🟡

| Punkt | Aktion |
|---|---|
| Kontenrahmen für UG | Tenant auf **SKR04** stellen (empfohlen); SKR03 ist nicht falsch, aber SKR04 passt zur Bilanzierung. Default ist aktuell SKR03 |
| Gewinnermittlungsart | Tenant auf **Bilanz** stellen (`Gewinnermittlungsart = Bilanz`), damit EÜR nicht als offizielle Methode gilt |
| Soll/Ist | Auf **Soll** lassen, bis eine Ist-Genehmigung des Finanzamts vorliegt |
| USt-Status | **Regelbesteuerung vs. §19** anhand der steuerlichen Registrierung eintragen |
| DATEV-Mapping-Abnahme | Geschäftsvorfall → Konto → Steuerschlüssel → USt-Kennziffer gegen SKR04 und aktuelle DATEV-Logik einmal prüfen (Testexport) |
| Produkt-Steuersätze | Tatsächliche NOEMA-Produkte einmal steuerlich klassifizieren (19/7/steuerfrei/Sonderfälle) |
| Steuerschlüssel jahresabhängig versionierbar | Prüfen/härten: Konten & Steuerschlüssel nicht dauerhaft hartkodieren, sondern jahresbezogen (wie bei EÜR-Zeilen bereits umgesetzt) |

---

## D) Strategische Empfehlung

Die laufende Buchführung (A) ist stark und weitgehend vollständig. Die Lücken (B) sind **Jahresabschluss-Themen**. Zwei realistische Wege:

**Weg 1 – Numera als Buchführung + Abschluss extern (empfohlen, pragmatisch):**
Numera bleibt das führende System für Buchungen, Belege, USt-VA und **DATEV-Export**. Jahresabschluss, Anlagenbuchhaltung/AfA, E-Bilanz, KSt/GewSt und Offenlegung laufen über dafür spezialisierte Software **oder** einen Steuerberater, gefüttert aus dem Numera-DATEV-Export. (Das Papier lässt das ausdrücklich zu: „mit geeigneter Buchhaltungs-/Abschlusssoftware".)
→ Kleinster Aufwand, geringstes Fehlerrisiko bei den rechtlich heiklen Pflichtteilen.

**Weg 2 – Numera zum Vollsystem ausbauen:**
Die Punkte aus B schrittweise in Numera bauen. Sinnvoll und machbar in Numera: **Anlagenverzeichnis + AfA**, **Bilanz/GuV-Auswertung**, **UG-Rücklage-Position**, **einfache Abschlussbuchungen**. Weniger sinnvoll in Numera selbst (Aufwand/Risiko hoch): **E-Bilanz-XBRL via ERiC**, **KSt-/GewSt-Formulare** – diese sind besser über Mein ELSTER / Spezialsoftware.

**Mein Vorschlag:** Weg 1 jetzt (go-live-fähig), und aus B gezielt **AfA/Anlagenverzeichnis + Bilanz/GuV-Ansicht + UG-Rücklage** als Numera-Ausbaustufe einplanen. E-Bilanz/KSt/GewSt über ELSTER/Spezialsoftware.

---

## E) Sofort erledigbar (ohne Programmierung)
1. Tenant NOEMA: **Gewinnermittlungsart = Bilanz**, **Kontenrahmen = SKR04**, **Besteuerung = Soll** (bis Ist genehmigt).
2. USt-Status (Regelbesteuerung vs. §19) eintragen.
3. Produktliste steuerlich klassifizieren (19/7/steuerfrei).
4. DATEV-Testexport ziehen und Konten-/Steuerschlüssel-Mapping abnehmen lassen.
