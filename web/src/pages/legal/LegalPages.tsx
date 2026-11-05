import { Link } from 'react-router-dom'
import { LEGAL_VERSION } from '@/lib/legal'

// Public legal pages (A2): Impressum, Datenschutzerklärung, AGB — reachable without login,
// linked from the Landing footer and the registration form.
//
// ⚠️ ENTWURF: The texts below are a structured STARTING POINT for the operator's lawyer, not
// legally reviewed copy. Operator-specific details are marked with […] placeholders. Replace
// the content with the final, lawyer-approved version before any public sale, and keep
// LEGAL_VERSION (web/src/lib/legal.ts) + LegalDocuments.CurrentVersion (backend) in sync.

interface Section {
  heading: string
  body: string[]
}
interface LegalDoc {
  title: string
  intro?: string
  sections: Section[]
}

// --- Operator identity (single source for all three documents) ----------------------------
const OPERATOR = {
  company: 'NOEMA Essentials UG (haftungsbeschränkt)',
  street: 'Schäftlarnstraße 72',
  city: '81371 München',
  country: 'Deutschland',
  managingDirector: 'Vasilios Petropoulos',
  register: 'Amtsgericht München, HRB 312487',
  vatId: 'DE463689885 (gemäß § 27a UStG)',
  email: 'info@noema-essentials.com',
  phone: '+49 176 99234953',
}

const impressum: LegalDoc = {
  title: 'Impressum',
  intro: 'Angaben gemäß § 5 Digitale-Dienste-Gesetz (DDG) und § 18 Abs. 2 MStV.',
  sections: [
    {
      heading: 'Diensteanbieter',
      body: [
        `${OPERATOR.company}`,
        `${OPERATOR.street}`,
        `${OPERATOR.city}, ${OPERATOR.country}`,
      ],
    },
    {
      heading: 'Vertretung',
      body: [`Vertretungsberechtigte:r Geschäftsführer:in: ${OPERATOR.managingDirector}`],
    },
    {
      heading: 'Registereintrag',
      body: [`Eintragung im Handelsregister: ${OPERATOR.register}`],
    },
    {
      heading: 'Umsatzsteuer-Identifikationsnummer',
      body: [OPERATOR.vatId],
    },
    {
      heading: 'Kontakt',
      body: [`E-Mail: ${OPERATOR.email}`, `Telefon: ${OPERATOR.phone}`],
    },
    {
      heading: 'Verantwortlich i. S. d. § 18 Abs. 2 MStV',
      body: [`${OPERATOR.managingDirector}, Anschrift wie oben.`],
    },
    {
      heading: 'EU-Streitschlichtung',
      body: [
        'Die Europäische Kommission stellt eine Plattform zur Online-Streitbeilegung (OS) bereit: https://ec.europa.eu/consumers/odr/.',
        'Wir sind nicht verpflichtet und nicht bereit, an Streitbeilegungsverfahren vor einer Verbraucherschlichtungsstelle teilzunehmen (Numera richtet sich an Unternehmer, § 14 UStG / B2B).',
      ],
    },
  ],
}

