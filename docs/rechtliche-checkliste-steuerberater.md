# Numera – Rechtliche/Steuerliche Checkliste

**Unternehmen:** NOEMA Essentials UG (haftungsbeschränkt)
**Zweck:** Abgrenzung – was Numera abdeckt vs. was zwingend über Steuerberater/ELSTER läuft.
**Stand:** 2026-10-05
**Hinweis:** Diese Checkliste wurde durch ein KI-Assistenzsystem erstellt. Sie ist **keine Steuer- oder Rechtsberatung** und ersetzt nicht die Prüfung durch einen Steuerberater/Wirtschaftsprüfer.

---

## Legende
- ✅ **In Numera umgesetzt & geprüft** – technisch vorhanden, gegen maßgebliche Quelle validiert
- 🟡 **In Numera vorhanden, Freigabe durch Steuerberater nötig** – Logik gebaut, fachlich noch nicht abgenommen
- 🔴 **Nicht von Numera abgedeckt** – muss extern (Steuerberater/ELSTER) erfolgen

---

## 1. Ausgangsrechnungen / E-Rechnung

| Punkt | Status | Anmerkung |
|---|---|---|
| EN 16931 / XRechnung 3.0 (UBL + CII) | ✅ | Gegen offiziellen **KoSIT-Validator** geprüft (Konformitätsfälle grün) |
| ZUGFeRD PDF/A-3 (eingebettete XML) | ✅ | Hybrid-PDF wird erzeugt und mit versandt |
| §14 UStG Pflichtangaben | ✅ | Aussteller/Empfänger, fortlaufende Nummer, Datum, Leistungsdatum, Steueraufstellung werden erzwungen |
| §19 UStG Kleinunternehmer-Hinweis | ✅ | Wird gesetzt, wenn Tenant als Kleinunternehmer konfiguriert |
| §13b Reverse-Charge-Hinweis | ✅ | Hinweistext + Steuerkategorie |
| §35a GmbHG (GF, HRB, Registergericht) | ✅ | Auf Rechnung/Geschäftsbrief |
| Fortlaufende, lückenlose Nummernvergabe | ✅ | Atomar, keine Lücken/Doppelungen |
| **Prüfpunkt Steuerberater** | 🟡 | Stammdaten korrekt? (USt-IdNr/Steuernummer, Steuersätze je Artikel, §13b-/§19-Einordnung je Kunde) |

---

## 2. Eingangsbelege / Betriebsausgaben

| Punkt | Status | Anmerkung |
|---|---|---|
| Erfassung Eingangsrechnungen + Belege | ✅ | Upload, OCR/Metadaten, Zuordnung |
| Zahlungserfassung (Abflussprinzip) | ✅ | Lieferantenzahlung triggert Aufwandserfassung |
| Konten-/Kategorie-Zuordnung | 🟡 | Automatik vorhanden – **Kontenrahmen-Feinzuordnung vom Steuerberater prüfen lassen** |
| AfA / Abschreibungen Anlagevermögen | 🔴 | **Nicht umgesetzt** – AfA-Berechnung/Anlagenspiegel extern |

---

## 3. Umsatzsteuer-Voranmeldung (USt-VA)

| Punkt | Status | Anmerkung |
|---|---|---|
| Kennziffern-Berechnung (Prüfansicht) | 🟡 | Hilft beim Abgleich; Soll-/Ist-Versteuerung berücksichtigt |
| Vorsteuer aus Eingangsbelegen | 🟡 | Fließt in Kennziffern ein |
| **ELSTER-Übermittlung (ERiC/XML)** | 🔴 | **XML nicht gegen offizielles Schema validiert, nicht eingereicht.** Abgabe erfolgt extern über ELSTER/Steuerberater |

---

## 4. Gewinnermittlung / Jahresabschluss ⚠️ **wichtigster Punkt für eine UG**

