import { type ReactNode } from 'react'

// Benutzerhandbuch — a self-contained, in-app user manual (German). Static content
// plus screenshots served from /handbuch. One page with an anchor table of contents.
// Deliberately framework-light: plain sections + Tailwind, no data fetching.

interface Section {
  id: string
  title: string
}

const SECTIONS: Section[] = [
  { id: 'einstieg', title: '1. Einstieg & Überblick' },
  { id: 'stammdaten', title: '2. Stammdaten einrichten' },
  { id: 'rechnungen', title: '3. Rechnungen & Belegkette' },
  { id: 'erechnung', title: '4. E-Rechnung (XRechnung & ZUGFeRD)' },
  { id: 'offene-posten', title: '5. Offene Posten & Zahlungen' },
  { id: 'mahnwesen', title: '6. Mahnwesen' },
  { id: 'serien', title: '7. Serienrechnungen' },
  { id: 'banking', title: '8. Banking' },
  { id: 'belege', title: '9. Eingangsbelege (Belege erfassen)' },
  { id: 'berichte', title: '10. Berichte (USt-VA & EÜR)' },
  { id: 'team', title: '11. Team & Nutzer' },
  { id: 'compliance', title: '12. GoBD, Backup & Datenschutz' },
]

function Shot({ src, alt }: { src: string; alt: string }) {
  return (
    <figure className="my-4 overflow-hidden rounded-lg border border-border bg-card">
      <img src={src} alt={alt} loading="lazy" className="block w-full" />
      <figcaption className="border-t border-border px-3 py-2 text-xs text-muted-foreground">
        {alt}
      </figcaption>
    </figure>
  )
}

function H({ id, children }: { id: string; children: ReactNode }) {
  return (
    <h2
      id={id}
      className="mt-10 scroll-mt-24 border-b border-border pb-2 text-xl font-semibold text-foreground"
    >
      {children}
    </h2>
  )
}

function H3({ children }: { children: ReactNode }) {
  return <h3 className="mt-6 text-base font-semibold text-foreground">{children}</h3>
}

function P({ children }: { children: ReactNode }) {
  return <p className="mt-3 text-sm leading-relaxed text-muted-foreground">{children}</p>
}

function Steps({ children }: { children: ReactNode }) {
  return (
    <ol className="mt-3 list-decimal space-y-1.5 pl-5 text-sm leading-relaxed text-muted-foreground marker:text-muted-foreground/70">
      {children}
    </ol>
  )
}

function Bullets({ children }: { children: ReactNode }) {
  return (
    <ul className="mt-3 list-disc space-y-1.5 pl-5 text-sm leading-relaxed text-muted-foreground marker:text-muted-foreground/70">
      {children}
    </ul>
  )
}

function Note({ children }: { children: ReactNode }) {
  return (
    <div className="mt-4 rounded-lg border border-primary/30 bg-primary/5 px-4 py-3 text-sm leading-relaxed text-foreground">
      {children}
    </div>
  )
}

// Inline UI chip (menu item / button label) for consistent references.
function UI({ children }: { children: ReactNode }) {
  return (
    <span className="rounded border border-border bg-muted px-1.5 py-0.5 text-[0.8em] font-medium text-foreground">
      {children}
    </span>
  )
}

