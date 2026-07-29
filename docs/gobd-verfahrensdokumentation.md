# Verfahrensdokumentation (GoBD)

> **Status: ENTWURF / DRAFT — nicht freigegeben**
> **Version:** 0.1 (Entwurf)
> **Stand:** 2026-07-29
> **Gegenstand:** Numera — mandantenfähige Auftrags- und Finanzverwaltung
> **Bearbeitungshinweis:** Dies ist ein versionierter Entwurf einer Verfahrensdokumentation
> im Sinne der GoBD (BMF-Schreiben zu den „Grundsätzen zur ordnungsmäßigen Führung und
> Aufbewahrung von Büchern, Aufzeichnungen und Unterlagen in elektronischer Form sowie zum
> Datenzugriff"). Er beschreibt die tatsächlich in Numera implementierten Mechanismen.
> Numera ist **GoBD-konform** ausgelegt; eine externe **Zertifizierung** wird **nicht**
> behauptet — eine Verfahrensdokumentation ist eine begründete Selbstauskunft des
> Unternehmens, keine Zertifizierung.

## Versionshistorie

Die GoBD verlangen, dass Änderungen der Verfahrensdokumentation nachvollziehbar
versioniert werden. Jede materielle Änderung erhält eine neue Versionsnummer mit Datum
und Kurzbegründung.

| Version | Datum      | Änderung                                                        |
| ------- | ---------- | --------------------------------------------------------------- |
| 0.1     | 2026-07-29 | Erstentwurf — 4-teilige Struktur, Bestandsmechanismen erfasst.  |

---

## Teil 1 — Allgemeine Beschreibung

### 1.1 Was ist Numera

Numera ist eine mandantenfähige (multi-tenant) SaaS-Anwendung, mit der ein Unternehmen
seine komplette Auftrags- und Finanzverwaltung an einem Ort führt — von der Rechnung
inklusive der gesetzlichen E-Rechnung bis zur Buchhaltung, rechtskonform nach GoBD und
E-Rechnungspflicht, auf jedem Gerät.

Technisch ist Numera ein modularer .NET-10-Monolith mit einer React-19-Oberfläche und
einer PostgreSQL-Datenbank. Die fachlichen Module sind Platform (Mandanten, Nutzer,
Audit), Catalog (Artikelstamm), Crm (Geschäftspartner, Kontakte, Notizen, Aufgaben,
Kundenakte), Ledger (Konten, Buchungen) und Sales (Belege, E-Rechnung, Zahlungen,
Mahnwesen, wiederkehrende Rechnungen).

### 1.2 Mandantenfähigkeit und Abgrenzung

Jeder Datensatz gehört genau einem Mandanten (Tenant). Die Trennung der Mandantendaten
ist auf Datenbankebene durch **Row-Level Security (RLS)** erzwungen (siehe Teil 3). Diese
Verfahrensdokumentation beschreibt das Verfahren mandantenübergreifend; die konkreten
Stammdaten (Firmenname, Steuernummer/USt-IdNr., Aufbewahrungsverantwortliche) sind
mandantenspezifisch im Firmenprofil (`company_profile`) hinterlegt.

### 1.3 Umfang der aufbewahrten Aufzeichnungen

Numera bewahrt insbesondere folgende steuerlich und handelsrechtlich relevanten
Aufzeichnungen auf:

- **Ausgangsbelege**: finalisierte Rechnungen, Storni, Gutschriften, Abschlags- und
  Schlussrechnungen (`sales_documents` mit Positionen `sales_document_lines`,
  Steueraufteilung `sales_document_tax_breakdown`, Anzahlungsbezug
  `sales_document_prepayment`).
- **Gerenderte Belegbilder**: die menschenlesbaren §14-UStG-PDFs (`document_render`).
- **Strukturierte E-Rechnungen**: XRechnung (UBL/CII) und ZUGFeRD als aufbewahrtes
  Original (`document_einvoice`).
- **Eingangs-E-Rechnungen**: die unveränderten Originalbytes empfangener E-Rechnungen
  (`inbound_document`).
- **Offene Posten, Zahlungen, Mahnungen**: `open_items`, `payment`,
  `payment_allocation`, `dunning_level_config`, `dunning_notice`.
- **Nummernkreise**: `number_sequences`, `document_number_formats`.
- **Nachvollziehbarkeitsdaten**: das unveränderliche Änderungsprotokoll (`audit_events`).
- **Geschäftspartner- und Belegumfeld**: `partners`, `partner_contacts`, `partner_notes`,
  `partner_activities`, `partner_tasks`, `customer_files` (Kundenakte).

---

## Teil 2 — Anwenderdokumentation (Belegweg)

Dieser Teil beschreibt den „Weg eines Belegs" (Belegweg) von der Erstellung über die
Festschreibung und den Versand bis zur Archivierung.

### 2.1 Erfassung (Entwurf)

Ein Anwender erstellt eine Rechnung zunächst als **Entwurf** (Draft). Entwürfe sind frei
editierbar und tragen noch **keine** rechtsverbindliche Belegnummer. In diesem Zustand
kann der Beleg beliebig geändert oder gelöscht werden — er ist noch kein festgeschriebener
Beleg im Sinne der GoBD.

### 2.2 Festschreibung (Finalisierung)

Mit der **Finalisierung** wird der Beleg unveränderlich festgeschrieben. Beim Finalisieren
geschieht in einer einzigen Datenbanktransaktion:

1. **Vergabe einer lückenlosen, laufenden Belegnummer** aus dem mandant- und
   belegartspezifischen Nummernkreis (`number_sequences`). Doppel- oder
   Lückennummerierung ist durch einen **partiellen UNIQUE-Index** auf
   `sales_documents.document_number` technisch ausgeschlossen (siehe Teil 3.2).
2. **Einfrieren der Belegdaten** (Snapshot): Aussteller- und Empfängerdaten sowie die
   Pflichtangaben nach §14 UStG werden als unveränderlicher JSON-Snapshot
   (`IssuerSnapshot`, `RecipientSnapshot`) im Beleg eingefroren. Spätere Stammdaten-
   änderungen wirken sich **nicht** rückwirkend auf festgeschriebene Belege aus.
3. **Erzeugung des Belegbildes** (§14-PDF) und — bei aktiviertem Tarif — der
   strukturierten E-Rechnung (siehe 2.4).
4. **Schreiben eines Audit-Eintrags** (`audit_events`), der die Finalisierung protokolliert.

Nach der Finalisierung ist der Beleg **inhaltlich unveränderlich**. Eine nachträgliche
Korrektur erfolgt **nicht** durch Bearbeiten, sondern ausschließlich durch **Storno** bzw.
**Gutschrift** — also durch einen neuen, ebenfalls festgeschriebenen Gegenbeleg mit eigener
Nummer. Damit bleibt die ursprüngliche Aufzeichnung erhalten und die Änderung ist
nachvollziehbar (GoBD-Grundsatz der Unveränderbarkeit und Nachvollziehbarkeit).

### 2.3 Versand

Ein finalisierter Beleg kann per E-Mail versendet werden. Der Versand erzeugt einen
Versandvermerk (`document_email` mit Status Queued/Sent/Failed) und schreibt den
Versandzeitpunkt in den Beleg. Das versendete PDF entspricht byte-identisch dem
gerenderten und aufbewahrten Belegbild.

### 2.4 E-Rechnung (strukturierter Beleg)

Bei aktivierter E-Rechnung wird aus dem eingefrorenen Snapshot die strukturierte
E-Rechnung erzeugt (XRechnung UBL und CII, sowie ZUGFeRD/Factur-X als hybrides PDF/A-3).
Die erzeugte XML wird vor der Festschreibung gegen den amtlichen **KoSIT-Validator**
(XRechnung 3.0.2) geprüft; eine nicht konforme E-Rechnung wird abgewiesen, **bevor** eine
Belegnummer verbraucht wird, sodass keine Nummer „gestrandet" wird. Die validierte,
strukturierte XML wird als aufbewahrtes Original gespeichert (`document_einvoice`).

### 2.5 Eingangs-E-Rechnungen

Empfangene E-Rechnungen (XRechnung XML oder ZUGFeRD-PDF) werden hochgeladen, formaterkannt,
geparst und gegen KoSIT validiert. Die **Originalbytes** werden dabei **unverändert**
aufbewahrt (`inbound_document`), zusätzlich zu einem menschenlesbaren Lesemodell. Das
Original bleibt das maßgebliche, unveränderliche Dokument (GoBD).

### 2.6 Archivierung

Alle festgeschriebenen Belege, Belegbilder, strukturierten E-Rechnungen und
Eingangsoriginale verbleiben in der PostgreSQL-Datenbank und werden über die
Aufbewahrungsfrist (Teil 4) vorgehalten. Ein DSGVO-Datenexport (Art. 20) steht dem
Mandanten über die Export-Schnittstelle `/api/export` zur Verfügung (siehe 4.5).

---

## Teil 3 — Technische Systemdokumentation

### 3.1 Datenbank und Mandantentrennung (RLS)

Numera nutzt PostgreSQL. Jede mandantenbezogene Tabelle trägt eine **hand-geschriebene
Row-Level-Security-Policy** (`ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` +
`tenant_isolation`-Policy), die den Zugriff auf die Zeilen des aktuell gesetzten Mandanten
(`app.current_tenant`) beschränkt. Die Anwendung verbindet sich als nicht-privilegierte
Rolle `numera_app` (kein `BYPASSRLS`), sodass die Mandantentrennung auch bei einem
Anwendungsfehler datenbankseitig erzwungen bleibt. Die Isolation ist durch eine
Cross-Tenant-Sicherheitssuite auf echtem PostgreSQL nachgewiesen (Tests mit
`IgnoreQueryFilters()`, sodass ausschließlich RLS die Trennung leistet).

### 3.2 Lückenlose, laufende Nummerierung (rennsicher)

Belegnummern werden je Mandant und Belegart aus `number_sequences` vergeben. Eine
Doppelvergabe ist durch einen **partiellen UNIQUE-Index** auf `sales_documents.
document_number` ausgeschlossen (partiell, weil Entwürfe ohne Nummer ausgenommen sind).
Die Nummernvergabe ist gegen Nebenläufigkeit („race") mit einer Zeilensperre abgesichert,
sodass auch bei gleichzeitiger Finalisierung keine Nummer doppelt oder mit Lücke vergeben
wird (GoBD-Grundsatz der Vollständigkeit und Nachvollziehbarkeit).

### 3.3 Schlüssel (UUIDv7)

Primärschlüssel werden als **UUIDv7** anwendungsseitig erzeugt (`Guid.CreateVersion7()`).
UUIDv7 ist zeitsortiert, sodass die Erzeugungsreihenfolge aus dem Schlüssel ersichtlich
bleibt, ohne eine fortlaufende DB-Sequenz offenzulegen.

### 3.4 Unveränderliches Änderungsprotokoll (Append-only Audit-Log)

Das Änderungsprotokoll (`audit_events`) ist **append-only**: Nachträgliche Änderungen und
Löschungen sind datenbankseitig unterbunden (Entzug der UPDATE/DELETE-Rechte per `REVOKE`
zzgl. Trigger-Absicherung) und die Zeilen sind zusätzlich per RLS mandantenisoliert. Damit
sind protokollierte Vorgänge (z. B. Finalisierung, Storno, Zahlungseingang, Export)
nachträglich nicht mehr veränderbar.

### 3.5 Append-only Zahlungen und Zuordnungen

Zahlungen (`payment`) und deren Zuordnungen (`payment_allocation`) sind ebenfalls
**append-only** — ein Datenbank-Trigger weist UPDATE/DELETE ausdrücklich mit dem Hinweis
„append-only (GoBD)" zurück. Eine Zahlung wird nicht bearbeitet oder gelöscht, sondern
durch eine ausgleichende **Gegenbuchung** (negative Zahlung samt negativer Zuordnungen)
storniert; die ursprüngliche Buchung bleibt erhalten.

### 3.6 Unveränderliche Eingangsoriginale

Empfangene E-Rechnungen werden mit ihren **Originalbytes** unverändert gespeichert
(`inbound_document`), zusätzlich zu einem abgeleiteten Lesemodell. Das strukturierte
Original ist das maßgebliche Dokument.

### 3.7 E-Rechnungs-Engine und GoBD-2025

Die E-Rechnung wird aus dem eingefrorenen Snapshot erzeugt (ZUGFeRD-csharp) und amtlich
gegen den **KoSIT-Validator** (XRechnung 3.0.2) geprüft. Die **strukturierte XML** wird als
Aufzeichnung aufbewahrt (`document_einvoice`).

Das **BMF-Schreiben vom 14.07.2025** zur Aktualisierung der GoBD stellt klar, dass bei einer
E-Rechnung die **strukturierte XML das aufzubewahrende Original** ist; eine zusätzliche
PDF-Kopie ist nicht zwingend. Numera erfüllt dies bereits, da die validierte XML als
Original vorgehalten wird; das §14-PDF ist ein zusätzliches, menschenlesbares Belegbild.

### 3.8 Hintergrundverarbeitung (Hangfire)

Rechenintensive oder asynchrone Schritte (PDF-Rendering, E-Rechnungserzeugung,
E-Mail-Versand, wiederkehrende Rechnungen, Mahnungsversand) laufen als Hangfire-Jobs. Jeder
Job stellt in einem frischen Scope explizit den Mandantenkontext her
(`ICurrentTenant.SetTenant`), sodass RLS auch im Hintergrund greift.

---

## Teil 4 — Betriebsdokumentation

### 4.1 Datensicherung (Backups)

Die maßgeblichen Aufzeichnungen liegen vollständig in der PostgreSQL-Datenbank (inklusive
der binären Belegbilder, E-Rechnungs-XML, Eingangsoriginale und Kundenakte-Dateien als
`bytea`). Die Datensicherung erfolgt über das PostgreSQL-Backup des Betriebs
(regelmäßige Sicherung und Wiederherstellungstest). *Betreiberspezifische Details
(Frequenz, Aufbewahrungsort, Restore-Test-Turnus) sind vom Betreiber je Installation zu
ergänzen.*

### 4.2 Aufbewahrungsfrist

Steuerlich relevante Aufzeichnungen und Belege werden über die gesetzliche
Aufbewahrungsfrist von **10 Jahren** gemäß **§147 AO** vorgehalten. Durch die
Append-only-Auslegung (Audit-Log, Zahlungen) und die Unveränderlichkeit festgeschriebener
Belege bleibt die Aufzeichnung über die Frist inhaltlich unverändert erhalten.

### 4.3 Zugriffskontrolle (Rollen)

Der Zugriff ist rollenbasiert. Die Mitgliedschaftsrollen sind **Owner**, **Employee** und
**TaxAdvisor** (Steuerberater, lesend). Eine global wirkende Write-Guard-Middleware
verweigert der Rolle TaxAdvisor jeden schreibenden Zugriff (403); TaxAdvisor erhält
ausschließlich eine definierte Lese-Allow-List. Sensible Aktionen (z. B. Team-Verwaltung,
Datenexport) sind zusätzlich auf die Rolle **Owner** beschränkt.

### 4.4 Änderungsmanagement

Änderungen an der Software erfolgen versioniert über die Versionsverwaltung (Git) mit
Migrationsskripten für Schemaänderungen; jede mandantenbezogene Tabelle erhält in ihrer
Migration eine hand-geschriebene RLS-Policy. Änderungen an dieser Verfahrensdokumentation
werden in der Versionshistorie oben nachgeführt.

### 4.5 Datenauskunft / Datenportabilität (DSGVO Art. 20)

Zur Erfüllung des Rechts auf Datenübertragbarkeit nach **Art. 20 DSGVO** stellt Numera dem
Mandanten einen vollständigen Datenexport über die Schnittstelle **`/api/export`** (GET,
`application/zip`) bereit. Der Export ist **Owner-only**, auf die Capability `DataExport`
(S+) gegated, wird im Audit-Log vermerkt (`data.exported`) und ist **nicht-mutierend**. Er
liefert je Tabelle eine JSON-Datei sowie die realen Binärdateien (PDFs, E-Rechnungs-XML,
Eingangsoriginale, Kundenakte-Dateien, Logo) in einem strukturierten, gängigen und
maschinenlesbaren Format im Sinne von Art. 20 DSGVO.

---

## Hinweis zur Wortwahl (Compliance)

Numera ist **GoBD-konform** ausgelegt und stellt die dafür erforderlichen technischen und
organisatorischen Mechanismen bereit. Numera behauptet **keine** externe **Zertifizierung**
— eine solche wird durch eine Verfahrensdokumentation weder ersetzt noch impliziert. Die
Wortwahl „GoBD-konform" (nie eine Zertifizierungs-Aussage) ist bewusst gewählt und wird
durch einen automatisierten Test (`WordingComplianceTests`) gegen versehentliche
Zertifizierungs-Aussagen abgesichert.