| Punkt | Status | Anmerkung |
|---|---|---|
| EÜR (Anlage EÜR) | 🟡 | Zeilen 2024/2025 korrigiert, Kassenprinzip, USt-neutral über Zeile 58 |
| **Richtige Methode für eine UG?** | 🔴 | Eine **UG ist Kapitalgesellschaft → bilanzierungspflichtig (§238 HGB)**. EÜR ist **nicht** die zulässige Methode. Numera erzeugt **keine Bilanz**. |
| **E-Bilanz** | 🔴 | Nicht von Numera – extern (Steuerberater) |
| **Körperschaftsteuer (KSt)** | 🔴 | Nicht von Numera |
| **Gewerbesteuer (GewSt)** | 🔴 | Nicht von Numera |
| **Offenlegung (Unternehmensregister)** | 🔴 | Nicht von Numera |
| Gesetzliche Rücklage UG (25 % Thesaurierung, §5a GmbHG) | 🔴 | Nicht von Numera – im Jahresabschluss zu beachten |

> **Kernaussage:** Numera liefert die **laufenden Daten** (Rechnungen, Belege, USt-Zahlen, DATEV-Export) als Grundlage. Der **Jahresabschluss einer UG (Bilanz, E-Bilanz, KSt, GewSt, Offenlegung) muss durch einen Steuerberater** erstellt werden.

---

## 5. DATEV-Export

| Punkt | Status | Anmerkung |
|---|---|---|
| EXTF Buchungsstapel | ✅ | Export vorhanden |
| BU-Schlüssel (Inlandsfälle) | ✅ | Gegen DATEV-Tabelle geprüft; nur Standard-BU-Schlüssel |
| Konten-/SKR-Zuordnung final | 🟡 | **Vom Steuerberater gegen verwendeten Kontenrahmen abstimmen** |

---

## 6. GoBD / Aufbewahrung

| Punkt | Status | Anmerkung |
|---|---|---|
| Unveränderbarkeit finalisierter Belege | ✅ | DB-Trigger, Festschreibung |
| Lückenlose Nummerierung | ✅ | — |
| Audit-Log / Protokollierung | ✅ | — |
| Aufbewahrung/Backups | ✅ | Backups eingerichtet |
| **GoBD-Verfahrensdokumentation** | 🟡 | Liegt als **Entwurf** vor – muss vom Betreiber ausgefüllt und vom Steuerberater **freigegeben** werden |
| „GoBD-Zertifizierung" | ℹ️ | Gibt es nicht; wird korrekt **nicht** behauptet. System ist „nach GoBD-Grundsätzen gebaut" |

---

## 7. Datenschutz (nur interne Nutzung)

| Punkt | Status | Anmerkung |
|---|---|---|
| Mandantentrennung (RLS) | ✅ | — |
| DSGVO-Datenexport | ✅ | — |
| Impressum/Datenschutzerklärung | ℹ️ | Bei rein **interner** Nutzung nachrangig; sobald öffentlich/verkauft → juristisch prüfen (die Landingpage ist öffentlich erreichbar) |

---

## Konkrete Fragen an den Steuerberater

1. **UG & EÜR:** Bestätigen, dass die UG bilanzieren muss. Wer erstellt Bilanz/E-Bilanz/KSt/GewSt? (Numera macht das nicht.)
2. **Kontenrahmen:** Welcher SKR? Konten-/BU-Zuordnung im DATEV-Export abnehmen.
3. **USt-VA:** Abgabe über Steuerberater oder ELSTER direkt? Soll- oder Ist-Versteuerung bestätigt?
4. **Stammdaten:** USt-IdNr/Steuernummer, Steuersätze, §13b-/§19-Einordnung je Kunde prüfen.
5. **AfA/Anlagevermögen:** Wie werden Abschreibungen geführt (extern)?
6. **GoBD-Verfahrensdokumentation:** Entwurf prüfen und freigeben.
7. **Gesetzliche Rücklage UG** (25 %): im Jahresabschluss berücksichtigen.

---

### Zusammenfassung in einem Satz
Das **Rechnungs-/E-Rechnungs-Fundament ist rechtskonform gebaut und gegen den KoSIT-Validator geprüft**; die **Jahresabschluss-, Körperschaftsteuer- und ELSTER-Ebene einer UG deckt Numera nicht ab** und gehört zwingend in die Hand eines Steuerberaters.