export default function UserManualPage() {
  return (
    <main className="app-main" style={{ maxWidth: '920px' }}>
      <header className="mb-6">
        <h1 className="text-2xl font-semibold text-foreground">Benutzerhandbuch</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          Diese Anleitung führt Sie durch alle Bereiche von Numera — vom Einrichten der
          Stammdaten über die rechtskonforme Rechnung und E-Rechnung bis zu Mahnwesen,
          Banking und den steuerlichen Berichten.
        </p>
      </header>

      {/* Table of contents */}
      <nav className="mb-8 rounded-lg border border-border bg-card p-4">
        <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          Inhalt
        </p>
        <ol className="grid gap-1 sm:grid-cols-2">
          {SECTIONS.map((s) => (
            <li key={s.id}>
              <a
                href={`#${s.id}`}
                className="text-sm text-primary hover:underline"
              >
                {s.title}
              </a>
            </li>
          ))}
        </ol>
      </nav>

      <article className="pb-16">
        {/* 1 */}
        <H id="einstieg">1. Einstieg & Überblick</H>
        <P>
          Numera bündelt Angebote, Rechnungen, die gesetzliche E-Rechnung, offene Posten,
          Mahnwesen, Banking und die steuerlichen Auswertungen an einem Ort — GoBD-konform
          und EN-16931-geprüft. Nach der Anmeldung landen Sie auf der <UI>Übersicht</UI>
          (Dashboard) mit Ihren wichtigsten Kennzahlen.
        </P>
        <Shot src="/handbuch/01-dashboard.jpg" alt="Dashboard: Finanzüberblick mit Umsatz, offenen Posten und Belegstatus" />
        <P>
          Die Navigation links ist nach Aufgaben gruppiert: <UI>Verkauf</UI> (Belege, Offene
          Posten, Mahnwesen, Serienrechnungen, Eingangsbelege), <UI>Belege</UI> (Eingangs­belege
          erfassen und prüfen), <UI>Banking</UI>, <UI>Berichte</UI>, <UI>Stammdaten</UI> und
          <UI>System</UI>. Oben rechts schalten Sie zwischen Deutsch/Englisch und hell/dunkel um.
        </P>
        <Note>
          <strong>Reihenfolge für den Start:</strong> Zuerst das Firmenprofil ausfüllen, dann
          einen Kunden anlegen, optional Artikel hinterlegen — danach können Sie Ihre erste
          Rechnung schreiben.
        </Note>

        {/* 2 */}
        <H id="stammdaten">2. Stammdaten einrichten</H>

        <H3>2.1 Firmenprofil</H3>
        <P>
          Öffnen Sie <UI>System → Einstellungen</UI>. Hinterlegen Sie Ihre gesetzlichen
          Pflichtangaben — diese erscheinen auf jeder Rechnung (§14 UStG):
        </P>
        <Bullets>
          <li>Firmenname (Rechtsform), Anschrift, Kontakt</li>
          <li>USt-IdNr. <em>oder</em> Steuernummer (eines von beiden genügt)</li>
          <li>Bankverbindung (IBAN/BIC) für den Zahlungsteil der Rechnung</li>
          <li>Logo (PNG/JPG, max. 1&nbsp;MB — am besten ein transparentes PNG im Querformat)</li>
          <li>Standard-Zahlungsziel in Tagen</li>
          <li>Schalter <UI>Kleinunternehmer (§19)</UI> — nur aktivieren, wenn Sie keine USt ausweisen</li>
        </Bullets>
        <Shot src="/handbuch/02-firmenprofil.jpg" alt="Einstellungen: Firmenprofil mit gesetzlichen Pflichtangaben und Logo" />

        <H3>2.2 Partner (Kunden & Lieferanten)</H3>
        <P>
          Unter <UI>Stammdaten → Partner</UI> legen Sie Ihre Kunden und Lieferanten an. Für eine
          rechtskonforme Rechnung braucht ein Kunde mindestens Name und vollständige Anschrift;
          die USt-IdNr. ist für EU-Geschäfte wichtig.
        </P>
        <Steps>
          <li>Auf <UI>Partner anlegen</UI> klicken.</li>
          <li>Name, Rechtsform und Rechnungsadresse eingeben.</li>
          <li>Rolle wählen: <UI>Kunde</UI> und/oder <UI>Lieferant</UI>.</li>
          <li>Optional: USt-IdNr., E-Mail (für den Rechnungsversand), Zahlungsziel/Skonto.</li>
          <li>Speichern.</li>
        </Steps>
        <Shot src="/handbuch/03-partner.jpg" alt="Partnerliste mit Kunden und Lieferanten" />

        <H3>2.3 Artikel (Katalog)</H3>
        <P>
          Unter <UI>Stammdaten → Artikel</UI> hinterlegen Sie wiederkehrende Leistungen und
          Produkte mit Name, Einheit, Netto-Preis und Steuersatz. Beim Schreiben einer Rechnung
          übernehmen Sie Positionen dann per Klick aus dem Artikelstamm.
        </P>
        <Shot src="/handbuch/04-artikel.jpg" alt="Artikelstamm mit Leistungen und Produkten" />

        {/* 3 */}
        <H id="rechnungen">3. Rechnungen & Belegkette</H>
        <P>
          Numera bildet die vollständige Belegkette ab: <strong>Angebot → Auftragsbestätigung →
          Lieferschein → Rechnung</strong>. Jeder Beleg kann in den nächsten umgewandelt werden.
          Alles beginnt unter <UI>Verkauf → Belege</UI>.
        </P>

        <H3>3.1 Rechnung anlegen</H3>
        <Steps>
          <li>
            <UI>Verkauf → Belege</UI> öffnen, <UI>Beleg anlegen</UI> klicken, Belegart
            <UI>Rechnung</UI> wählen.
          </li>
          <li>Kunden auswählen (aus den Stammdaten).</li>
          <li>
            Positionen hinzufügen (<UI>Position hinzufügen</UI> oder <UI>Aus Artikelstamm</UI>):
            Bezeichnung, Menge, Einheit, Einzelpreis.
          </li>
          <li>
            Pro Position Steuerkategorie und USt-Satz setzen — im Normalfall
            <UI>S</UI> (Regelsteuersatz) mit <UI>19&nbsp;%</UI> bzw. <UI>7&nbsp;%</UI>.
          </li>
          <li>Optional: Leistungsdatum, Käufer-Referenz, Notizen.</li>
          <li><UI>Speichern</UI> — die Rechnung ist jetzt ein <em>Entwurf</em>.</li>
        </Steps>
        <Shot src="/handbuch/05-beleg-neu.jpg" alt="Neue Rechnung: Kopf, Positionen und Steuersätze" />
        <Note>
          <strong>Steuerkategorie kurz erklärt:</strong> <UI>S</UI> = Regelsteuersatz (19/7&nbsp;%,
          Ihr Normalfall) · <UI>AE</UI> = Reverse-Charge §13b · <UI>K</UI> = innergemeinschaftliche
          Lieferung · <UI>E</UI> = steuerbefreit §4 · <UI>Z</UI> = Nullsatz.
        </Note>

        <H3>3.2 Finalisieren</H3>
        <P>
          Auf der Belegdetailseite klicken Sie auf <UI>Finalisieren</UI>. Dabei wird eine
          endgültige, lückenlose Belegnummer vergeben und die Rechnung gegen den offiziellen
          <strong> KoSIT-Validator</strong> geprüft. Fehlen Pflichtfelder, zeigt Numera genau an,
          was ergänzt werden muss. Nach dem Finalisieren ist die Rechnung
          <strong> unveränderbar</strong> (GoBD).
        </P>
        <Shot src="/handbuch/06-rechnung-detail.jpg" alt="Finalisierte Rechnung mit Aktionen: PDF, XRechnung, ZUGFeRD, Versand" />

        <H3>3.3 Herunterladen & versenden</H3>
        <Bullets>
          <li><UI>PDF herunterladen</UI> — die klassische Sicht-Rechnung (Sprache de/en wählbar).</li>
          <li><UI>ZUGFeRD (PDF/A-3)</UI> — PDF mit eingebettetem XML (Sicht- und E-Rechnung in einem).</li>
          <li><UI>XRechnung (XML)</UI> — das reine XRechnung-UBL-XML.</li>
          <li><UI>Per E-Mail senden</UI> / <UI>E-Rechnung senden</UI> — Versand an die Kunden-E-Mail.</li>
        </Bullets>

        <H3>3.4 Korrigieren: Storno & Gutschrift</H3>
        <P>
          Eine finalisierte Rechnung wird nie gelöscht, sondern korrigiert: <UI>Stornieren</UI>
          erstellt einen Storno-Beleg, <UI>Gutschrift</UI> einen Gutschrift-Entwurf. Ein reiner
          <em> Entwurf</em> lässt sich dagegen über <UI>Entwurf löschen</UI> entfernen.
        </P>

        {/* 4 */}
        <H id="erechnung">4. E-Rechnung (XRechnung & ZUGFeRD)</H>
        <P>
          Seit 2025 gilt die E-Rechnungspflicht im B2B. Numera erzeugt beide gängigen Formate und
          prüft jede Ausgangsrechnung vor dem Versand live gegen den KoSIT-Validator — dieselbe
          Instanz, die definiert, was als XRechnung gültig ist.
        </P>
        <Bullets>
          <li><strong>XRechnung</strong> in UBL und CII (wertidentisch, zwei Syntaxen).</li>
          <li><strong>ZUGFeRD / Factur-X</strong> als PDF/A-3 mit eingebettetem XML.</li>
          <li>
            <strong>Käufer-Referenz (Leitweg-ID, BT-10):</strong> für die XRechnung erforderlich —
            bei einer Rechnung im Feld <UI>Käufer-Referenz</UI> hinterlegen.
          </li>
          <li>
            <strong>Empfang:</strong> Unter <UI>Verkauf → Eingangsbelege</UI> können Sie
            eingehende XRechnung/ZUGFeRD hochladen, validieren, lesbar anzeigen und dem Lieferanten
            zuordnen.
          </li>
        </Bullets>

        {/* 5 */}
        <H id="offene-posten">5. Offene Posten & Zahlungen</H>
        <P>
          Jede finalisierte Rechnung erscheint unter <UI>Verkauf → Offene Posten</UI>. Hier sehen
          Sie Beträge, Fälligkeiten und überfällige Posten. Über <UI>Zahlung erfassen</UI> buchen
          Sie (Teil-)Zahlungen; der Status wird automatisch fortgeschrieben
          (offen → teilweise bezahlt → bezahlt).
        </P>
        <Shot src="/handbuch/07-offene-posten.jpg" alt="Offene Posten mit Kunde, Beträgen und Fälligkeit" />

        {/* 6 */}
        <H id="mahnwesen">6. Mahnwesen</H>
        <P>
          Unter <UI>Verkauf → Mahnwesen</UI> konfigurieren Sie die Mahnstufen (Fristen, Gebühren,
          Verzugszinsen). Ein Mahnlauf erzeugt für überfällige Posten automatisch die passenden
          Mahnungen als PDF.
        </P>
        <Shot src="/handbuch/08-mahnwesen.jpg" alt="Mahnwesen-Konfiguration mit Mahnstufen" />

        {/* 7 */}
        <H id="serien">7. Serienrechnungen</H>
        <P>
          Wiederkehrende Rechnungen (z.&nbsp;B. monatliche Leistungen) hinterlegen Sie unter
          <UI>Verkauf → Serienrechnungen</UI> als Vorlage mit Intervall. Numera erzeugt daraus
          automatisch die fälligen Rechnungen.
        </P>

        {/* 8 */}
        <H id="banking">8. Banking</H>
        <P>
          Unter <UI>Banking → Bankkonten</UI> verbinden Sie ein Konto bzw. importieren Umsätze.
          In <UI>Banking → Zahlungen prüfen</UI> gleichen Sie Bankumsätze mit offenen Posten ab
          und ordnen Zahlungen per Klick den passenden Rechnungen zu.
        </P>
        <Shot src="/handbuch/10-banking.jpg" alt="Bankkonten und Umsätze" />

        {/* 9 */}
        <H id="belege">9. Eingangsbelege (Belege erfassen)</H>
        <P>
          Belege (Rechnungen von Lieferanten, Quittungen) erfassen Sie unter
          <UI>Belege → Beleg erfassen</UI> — per Upload oder Foto. Numera liest die Eckdaten aus
          (OCR). Unter <UI>Belege → Prüfen &amp; Buchen</UI> kontrollieren Sie die erkannten Werte,
          ordnen den Lieferanten zu und übernehmen den Beleg.
        </P>
        <Shot src="/handbuch/11-belege-pruefen.jpg" alt="Belege prüfen & buchen (Eingangsbelege)" />

        {/* 10 */}
        <H id="berichte">10. Berichte (USt-VA & EÜR)</H>
        <P>
          Unter <UI>Berichte</UI> erzeugt Numera die steuerlichen Auswertungen:
        </P>
        <Bullets>
          <li>
            <UI>USt-Voranmeldung</UI> — die Prüfansicht zeigt die Kennziffern Ihrer Umsatzsteuer-
            Voranmeldung, die Sie bei Mein-ELSTER übertragen.
          </li>
          <li>
            <UI>EÜR</UI> — die Einnahmen-Überschuss-Rechnung als Jahresübersicht.
          </li>
        </Bullets>
        <P>
          Wählen Sie Jahr und Zeitraum und exportieren Sie das Ergebnis als PDF; die USt-VA lässt
          sich zusätzlich als ELSTER-XML ausgeben. Werte erscheinen, sobald für den Zeitraum
          gebuchte Belege vorliegen.
        </P>
        <Note>
          Lassen Sie die steuerliche Logik (Kontenzuordnung, USt) vor dem produktiven Einsatz von
          Ihrem Steuerberater freigeben.
        </Note>

        {/* 11 */}
        <H id="team">11. Team & Nutzer</H>
        <P>
          Unter <UI>System → Team</UI> laden Sie weitere Personen ein und vergeben Rollen
          (<UI>Inhaber</UI> / <UI>Mitarbeiter</UI>). Jede Person meldet sich mit eigenem Login an;
          alle Daten bleiben strikt Ihrem Mandanten zugeordnet.
        </P>
        <Shot src="/handbuch/14-team.jpg" alt="Team: Nutzer einladen und Rollen vergeben" />

        {/* 12 */}
        <H id="compliance">12. GoBD, Backup & Datenschutz</H>
        <Bullets>
          <li>
            <strong>Unveränderbarkeit:</strong> Finalisierte Belege sind GoBD-fest; Korrekturen
            entstehen als Storno oder Gutschrift. Ein revisionssicheres Audit-Log protokolliert
            Änderungen.
          </li>
          <li>
            <strong>Mandantentrennung:</strong> Strikte Datentrennung über Row-Level-Security.
          </li>
          <li>
            <strong>Backups:</strong> Die Datenbank wird automatisch täglich gesichert. Bewahren
            Sie zusätzlich eine Kopie außer Haus auf (Betreiberpflicht).
          </li>
          <li>
            <strong>Datenexport (DSGVO):</strong> Über das Firmenprofil bzw. den Export können Sie
            Ihre Daten jederzeit ausleiten.
          </li>
        </Bullets>

        <p className="mt-10 border-t border-border pt-4 text-xs text-muted-foreground">
          Hinweis: „GoBD-konform" bedeutet, dass Numera nach den GoBD-Grundsätzen gebaut ist. Eine
          „GoBD-Zertifizierung" gibt es nicht und wird nicht behauptet. Für steuerliche
          Einzelfragen wenden Sie sich an Ihren Steuerberater.
        </p>
      </article>
    </main>
  )
}