const datenschutz: LegalDoc = {
  title: 'Datenschutzerklärung',
  intro:
    'Diese Erklärung informiert über die Verarbeitung personenbezogener Daten bei Nutzung von Numera (gehostete SaaS). Verantwortlicher ist der unten genannte Anbieter.',
  sections: [
    {
      heading: '1. Verantwortlicher',
      body: [
        `${OPERATOR.company}, ${OPERATOR.street}, ${OPERATOR.city}.`,
        `E-Mail: ${OPERATOR.email}. Einen Datenschutzbeauftragten haben wir nicht bestellt; eine gesetzliche Bestellpflicht nach § 38 BDSG besteht derzeit nicht.`,
      ],
    },
    {
      heading: '2. Rollen: Verantwortlicher und Auftragsverarbeiter',
      body: [
        'Für die eigenen Stammdaten der Nutzer (Registrierung, Abrechnung, Support) sind wir Verantwortlicher.',
        'Für die Inhaltsdaten, die ein Kunde in seinem Mandanten verarbeitet (z. B. Daten seiner Geschäftspartner), handeln wir als Auftragsverarbeiter nach Art. 28 DSGVO auf Grundlage des separaten Auftragsverarbeitungsvertrags (AVV).',
      ],
    },
    {
      heading: '3. Zwecke und Rechtsgrundlagen',
      body: [
        'Bereitstellung des Dienstes und Vertragserfüllung (Art. 6 Abs. 1 lit. b DSGVO).',
        'Erfüllung gesetzlicher Aufbewahrungs- und Buchführungspflichten, insbesondere GoBD, §§ 147 AO, 257 HGB (Art. 6 Abs. 1 lit. c DSGVO).',
        'Betriebssicherheit, Missbrauchs- und Angriffsabwehr sowie Produktverbesserung (Art. 6 Abs. 1 lit. f DSGVO).',
      ],
    },
    {
      heading: '4. Empfänger und Auftragsverarbeiter (Subunternehmer)',
      body: [
        'Oracle Cloud Infrastructure – Hosting/Infrastruktur, Region Frankfurt (eu-frankfurt-1), EU.',
        'Stripe Payments Europe, Ltd. – Zahlungsabwicklung/Abonnement (nur bei kostenpflichtiger Nutzung).',
        'finAPI GmbH – Kontoanbindung (Banking), sofern der Kunde die Funktion nutzt.',
        'ALL-INKL.COM – Neue Medien Münnich (SMTP-/E-Mail-Dienstleister) – Versand von System- und Rechnungs-E-Mails.',
        'KoSIT-Validator – Prüfung von E-Rechnungen (Betrieb als Sidecar; keine dauerhafte Speicherung).',
        'Mit allen Auftragsverarbeitern bestehen Verträge nach Art. 28 DSGVO. Eine aktuelle Liste stellen wir auf Anfrage bereit.',
      ],
    },
    {
      heading: '5. Drittlandübermittlung',
      body: [
        'Eine Übermittlung in Drittländer findet nur statt, soweit ein Subunternehmer dies erfordert, und dann auf Grundlage geeigneter Garantien (z. B. EU-Standardvertragsklauseln). [Im Einzelnen prüfen.]',
      ],
    },
    {
      heading: '6. Speicherdauer',
      body: [
        'Personenbezogene Daten werden gelöscht, sobald sie für die Zwecke nicht mehr erforderlich sind und keine gesetzliche Aufbewahrungspflicht entgegensteht.',
        'Buchführungsrelevante Belege unterliegen gesetzlichen Aufbewahrungsfristen (i. d. R. 10 Jahre). Für Geschäftspartner, deren Belege aufbewahrungspflichtig sind, erfolgt statt Löschung eine Anonymisierung der Stammdaten (Art. 17 DSGVO i. V. m. Aufbewahrungspflichten).',
        'Bei Konto-Löschung werden die Mandantendaten nach einer Übergangs-/Wiederherstellungsfrist von 30 Tagen endgültig und unwiderruflich gelöscht.',
      ],
    },
    {
      heading: '7. Betroffenenrechte',
      body: [
        'Sie haben das Recht auf Auskunft (Art. 15), Berichtigung (Art. 16), Löschung (Art. 17), Einschränkung (Art. 18), Datenübertragbarkeit (Art. 20) und Widerspruch (Art. 21).',
        'Numera stellt dafür einen vollständigen Datenexport (Art. 20) sowie eine Konto-Löschung bereit.',
        'Sie haben zudem das Recht auf Beschwerde bei einer Aufsichtsbehörde (Art. 77). Für uns zuständig ist das Bayerische Landesamt für Datenschutzaufsicht (BayLDA), Promenade 18, 91522 Ansbach.',
      ],
    },
    {
      heading: '8. Technische und organisatorische Maßnahmen',
      body: [
        'Mandantentrennung per Row-Level-Security, verschlüsselte Übertragung (TLS), Zugriffskontrolle, unveränderbares Audit-Log, regelmäßige Backups. Details siehe TOM-Dokument (Bestandteil des AVV).',
      ],
    },
    {
      heading: '9. Cookies',
      body: [
        'Numera verwendet ausschließlich technisch notwendige Cookies (Sitzungs-/Authentifizierungscookie). Es werden keine Tracking- oder Marketing-Cookies gesetzt; eine Einwilligung ist hierfür nicht erforderlich.',
      ],
    },
  ],
}

