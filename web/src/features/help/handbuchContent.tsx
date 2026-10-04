import { type ReactNode } from 'react'

// Benutzerhandbuch — central content source. One entry per chapter; the overview
// page (UserManualPage) and the per-chapter subpage (ManualChapterPage) both render
// from CHAPTERS. German, static content; screenshots served from /handbuch.

export function Shot({ src, alt }: { src: string; alt: string }) {
  return (
    <figure className="my-4 overflow-hidden rounded-lg border border-border bg-card">
      <img src={src} alt={alt} loading="lazy" className="block w-full" />
      <figcaption className="border-t border-border px-3 py-2 text-xs text-muted-foreground">
        {alt}
      </figcaption>
    </figure>
  )
}

export function H3({ children }: { children: ReactNode }) {
  return <h3 className="mt-6 text-base font-semibold text-foreground">{children}</h3>
}

export function P({ children }: { children: ReactNode }) {
  return <p className="mt-3 text-sm leading-relaxed text-muted-foreground">{children}</p>
}

export function Steps({ children }: { children: ReactNode }) {
  return (
    <ol className="mt-3 list-decimal space-y-1.5 pl-5 text-sm leading-relaxed text-muted-foreground marker:text-muted-foreground/70">
      {children}
    </ol>
  )
}

export function Bullets({ children }: { children: ReactNode }) {
  return (
    <ul className="mt-3 list-disc space-y-1.5 pl-5 text-sm leading-relaxed text-muted-foreground marker:text-muted-foreground/70">
      {children}
    </ul>
  )
}

export function Note({ children }: { children: ReactNode }) {
  return (
    <div className="mt-4 rounded-lg border border-primary/30 bg-primary/5 px-4 py-3 text-sm leading-relaxed text-foreground">
      {children}
    </div>
  )
}

// Inline UI chip (menu item / button label) for consistent references.
export function UI({ children }: { children: ReactNode }) {
  return (
    <span className="rounded border border-border bg-muted px-1.5 py-0.5 text-[0.8em] font-medium text-foreground">
      {children}
    </span>
  )
}

export interface Chapter {
  slug: string
  /** Chapter number shown in lists and headings. */
  number: number
  title: string
  /** One-line teaser for the overview cards. */
  summary: string
  Body: () => ReactNode
}