const agb: LegalDoc = {
  title: 'Allgemeine Geschäftsbedingungen (AGB)',
  intro:
    'Diese AGB gelten für die Nutzung von Numera durch Unternehmer i. S. d. § 14 BGB (B2B). Ein Vertragsschluss mit Verbrauchern ist nicht vorgesehen.',
  sections: [
    {
      heading: '§ 1 Geltungsbereich',
      body: [
        `Anbieter ist ${OPERATOR.company} (nachfolgend „Anbieter"). Diese AGB gelten für alle Verträge über die Nutzung der SaaS-Anwendung Numera.`,
        'Abweichenden Bedingungen des Kunden wird widersprochen; sie werden nur bei ausdrücklicher schriftlicher Zustimmung Vertragsbestandteil.',
      ],
    },
    {
      heading: '§ 2 Leistungsgegenstand',
      body: [
        'Der Anbieter stellt Numera als über das Internet nutzbare Software (Software-as-a-Service) im jeweils gebuchten Tarif bereit. Der Funktionsumfang ergibt sich aus der Tarifbeschreibung.',
        'Der Anbieter schuldet die Bereitstellung, nicht einen bestimmten wirtschaftlichen oder steuerlichen Erfolg. Numera ersetzt keine steuerliche oder rechtliche Beratung.',
      ],
    },
    {
      heading: '§ 3 Registrierung und Vertragsschluss',
      body: [
        'Mit der Registrierung gibt der Kunde ein Angebot auf Abschluss eines Nutzungsvertrags ab. Der Vertrag kommt mit Freischaltung des Zugangs zustande.',
        'Der Kunde ist für die Richtigkeit seiner Angaben sowie die Geheimhaltung seiner Zugangsdaten verantwortlich.',
      ],
    },
    {
      heading: '§ 4 Testphase, Preise und Zahlung',
      body: [
        'Sofern angeboten, kann eine kostenlose Testphase genutzt werden. Nach deren Ablauf richtet sich die Nutzung nach dem gebuchten, kostenpflichtigen Tarif.',
        'Preise ergeben sich aus der zum Zeitpunkt der Buchung gültigen Preisliste. Die Abrechnung erfolgt über den Zahlungsdienstleister Stripe. [Preise, Abrechnungszeitraum, Fälligkeit konkretisieren.]',
      ],
    },
    {
      heading: '§ 5 Verfügbarkeit',
      body: [
        'Der Anbieter bemüht sich um eine hohe Verfügbarkeit, schuldet jedoch keine ununterbrochene Verfügbarkeit. Wartungsfenster und Störungen außerhalb des Einflussbereichs des Anbieters sind von der Verfügbarkeit ausgenommen. [Ggf. konkretes SLA ergänzen.]',
      ],
    },
    {
      heading: '§ 6 Pflichten des Kunden',
      body: [
        'Der Kunde nutzt Numera nur im Rahmen der gesetzlichen Vorschriften und stellt sicher, dass er zur Verarbeitung der von ihm eingegebenen Daten berechtigt ist.',
        'Für die Verarbeitung personenbezogener Daten Dritter im Mandanten gilt ergänzend der Auftragsverarbeitungsvertrag (AVV).',
      ],
    },
    {
      heading: '§ 7 Datenschutz und Datensicherheit',
      body: [
        'Der Anbieter verarbeitet personenbezogene Daten nach Maßgabe der Datenschutzerklärung und des AVV. Backups und Mandantentrennung sind Bestandteil des Betriebs.',
      ],
    },
    {
      heading: '§ 8 Laufzeit und Kündigung',
      body: [
        'Der Vertrag läuft auf unbestimmte Zeit und kann vom Kunden jederzeit zum Ende des laufenden Abrechnungszeitraums gekündigt werden. [Fristen je Tarif konkretisieren.]',
        'Nach Vertragsende kann der Kunde seine Daten exportieren; danach werden die Daten nach einer Frist von 30 Tagen gelöscht, soweit keine gesetzliche Aufbewahrungspflicht entgegensteht.',
      ],
    },
    {
      heading: '§ 9 Haftung',
      body: [
        'Der Anbieter haftet unbeschränkt bei Vorsatz und grober Fahrlässigkeit sowie nach dem Produkthaftungsgesetz und bei Verletzung von Leben, Körper oder Gesundheit.',
        'Bei leicht fahrlässiger Verletzung wesentlicher Vertragspflichten (Kardinalpflichten) ist die Haftung auf den vertragstypischen, vorhersehbaren Schaden begrenzt. Im Übrigen ist die Haftung ausgeschlossen. [Anwaltlich prüfen.]',
        'Der Kunde ist für regelmäßige eigene Datensicherungen im Rahmen des Export-Features mitverantwortlich.',
      ],
    },
    {
      heading: '§ 10 Schlussbestimmungen',
      body: [
        'Es gilt das Recht der Bundesrepublik Deutschland. Gerichtsstand ist, soweit zulässig, der Sitz des Anbieters.',
        'Sollten einzelne Bestimmungen unwirksam sein, bleibt der Vertrag im Übrigen wirksam.',
      ],
    },
  ],
}

const DOCS: Record<string, LegalDoc> = { impressum, datenschutz, agb }

function LegalChrome({ docKey }: { docKey: keyof typeof DOCS | string }) {
  const doc = DOCS[docKey]
  if (!doc) return null
  return (
    <div className="min-h-dvh bg-background">
      <header className="border-b border-border/70">
        <div className="mx-auto flex h-16 max-w-3xl items-center justify-between px-5 sm:px-6">
          <Link to="/" className="text-[1.15rem] font-bold tracking-tight text-foreground">
            Numera
          </Link>
          <Link to="/login" className="text-sm font-semibold text-primary hover:underline">
            Zur Anmeldung
          </Link>
        </div>
      </header>

      <main className="mx-auto max-w-3xl px-5 py-10 sm:px-6">
        <div className="mb-6 rounded-lg border border-warning/40 bg-warning/5 p-3 text-sm text-warning">
          ⚠️ Entwurf — rechtlich noch nicht geprüft. Vor dem öffentlichen Verkauf durch eine
          fachkundige Person (Anwalt/Datenschutz) finalisieren. Platzhalter in […] ersetzen.
        </div>

        <h1 className="text-3xl font-bold tracking-tight text-foreground">{doc.title}</h1>
        {doc.intro && <p className="mt-3 text-muted-foreground">{doc.intro}</p>}

        <div className="mt-8 flex flex-col gap-7">
          {doc.sections.map((s) => (
            <section key={s.heading}>
              <h2 className="text-lg font-semibold text-foreground">{s.heading}</h2>
              <div className="mt-2 flex flex-col gap-2">
                {s.body.map((p, i) => (
                  <p key={i} className="text-sm leading-relaxed text-muted-foreground">
                    {p}
                  </p>
                ))}
              </div>
            </section>
          ))}
        </div>

        <footer className="mt-12 flex flex-wrap gap-x-5 gap-y-2 border-t border-border pt-6 text-sm">
          <Link to="/impressum" className="text-muted-foreground hover:text-foreground">Impressum</Link>
          <Link to="/datenschutz" className="text-muted-foreground hover:text-foreground">Datenschutz</Link>
          <Link to="/agb" className="text-muted-foreground hover:text-foreground">AGB</Link>
          <span className="ml-auto text-xs text-muted-foreground/70">Fassung {LEGAL_VERSION}</span>
        </footer>
      </main>
    </div>
  )
}

export function ImpressumPage() {
  return <LegalChrome docKey="impressum" />
}
export function DatenschutzPage() {
  return <LegalChrome docKey="datenschutz" />
}
export function AgbPage() {
  return <LegalChrome docKey="agb" />
}