export const CHAPTERS: Chapter[] = [
  {
    slug: 'einstieg',
    number: 1,
    title: 'Einstieg & Überblick',
    summary: 'Navigation, Dashboard-Kennzahlen und die empfohlene Reihenfolge beim Start.',
    Body: () => (
      <>
        <P>
          Numera bündelt Angebote, Rechnungen, die gesetzliche E-Rechnung, Offene Posten, Mahnwesen,
          Eingangsbelege, Banking und die steuerlichen Auswertungen an einem Ort — GoBD-konform
          gebaut und EN-16931-geprüft. Nach der Anmeldung landen Sie auf der <UI>Übersicht</UI>
          (Dashboard) mit Ihren wichtigsten Kennzahlen.
        </P>
        <Shot src="/handbuch/01-dashboard.jpg" alt="Dashboard: Finanzüberblick mit Umsatz, offenen Posten und Belegstatus" />

        <H3>Navigation</H3>
        <P>Die Navigation links ist nach Aufgaben gruppiert:</P>
        <Bullets>
          <li><UI>Verkauf</UI> — Belege, Offene Posten, Mahnwesen, Serienrechnungen, Eingangsbelege</li>
          <li><UI>Belege</UI> — Eingangsbelege erfassen sowie prüfen &amp; buchen</li>
          <li><UI>Banking</UI> — Bankkonten und Zahlungsabgleich</li>
          <li><UI>Berichte</UI> — USt-Voranmeldung, EÜR (nur bei Gewinnermittlung EÜR) und DATEV-Export</li>
          <li><UI>Stammdaten</UI> — Partner (Kunden/Lieferanten) und Artikel</li>
          <li><UI>System</UI> — Einstellungen, E-Mail, Kontenrahmen, Team und dieses Handbuch</li>
        </Bullets>
        <P>
          Oben rechts schalten Sie zwischen <UI>DE</UI>/<UI>EN</UI> (Deutsch/Englisch, pro Nutzer)
          und zwischen hellem und dunklem Design um. Ihr aktueller Tarif wird im Dashboard angezeigt.
        </P>

        <H3>Die Kennzahlen im Dashboard</H3>
        <Bullets>
          <li><strong>Umsatz (Jahr):</strong> Netto-Umsatz aus finalisierten Rechnungen des laufenden Jahres.</li>
          <li><strong>Offene Posten / Überfällig:</strong> Summe der noch offenen bzw. bereits überfälligen Forderungen.</li>
          <li><strong>Belege (Monat):</strong> Anzahl der in diesem Monat erstellten Belege.</li>
          <li><strong>Umsatzentwicklung:</strong> Verlauf der letzten zwölf Monate.</li>
          <li><strong>Offene Posten nach Fälligkeit:</strong> nicht fällig, 1–30, 31–60 und über 60 Tage.</li>
          <li><strong>Belege nach Status:</strong> Entwurf, Finalisiert, Bezahlt, Storniert.</li>
        </Bullets>
        <P>
          Solange noch keine Belege vorhanden sind, zeigt das Dashboard einen Hinweis statt
          Dummy-Werten. Numera ist zudem als App installierbar (Progressive Web App) auf Desktop und
          Smartphone.
        </P>

        <Note>
          <strong>Empfohlene Reihenfolge beim Start:</strong> (1) Firmenprofil ausfüllen,
          (2) Kontenrahmen einrichten, (3) ersten Kunden anlegen, (4) optional Artikel hinterlegen,
          (5) optional E-Mail-Versand einrichten — danach schreiben Sie Ihre erste Rechnung.
        </Note>
      </>
    ),
  },
  {
    slug: 'stammdaten',
    number: 2,
    title: 'Stammdaten einrichten',
    summary: 'Firmenprofil (§14-Pflichtangaben), Partner mit Skonto und der Artikelstamm.',
    Body: () => (
      <>
        <H3>Firmenprofil</H3>
        <P>
          Öffnen Sie <UI>System → Einstellungen</UI>. Die hier hinterlegten Angaben werden bei der
          Finalisierung jeder Rechnung <strong>eingefroren</strong> und erscheinen auf dem Beleg
          (§14 UStG). Ohne vollständiges Profil lässt sich keine rechtskonforme Rechnung finalisieren.
        </P>
        <Bullets>
          <li><strong>Firmenname inkl. Rechtsform</strong> und vollständige Anschrift (Straße, ggf. Zusatz, PLZ, Ort, Land, optional Postfach).</li>
          <li><strong>USt-IdNr. und/oder Steuernummer:</strong> Mindestens eines ist Pflicht — beide dürfen angegeben werden. Für die USt-Voranmeldung bei ELSTER wird in der Regel die Steuernummer benötigt.</li>
          <li><strong>Registergericht + Registernummer (HRB)</strong> und <strong>Geschäftsführer:</strong> Bei einer UG/GmbH gesetzlich vorgeschrieben (§35a GmbHG) — erscheinen im Fußbereich des PDFs.</li>
          <li><strong>Bankverbindung</strong> (IBAN/BIC/Bank) für den Zahlungsteil der Rechnung.</li>
          <li><strong>Kontakt-E-Mail und Telefon:</strong> Das Telefon ist optional und erscheint nicht im PDF-Briefkopf; in der XRechnung bleibt der Verkäufer-Kontakt (inkl. Telefon) jedoch Pflichtangabe.</li>
          <li><strong>Standard-Zahlungsziel</strong> (Tage) und <strong>Standard-Steuerkategorie</strong> für neue Belege.</li>
          <li><strong>Kleinunternehmer (§19 UStG):</strong> Nur aktivieren, wenn Sie keine Umsatzsteuer ausweisen. Dann tragen alle Rechnungen den §19-Hinweis und keine USt.</li>
          <li><strong>Logo:</strong> PNG/JPG, max. 1&nbsp;MB — idealerweise ein transparentes PNG im Querformat.</li>
        </Bullets>
        <Shot src="/handbuch/02-firmenprofil.jpg" alt="Einstellungen: Firmenprofil mit gesetzlichen Pflichtangaben und Logo" />

        <H3>Partner (Kunden &amp; Lieferanten)</H3>
        <P>
          Unter <UI>Stammdaten → Partner</UI> pflegen Sie Ihre Geschäftspartner. Ein Partner kann
          gleichzeitig Kunde und Lieferant sein. Für eine rechtskonforme Rechnung braucht ein Kunde
          mindestens Name und vollständige Anschrift.
        </P>
        <Steps>
          <li><UI>Partner anlegen</UI> klicken.</li>
          <li>Name, Rechtsform und Rechnungsadresse eingeben; optional abweichende Lieferadresse.</li>
          <li>Rolle wählen: <UI>Kunde</UI> und/oder <UI>Lieferant</UI>.</li>
          <li>Optional: Sprache, E-Mail (für den Rechnungsversand), Telefon, Website, Kunden-/Lieferantennummer.</li>
          <li>Steuerlich: USt-IdNr. (wichtig für EU-Geschäfte), Steuernummer, Standard-Steuerkategorie, Währung.</li>
          <li>Zahlungsbedingungen: Zahlungsziel sowie <strong>Skonto</strong> (Prozent + Tage) — ein hinterlegtes Skonto erscheint automatisch im Zahlungsteil der Rechnung.</li>
          <li>Speichern.</li>
        </Steps>
        <Shot src="/handbuch/03-partner.jpg" alt="Partnerliste mit Kunden und Lieferanten" />
        <P>
          Auf der Detailseite eines Partners finden Sie zusätzlich Ansprechpartner, Notizen, Aufgaben
          und eine <strong>Kundenakte</strong> (Datei-Upload mit Belegcharakter — hochgeladene
          Dateien sind unveränderbar) sowie die Liste der Belege dieses Partners.
        </P>

        <H3>Artikel (Katalog)</H3>
        <P>
          Unter <UI>Stammdaten → Artikel</UI> hinterlegen Sie wiederkehrende Leistungen und Produkte
          mit Name, Einheit, Netto-Preis und Steuersatz. Beim Schreiben einer Rechnung übernehmen Sie
          solche Positionen per Klick über <UI>Aus Artikelstamm</UI>.
        </P>
        <Shot src="/handbuch/04-artikel.jpg" alt="Artikelstamm mit Leistungen und Produkten" />
      </>
    ),
  },
  {
    slug: 'kontenrahmen',
    number: 3,
    title: 'Kontenrahmen & Buchhaltung',
    summary: 'Einmalige Einrichtung: SKR03/04, Soll-/Ist-Versteuerung, EÜR oder Bilanzierung.',
    Body: () => (
      <>
        <P>
          Unter <UI>System → Kontenrahmen</UI> richten Sie einmalig die buchhalterischen
          Grundeinstellungen ein. Diese sind Voraussetzung für die USt-Voranmeldung, die EÜR, den
          Buchungsvorschlag beim Buchen von Eingangsbelegen und den DATEV-Export.
        </P>
        <Bullets>
          <li><strong>Kontenrahmen:</strong> <UI>SKR03</UI> oder <UI>SKR04</UI> — die Standard-Kontenrahmen, auf die Numera Erlöse, Aufwände, Vorsteuer und Umsatzsteuer bucht.</li>
          <li><strong>Besteuerungsart:</strong> <UI>Soll-Versteuerung</UI> (nach vereinbarten Entgelten, USt entsteht mit der Rechnung) oder <UI>Ist-Versteuerung</UI> (nach vereinnahmten Entgelten, USt entsteht mit der Zahlung).</li>
          <li><strong>Gewinnermittlung:</strong> <UI>EÜR</UI> oder <UI>Bilanzierung</UI>. Diese Wahl steuert u.&nbsp;a., ob die EÜR-Ansicht in den Berichten erscheint: Eine Kapitalgesellschaft (UG/GmbH) wählt Bilanzierung und sieht keine EÜR.</li>
          <li><strong>Wirtschaftsjahr-Beginn:</strong> in der Regel Monat 1 (Januar).</li>
        </Bullets>
        <Note>
          Die Einrichtung ist <strong>einmalig</strong> und aus Gründen der Buchführungsintegrität
          danach nicht mehr über die Oberfläche änderbar. Wählen Sie Kontenrahmen und Besteuerungsart
          im Zweifel gemeinsam mit Ihrem Steuerberater.
        </Note>
      </>
    ),
  },
  {
    slug: 'rechnungen',
    number: 4,
    title: 'Rechnungen & Belegkette',
    summary: 'Von Angebot bis Rechnung: anlegen, Rabatt/Skonto, finalisieren, versenden, korrigieren.',
    Body: () => (
      <>
        <P>
          Numera bildet die vollständige Belegkette ab: <strong>Angebot → Auftragsbestätigung →
          Lieferschein → Rechnung</strong>. Jeder Beleg lässt sich über <UI>Umwandeln</UI> in den
          nächsten überführen. Daneben gibt es <UI>Storno</UI>, <UI>Gutschrift</UI>,
          <UI>Abschlagsrechnung</UI> und <UI>Schlussrechnung</UI>. Alles beginnt unter
          <UI>Verkauf → Belege</UI>.
        </P>

        <H3>Rechnung anlegen</H3>
        <Steps>
          <li><UI>Beleg anlegen</UI> klicken und Belegart <UI>Rechnung</UI> wählen.</li>
          <li>Belegdatum und Währung setzen (EUR; bei Fremdwährung zusätzlich Wechselkurs + Kursdatum).</li>
          <li>Kunden aus dem Dropdown wählen (aus den Stammdaten).</li>
          <li>Positionen hinzufügen (<UI>Position hinzufügen</UI> oder <UI>Aus Artikelstamm</UI>): Bezeichnung, Menge, Einheit (Stück, Stunde, Tag, Monat, kg, Meter, m², Liter, kWh u.&nbsp;a.), Netto-Einzelpreis.</li>
          <li>Pro Position <strong>Steuerkategorie</strong> und <strong>USt-Satz</strong> setzen — im Normalfall <UI>S</UI> mit <UI>19&nbsp;%</UI> bzw. <UI>7&nbsp;%</UI>.</li>
          <li>Optional pro Position einen <strong>Rabatt&nbsp;%</strong> eintragen.</li>
          <li>Optional: Leistungsdatum, Käufer-Referenz (Leitweg-ID), Notizen.</li>
          <li><UI>Speichern</UI> — die Rechnung ist jetzt ein <em>Entwurf</em>.</li>
        </Steps>
        <Shot src="/handbuch/05-beleg-neu.jpg" alt="Neue Rechnung: Kopf, Positionen und Steuersätze" />
        <Note>
          <strong>Steuerkategorien:</strong> <UI>S</UI> = Regelsteuersatz (19/7&nbsp;%, Ihr Normalfall)
          · <UI>AE</UI> = Reverse-Charge §13b · <UI>K</UI> = innergemeinschaftliche Lieferung ·
          <UI>E</UI> = steuerbefreit §4 · <UI>Z</UI> = Nullsatz · <UI>G</UI> = Ausfuhrlieferung ·
          <UI>O</UI> = nicht steuerbar.
        </Note>

        <H3>Rabatt und Skonto</H3>
        <Bullets>
          <li><strong>Rabatt (pro Position):</strong> Ein Prozent-Rabatt senkt den Nettobetrag der Position (Anzeige „abzgl. X&nbsp;% Rabatt"); die Umsatzsteuer wird auf den reduzierten Nettobetrag berechnet. In der XRechnung erscheint der Rabatt als Nachlass (AllowanceCharge).</li>
          <li><strong>Skonto:</strong> Wird aus den Partner-Stammdaten übernommen (Prozent + Tage) und im Zahlungsteil der Rechnung ausgewiesen. Skonto hinterlegen Sie beim Kunden, nicht an der einzelnen Rechnung.</li>
        </Bullets>

        <H3>Entwurf bearbeiten</H3>
        <P>
          Ein Entwurf zeigt die <strong>Nettosumme</strong>; Umsatzsteuer und Bruttobetrag erscheinen
          als Vorschau und werden erst bei der Finalisierung endgültig eingefroren. Einen Entwurf
          können Sie jederzeit erneut öffnen, Positionen ergänzen oder entfernen und wieder speichern.
        </P>

        <H3>Finalisieren</H3>
        <P>Auf der Belegdetailseite klicken Sie auf <UI>Finalisieren</UI>. Dabei passiert Folgendes:</P>
        <Bullets>
          <li>Es wird eine endgültige, <strong>lückenlose Belegnummer</strong> vergeben (z.&nbsp;B. RE-2026-00001).</li>
          <li>Die Rechnung wird live gegen den offiziellen <strong>KoSIT-Validator</strong> geprüft; fehlende Pflichtangaben werden konkret benannt.</li>
          <li>Das <strong>Fälligkeitsdatum</strong> wird gesetzt (Belegdatum + Zahlungsziel).</li>
          <li>Aussteller-/Empfängerangaben (§14) und die Steueraufstellung werden eingefroren; das Finalisierungsdatum wird festgehalten.</li>
          <li>Ohne erfasstes Leistungsdatum erscheint bei Rechnungstypen „Leistungsdatum entspricht dem Rechnungsdatum" (§14 Abs.&nbsp;4 Nr.&nbsp;6).</li>
        </Bullets>
        <P>Nach dem Finalisieren ist die Rechnung <strong>unveränderbar</strong> (GoBD).</P>
        <Shot src="/handbuch/06-rechnung-detail.jpg" alt="Finalisierte Rechnung mit Aktionen: PDF, XRechnung, ZUGFeRD, Versand" />

        <H3>Herunterladen &amp; versenden</H3>
        <Bullets>
          <li><UI>PDF herunterladen</UI> — die klassische Sicht-Rechnung (Sprache de/en wählbar).</li>
          <li><UI>ZUGFeRD (PDF/A-3)</UI> — PDF mit eingebettetem XML (Sicht- und E-Rechnung in einem).</li>
          <li><UI>XRechnung (XML)</UI> — das reine XRechnung-UBL-XML.</li>
          <li><UI>Per E-Mail senden</UI> — verschickt die Rechnung und hängt dabei das <strong>ZUGFeRD-PDF</strong> an (ist ZUGFeRD nicht erzeugbar, geht ein reines PDF raus).</li>
          <li><UI>E-Rechnung senden</UI> — versendet die reine XRechnung-XML.</li>
        </Bullets>
        <Note>
          Der E-Mail-Versand funktioniert erst, wenn unter <UI>System → E-Mail</UI> ein SMTP-Zugang
          hinterlegt ist (Kapitel „E-Mail-Einstellungen").
        </Note>

        <H3>Korrigieren: Storno &amp; Gutschrift</H3>
        <P>
          Eine finalisierte Rechnung wird nie gelöscht, sondern korrigiert: <UI>Stornieren</UI>
          erzeugt einen Storno-Beleg, <UI>Gutschrift</UI> einen Gutschrift-Entwurf. Ein reiner
          <em> Entwurf</em> lässt sich über <UI>Entwurf löschen</UI> entfernen.
        </P>
      </>
    ),
  },
  {
    slug: 'erechnung',
    number: 5,
    title: 'E-Rechnung (XRechnung & ZUGFeRD)',
    summary: 'Pflichtformate, KoSIT-Prüfung, Leitweg-ID, §19/§13b-Hinweise und Empfang.',
    Body: () => (
      <>
        <P>
          Seit 2025 gilt im B2B die E-Rechnungspflicht. Numera erzeugt beide gängigen Formate und
          prüft jede Ausgangsrechnung vor dem Versand live gegen den KoSIT-Validator — dieselbe
          Instanz, die definiert, was als XRechnung gültig ist. Fehler blockieren den Versand und
          werden verständlich erklärt.
        </P>
        <Bullets>
          <li><strong>XRechnung</strong> in UBL und CII (wertidentisch, zwei Syntaxen).</li>
          <li><strong>ZUGFeRD / Factur-X</strong> als PDF/A-3 mit eingebettetem XML.</li>
          <li><strong>Käufer-Referenz (Leitweg-ID, BT-10):</strong> für die XRechnung erforderlich — bei der Rechnung im Feld <UI>Käufer-Referenz</UI> hinterlegen.</li>
          <li><strong>Kleinunternehmer (§19):</strong> keine USt, dafür der Pflichthinweis „Kein Ausweis von Umsatzsteuer, da Kleinunternehmer gemäß §19 UStG".</li>
          <li><strong>Reverse-Charge (§13b):</strong> Hinweis „Steuerschuldnerschaft des Leistungsempfängers".</li>
          <li><strong>Empfang:</strong> Eingehende XRechnung/ZUGFeRD laden Sie unter <UI>Belege → Beleg erfassen</UI> hoch; Numera validiert, zeigt sie lesbar an und ordnet den Lieferanten zu.</li>
        </Bullets>
      </>
    ),
  },
  {
    slug: 'offene-posten',
    number: 6,
    title: 'Offene Posten & Zahlungen',
    summary: 'Forderungen im Blick, (Teil-)Zahlungen erfassen, Status automatisch fortschreiben.',
    Body: () => (
      <>
        <P>
          Jede finalisierte Rechnung erscheint unter <UI>Verkauf → Offene Posten</UI> mit Betrag,
          offenem Betrag, Fälligkeit, Mahnstufe und Tagen überfällig. Filtern Sie nach Status oder
          nur nach überfälligen Posten.
        </P>
        <Shot src="/handbuch/07-offene-posten.jpg" alt="Offene Posten mit Kunde, Beträgen und Fälligkeit" />
        <P>Über <UI>Zahlung erfassen</UI> buchen Sie Zahlungseingänge:</P>
        <Steps>
          <li>Betrag eingeben (auch Teilzahlungen möglich).</li>
          <li>Wertstellungsdatum wählen (maßgeblich für die kassenbasierten Berichte).</li>
          <li>Zahlungsart wählen: Überweisung, Bar, Karte, SEPA oder Sonstige.</li>
          <li>Optional eine Referenz angeben und speichern.</li>
        </Steps>
        <P>
          Der Status wird automatisch fortgeschrieben: <em>offen → teilweise bezahlt → bezahlt</em>.
          Eine versehentlich erfasste Zahlung lässt sich stornieren (Gegenbuchung).
        </P>
      </>
    ),
  },
  {
    slug: 'mahnwesen',
    number: 7,
    title: 'Mahnwesen',
    summary: 'Mahnstufen konfigurieren, Mahnlauf starten; Mahnung-PDF = komplette Rechnung + Mahnung.',
    Body: () => (
      <>
        <P>
          Unter <UI>Verkauf → Mahnwesen</UI> konfigurieren Sie die Mahnstufen mit Bezeichnung,
          Karenzfrist, Mahngebühr und Verzugszinssatz. Ein <UI>Mahnlauf</UI> erzeugt für überfällige
          Posten automatisch die passende Mahnung der nächsten Stufe.
        </P>
        <Shot src="/handbuch/08-mahnwesen.jpg" alt="Mahnwesen-Konfiguration mit Mahnstufen" />
        <Bullets>
          <li>Die erzeugten Mahnungen erscheinen unter <UI>Verkauf → Offene Posten</UI> im Abschnitt <UI>Mahnungen</UI> und lassen sich dort herunterladen oder per E-Mail senden.</li>
          <li>Das <strong>Mahnungs-PDF enthält die komplette Original-Rechnung</strong> (alle Positionen und Summen) <strong>plus die Mahnungsseite</strong> mit offenem Betrag, Mahngebühr, Verzugszinsen und neuer Zahlungsfrist.</li>
          <li>Das Mahnwesen ist ab Tarif&nbsp;L verfügbar.</li>
        </Bullets>
      </>
    ),
  },
  {
    slug: 'serien',
    number: 8,
    title: 'Serienrechnungen',
    summary: 'Wiederkehrende Rechnungen als Vorlage mit Intervall; automatische Erzeugung nachts.',
    Body: () => (
      <>
        <P>
          Wiederkehrende Rechnungen (z.&nbsp;B. monatliche Leistungen) hinterlegen Sie unter
          <UI>Verkauf → Serienrechnungen</UI> als Vorlage.
        </P>
        <Steps>
          <li>Vorlage anlegen: Kunde und Positionen wie bei einer Rechnung.</li>
          <li>Intervall wählen: <UI>monatlich</UI>, <UI>quartalsweise</UI>, <UI>jährlich</UI> oder <UI>wöchentlich</UI> (mit Intervall-Anzahl).</li>
          <li>Startdatum und Ende festlegen: bis Datum, nach einer Anzahl Rechnungen oder unbegrenzt.</li>
          <li>Schalter setzen: <UI>Automatisch finalisieren</UI> (Standard an — erzeugt direkt eine finalisierte Rechnung inkl. KoSIT-Prüfung/E-Rechnung) und optional <UI>Automatisch versenden</UI> (per E-Mail, als ZUGFeRD).</li>
          <li>Vorlage <strong>aktivieren</strong> — sie startet zunächst pausiert.</li>
        </Steps>
        <P>
          Numera erzeugt die fälligen Rechnungen danach <strong>automatisch im Hintergrund, nachts
          um 00:00&nbsp;Uhr am Starttag</strong> des jeweiligen Intervalls. Die Vorlage endet
          automatisch beim Enddatum bzw. nach der maximalen Anzahl. Die erzeugten Rechnungen
          erscheinen in der normalen Belegliste.
        </P>
        <Note>
          <strong>Monatsende:</strong> Vorlagen mit Starttag 29.–31. können in kurzen Monaten
          (z.&nbsp;B. Februar) verspätet erzeugt werden, da dieser Tag dort fehlt. Für Starttage
          1.–28. tritt das nicht auf.
        </Note>
      </>
    ),
  },
  {
    slug: 'banking',
    number: 9,
    title: 'Banking',
    summary: 'Bankkonto verbinden/importieren und Umsätze den offenen Posten zuordnen.',
    Body: () => (
      <>
        <P>
          Unter <UI>Banking → Bankkonten</UI> verbinden Sie ein Konto bzw. importieren Umsätze. In
          <UI>Banking → Zahlungen prüfen</UI> gleichen Sie Bankumsätze mit offenen Posten ab und
          ordnen Zahlungen per Klick den passenden Rechnungen zu — so bleibt der Zahlungsstatus Ihrer
          Offenen Posten aktuell, ohne jede Zahlung manuell zu erfassen.
        </P>
        <Shot src="/handbuch/10-banking.jpg" alt="Bankkonten und Umsätze" />
      </>
    ),
  },
  {
    slug: 'belege',
    number: 10,
    title: 'Eingangsbelege & Lieferantenzahlungen',
    summary: 'Belege erfassen, prüfen & buchen und die Zahlung erfassen (Abflussprinzip für die EÜR).',
    Body: () => (
      <>
        <P>
          Belege von Lieferanten (Rechnungen, Quittungen) erfassen Sie unter
          <UI>Belege → Beleg erfassen</UI> — per Upload oder Foto. Zusätzlich hat Ihr Mandant eine
          eigene <strong>Beleg-E-Mail-Adresse</strong>, an die Sie Lieferantenbelege weiterleiten
          können; die Anhänge landen automatisch in der Prüfliste.
        </P>
        <P>
          Numera liest die Eckdaten per <strong>Texterkennung (OCR)</strong> aus. Unter
          <UI>Belege → Prüfen &amp; Buchen</UI> arbeiten Sie den Beleg ab:
        </P>
        <Steps>
          <li>Erkannte Werte kontrollieren/korrigieren: Lieferant, Rechnungsnummer, Rechnungs- und Leistungsdatum.</li>
          <li>Aufwandskonto, USt-Satz sowie Netto/USt/Brutto prüfen.</li>
          <li>Den <UI>Buchungsvorschlag</UI> ansehen (welche Konten mit welchen Beträgen gebucht werden).</li>
          <li><UI>Bestätigen &amp; Buchen</UI> — es entsteht eine unveränderbare Buchung; das Original bleibt archiviert.</li>
        </Steps>
        <Shot src="/handbuch/11-belege-pruefen.jpg" alt="Belege prüfen & buchen (Eingangsbelege)" />
        <Note>Voraussetzung fürs Buchen ist ein eingerichteter <strong>Kontenrahmen</strong>.</Note>

        <H3>Lieferantenzahlung erfassen</H3>
        <P>
          Nach dem Buchen erscheint auf der Belegseite der Abschnitt <UI>Zahlungen</UI> mit dem Status
          (Offen / Teilweise bezahlt / Bezahlt) und dem offenen Betrag. Über <UI>Zahlung erfassen</UI>
          halten Sie fest, <strong>wann</strong> Sie den Beleg bezahlt haben (Betrag,
          Wertstellungsdatum, Zahlungsart, Referenz); Teilzahlungen und Stornos sind möglich.
        </P>
        <P>
          Das ist wichtig für die EÜR: Erst die erfasste Zahlung lässt die EÜR die
          <strong> Betriebsausgabe am Bezahldatum</strong> erfassen (Abflussprinzip). Ein nur
          gebuchter, aber unbezahlter Beleg zählt in der EÜR noch nicht als Ausgabe.
        </P>
      </>
    ),
  },
  {
    slug: 'berichte',
    number: 11,
    title: 'Berichte (USt-VA, EÜR & DATEV)',
    summary: 'USt-Voranmeldung mit ELSTER-XML, kassenbasierte EÜR und DATEV-Buchungsstapel.',
    Body: () => (
      <>
        <P>
          Unter <UI>Berichte</UI> erzeugt Numera die steuerlichen Auswertungen. Werte erscheinen,
          sobald für den Zeitraum gebuchte Belege bzw. Zahlungen vorliegen.
        </P>

        <H3>Umsatzsteuer-Voranmeldung</H3>
        <P>
          Die <UI>USt-VA</UI>-Prüfansicht zeigt die Kennziffern Ihrer Voranmeldung — u.&nbsp;a. Kz 81
          (Umsätze 19&nbsp;%), Kz 86 (Umsätze 7&nbsp;%), Kz 66 (Vorsteuer) und Kz 83 (Zahllast). Je
          nach eingestellter Besteuerungsart wird nach Soll oder Ist gerechnet; die Vorsteuer aus
          gebuchten Eingangsbelegen ist berücksichtigt. Ausgabe als <strong>ELSTER-XML</strong> und
          als <strong>PDF</strong>.
        </P>
        <P>
          Im Abschnitt <UI>Finanzamt-Zahlungen (USt)</UI> erfassen Sie Ihre USt-Vorauszahlungen an
          das Finanzamt bzw. Erstattungen. Diese fließen in die EÜR (Zeile 58) und machen den
          EÜR-Gewinn umsatzsteuer-neutral.
        </P>

        <H3>EÜR (Einnahmen-Überschuss-Rechnung)</H3>
        <P>
          Die <UI>EÜR</UI> ist nur sichtbar, wenn Ihre Gewinnermittlung auf EÜR eingestellt ist. Sie
          rechnet <strong>kassenbasiert</strong> (Zufluss-/Abflussprinzip): Einnahmen am
          Zahlungseingang der Rechnungen, Ausgaben am Bezahldatum der Eingangsbelege. Die Zeilen
          folgen der amtlichen Anlage EÜR; Kleinunternehmer-Einnahmen stehen in ihrer eigenen Zeile
          (brutto). Jahr/Zeitraum wählen und als PDF exportieren.
        </P>
        <Note>
          Eine Kapitalgesellschaft (UG/GmbH) bilanziert und gibt <strong>keine</strong> EÜR, sondern
          eine E-Bilanz ab — in diesem Fall ist die EÜR-Ansicht ausgeblendet.
        </Note>

        <H3>DATEV-Export</H3>
        <P>
          Unter <UI>Berichte → DATEV-Export</UI> erzeugen Sie für einen Zeitraum einen
          <strong> Buchungsstapel (EXTF)</strong> als CSV-Datei — zur Übergabe an Ihren Steuerberater
          oder den Import in eine Buchhaltungssoftware.
        </P>
        <Note>
          Lassen Sie die steuerliche Logik (Kontenzuordnung, Steuerschlüssel, USt) vor dem produktiven
          Einsatz von Ihrem Steuerberater freigeben.
        </Note>
      </>
    ),
  },
  {
    slug: 'team',
    number: 12,
    title: 'Team & Nutzer',
    summary: 'Personen einladen, Rollen vergeben, temporäres Passwort; strikte Mandantentrennung.',
    Body: () => (
      <>
        <P>
          Unter <UI>System → Team</UI> laden Sie weitere Personen per E-Mail ein und vergeben Rollen
          (<UI>Inhaber</UI> / <UI>Mitarbeiter</UI>). Beim Einladen wird ein <strong>temporäres
          Passwort</strong> gesetzt, das die Person bei der ersten Anmeldung ändert; die Liste zeigt
          die E-Mail-Adresse. Einige Funktionen sind nur für Inhaber verfügbar, und der Funktionsumfang
          hängt vom Tarif ab. Alle Daten bleiben strikt Ihrem Mandanten zugeordnet.
        </P>
        <Shot src="/handbuch/14-team.jpg" alt="Team: Nutzer einladen und Rollen vergeben" />
      </>
    ),
  },
  {
    slug: 'email',
    number: 13,
    title: 'E-Mail-Einstellungen',
    summary: 'SMTP pro Mandant (Passwort verschlüsselt) und anpassbare Textvorlagen mit Platzhaltern.',
    Body: () => (
      <>
        <P>
          Unter <UI>System → E-Mail</UI> (nur Inhaber) konfigurieren Sie den Mail-Versand pro Mandant
          — damit jedes Unternehmen aus seinem eigenen Postfach versendet.
        </P>
        <H3>SMTP-Zugang</H3>
        <Bullets>
          <li>Host, Port und TLS gemäß Ihrem Mail-Anbieter (meist Port 587 mit TLS).</li>
          <li>Benutzername und Passwort — das <strong>Passwort wird verschlüsselt gespeichert</strong> und nie im Klartext angezeigt.</li>
          <li>Absender-Adresse und Absender-Name.</li>
          <li>Mit <UI>Testmail senden</UI> prüfen Sie sofort, ob die Verbindung steht.</li>
        </Bullets>
        <H3>Textvorlagen</H3>
        <P>
          Betreff und Text für die Rechnungs- und die Mahnungs-E-Mail sind frei anpassbar. Verwenden
          Sie Platzhalter, die beim Versand automatisch ersetzt werden:
        </P>
        <Bullets>
          <li><UI>{'{Rechnungsnummer}'}</UI>, <UI>{'{Belegart}'}</UI>, <UI>{'{Betrag}'}</UI>, <UI>{'{Fälligkeitsdatum}'}</UI>, <UI>{'{Kundenname}'}</UI>, <UI>{'{Firmenname}'}</UI></li>
          <li>bei Mahnungen zusätzlich <UI>{'{Mahnstufe}'}</UI> und <UI>{'{Mahngebühr}'}</UI></li>
        </Bullets>
        <P>
          Leere Felder verwenden die Standardtexte. Ohne hinterlegten SMTP-Zugang wird nichts
          versendet.
        </P>
      </>
    ),
  },
  {
    slug: 'compliance',
    number: 14,
    title: 'GoBD, Backup & Datenschutz',
    summary: 'Unveränderbarkeit, Audit-Log, Mandantentrennung, Backups und DSGVO-Datenexport.',
    Body: () => (
      <>
        <Bullets>
          <li><strong>Unveränderbarkeit:</strong> Finalisierte Belege sind GoBD-fest; Korrekturen entstehen als Storno oder Gutschrift. Ein revisionssicheres Audit-Log protokolliert relevante Änderungen.</li>
          <li><strong>Mandantentrennung:</strong> Strikte Datentrennung über Row-Level-Security — jeder Mandant sieht ausschließlich seine eigenen Daten.</li>
          <li><strong>Backups:</strong> Die Datenbank wird automatisch täglich gesichert. Bewahren Sie zusätzlich eine Kopie außer Haus auf (Betreiberpflicht).</li>
          <li><strong>Datenexport (DSGVO):</strong> Über <UI>System → Einstellungen</UI> können Inhaber alle Mandantendaten als ZIP-Archiv ausleiten (maschinenlesbares JSON je Tabelle plus die Originaldateien).</li>
        </Bullets>
        <p className="mt-6 border-t border-border pt-4 text-xs text-muted-foreground">
          Hinweis: „GoBD-konform" bedeutet, dass Numera nach den GoBD-Grundsätzen gebaut ist. Eine
          „GoBD-Zertifizierung" gibt es nicht und wird nicht behauptet. Für steuerliche Einzelfragen
          wenden Sie sich an Ihren Steuerberater.
        </p>
      </>
    ),
  },
]

export function chapterBySlug(slug: string | undefined): Chapter | undefined {
  return CHAPTERS.find((c) => c.slug === slug)
}
