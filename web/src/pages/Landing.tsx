import { Link } from 'react-router-dom'
import { useTheme } from '../lib/useTheme'
import {
  IconDocuments,
  IconInbound,
  IconOpenItems,
  IconDashboard,
  IconTeam,
  IconSettings,
  IconSun,
  IconMoon,
} from '../components/icons'
import markLight from '../assets/numera-mark.png'
import markDark from '../assets/numera-mark-dark.png'

// Numera's public product website. Deliberately no fabricated prices, customer
// logos or testimonials — the trust markers are the real compliance standards the
// product meets. "Anmelden" / "Kostenlos starten" route to the app's auth page.

const STANDARDS = [
  'GoBD-konform',
  'EN 16931',
  'XRechnung (UBL & CII)',
  'ZUGFeRD / Factur-X',
  'KoSIT-validiert',
  'DSGVO',
]

const FEATURES = [
  {
    icon: IconDocuments,
    title: 'E-Rechnung, die der Staat akzeptiert',
    body: 'XRechnung (UBL + CII) und ZUGFeRD (PDF/A-3). Jede Ausgangsrechnung wird vor dem Versand live gegen den offiziellen KoSIT-Validator geprüft — Fehler werden verständlich erklärt.',
  },
  {
    icon: IconSettings,
    title: 'Unveränderbare Belegkette',
    body: 'Angebot → Auftrag → Lieferschein → Rechnung, GoBD-fest in der Datenbank erzwungen. Korrekturen entstehen als Storno oder Gutschrift. §14 UStG vollständig.',
  },
  {
    icon: IconOpenItems,
    title: 'Offene Posten & Mahnwesen',
    body: 'Zahlungen und Teilzahlungen erfassen, den Status automatisch fortschreiben und mehrstufige Mahnungen mit Fristen, Gebühren und Verzugszinsen erzeugen.',
  },
  {
    icon: IconInbound,
    title: 'E-Rechnungen empfangen',
    body: 'Eingehende XRechnung- und ZUGFeRD-Dateien hochladen, validieren, menschenlesbar anzeigen und automatisch dem passenden Lieferanten zuordnen.',
  },
  {
    icon: IconDashboard,
    title: 'Überall installierbar',
    body: 'Progressive Web App für Desktop und Smartphone mit offline-fähiger Hülle. Zweisprachig Deutsch und Englisch — umschaltbar pro Nutzer.',
  },
  {
    icon: IconTeam,
    title: 'Mandantensicher',
    body: 'Strikte Datentrennung pro Mandant über Row-Level-Security, ein unveränderbares Audit-Log und exakte Geldarithmetik ohne Rundungsfehler.',
  },
]

const TIERS = [
  { name: 'S', tagline: 'Einstieg', points: ['Rechnungen & Belegkette', 'Produktkatalog', 'Offene Posten', 'DSGVO-Datenexport'] },
  { name: 'M', tagline: 'Team', points: ['Alles aus S', 'Team & Mehrbenutzer', 'Rollen: Inhaber / Mitarbeiter'] },
  { name: 'L', tagline: 'Compliance', featured: true, points: ['Alles aus M', 'E-Rechnung (XRechnung/ZUGFeRD)', 'Mahnwesen', 'Fremdwährung, Serien- & Abschlagsrechnungen', 'Steuerberater-Zugang'] },
  { name: 'XL', tagline: 'Skalierung', points: ['Alles aus L', 'API-Zugang', 'Voller Funktionsumfang'] },
]

function Logo() {
  const { theme } = useTheme()
  return (
    <Link to="/" className="flex items-center gap-2.5" aria-label="Numera">
      <img
        src={theme === 'dark' ? markDark : markLight}
        alt=""
        aria-hidden="true"
        className="h-8 w-8 object-contain"
      />
      <span className="text-[1.15rem] font-bold tracking-tight text-foreground">Numera</span>
    </Link>
  )
}

function Nav() {
  const { theme, toggle } = useTheme()
  return (
    <header className="sticky top-0 z-40 border-b border-border/70 bg-background/80 backdrop-blur">
      <div className="mx-auto flex h-16 max-w-6xl items-center gap-4 px-5 sm:px-6">
        <Logo />
        <nav className="mx-auto hidden items-center gap-7 text-sm font-medium text-muted-foreground md:flex">
          <a href="#funktionen" className="transition-colors hover:text-foreground">Funktionen</a>
          <a href="#erechnung" className="transition-colors hover:text-foreground">E-Rechnung</a>
          <a href="#tarife" className="transition-colors hover:text-foreground">Tarife</a>
          <a href="#sicherheit" className="transition-colors hover:text-foreground">Sicherheit</a>
        </nav>
        <div className="ml-auto flex items-center gap-2 md:ml-0">
          <button
            type="button"
            onClick={toggle}
            aria-label={theme === 'dark' ? 'Helles Design' : 'Dunkles Design'}
            className="grid h-9 w-9 place-items-center rounded-lg border border-border text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground"
          >
            {theme === 'dark' ? <IconSun size={18} /> : <IconMoon size={18} />}
          </button>
          <Link
            to="/login"
            className="rounded-lg px-3.5 py-2 text-sm font-semibold text-foreground transition-colors hover:bg-secondary"
          >
            Anmelden
          </Link>
          <Link
            to="/login"
            className="rounded-lg bg-primary px-3.5 py-2 text-sm font-semibold text-primary-foreground shadow-sm transition-colors hover:bg-primary/90"
          >
            Kostenlos starten
          </Link>
        </div>
      </div>
    </header>
  )
}

function InvoiceMock() {
  return (
    <div className="relative">
      <div className="absolute -inset-4 -z-10 rounded-3xl bg-[radial-gradient(60%_60%_at_70%_0%,color-mix(in_oklab,var(--primary)_22%,transparent),transparent)]" />
      <div className="rounded-2xl border border-border bg-card p-5 shadow-xl shadow-foreground/5">
        <div className="flex items-start justify-between">
          <div>
            <p className="text-xs font-medium text-muted-foreground">Rechnung</p>
            <p className="text-lg font-bold tracking-tight text-foreground tnum">RE-2026-00042</p>
          </div>
          <span className="inline-flex items-center gap-1.5 rounded-full border border-primary/25 bg-primary/10 px-2.5 py-1 text-xs font-semibold text-primary">
            <span className="grid h-3.5 w-3.5 place-items-center rounded-full bg-primary text-[9px] text-primary-foreground">✓</span>
            E-Rechnung geprüft
          </span>
        </div>
        <div className="mt-4 space-y-2 border-t border-border pt-4 text-sm">
          {[
            ['Beratung (12 Std.)', '1.140,00 €'],
            ['Softwarelizenz', '360,00 €'],
          ].map(([k, v]) => (
            <div key={k} className="flex justify-between">
              <span className="text-muted-foreground">{k}</span>
              <span className="font-medium tabular-nums text-foreground">{v}</span>
            </div>
          ))}
          <div className="flex justify-between pt-1 text-muted-foreground">
            <span>USt 19 %</span>
            <span className="tabular-nums">285,00 €</span>
          </div>
          <div className="mt-1 flex justify-between border-t border-border pt-2 text-base font-bold">
            <span className="text-foreground">Gesamt</span>
            <span className="tabular-nums text-foreground">1.785,00 €</span>
          </div>
        </div>
        <div className="mt-4 flex items-center gap-2">
          <span className="rounded-md bg-secondary px-2 py-1 text-[0.7rem] font-semibold text-secondary-foreground">XRechnung</span>
          <span className="rounded-md bg-secondary px-2 py-1 text-[0.7rem] font-semibold text-secondary-foreground">ZUGFeRD</span>
          <span className="ml-auto text-[0.7rem] font-medium text-primary">KoSIT: 0 Fehler</span>
        </div>
      </div>
    </div>
  )
}

export default function Landing() {
  return (
    <div className="min-h-dvh bg-background">
      <Nav />

      {/* Hero */}
      <section className="relative overflow-hidden">
        <div className="mx-auto grid max-w-6xl items-center gap-12 px-5 py-16 sm:px-6 lg:grid-cols-2 lg:py-24">
          <div>
            <span className="inline-flex items-center gap-2 rounded-full border border-border bg-card px-3 py-1 text-xs font-medium text-muted-foreground">
              <span className="h-1.5 w-1.5 rounded-full bg-primary" />
              E-Rechnungspflicht seit 2025 — vorbereitet
            </span>
            <h1 className="mt-5 text-4xl font-bold leading-[1.08] tracking-tight text-foreground sm:text-5xl">
              Rechnungen und E-Rechnung,<br />
              <span className="text-primary">rechtskonform an einem Ort.</span>
            </h1>
            <p className="mt-5 max-w-xl text-lg leading-relaxed text-muted-foreground">
              Numera bündelt Angebote, Rechnungen, gesetzliche E-Rechnung, offene Posten und
              Mahnwesen für deutsche Unternehmen — GoBD-konform, EN-16931-geprüft, auf jedem Gerät.
            </p>
            <div className="mt-8 flex flex-wrap items-center gap-3">
              <Link
                to="/login"
                className="rounded-lg bg-primary px-5 py-3 text-sm font-semibold text-primary-foreground shadow-sm transition-colors hover:bg-primary/90"
              >
                Kostenlos starten
              </Link>
              <a
                href="#funktionen"
                className="rounded-lg border border-border bg-card px-5 py-3 text-sm font-semibold text-foreground transition-colors hover:bg-secondary"
              >
                Funktionen ansehen
              </a>
            </div>
            <p className="mt-4 text-xs text-muted-foreground">
              Keine Kreditkarte nötig · Deutsch &amp; Englisch · Desktop &amp; Smartphone
            </p>
          </div>
          <InvoiceMock />
        </div>

        {/* Standards strip */}
        <div className="border-y border-border bg-card/50">
          <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-center gap-x-8 gap-y-3 px-5 py-5 sm:px-6">
            {STANDARDS.map((s) => (
              <span key={s} className="text-sm font-semibold tracking-tight text-muted-foreground">
                {s}
              </span>
            ))}
          </div>
        </div>
      </section>

      {/* Features */}
      <section id="funktionen" className="mx-auto max-w-6xl px-5 py-20 sm:px-6">
        <div className="max-w-2xl">
          <p className="text-sm font-semibold uppercase tracking-[0.1em] text-primary">Funktionen</p>
          <h2 className="mt-2 text-3xl font-bold tracking-tight text-foreground">
            Die komplette Belegkette — bis zur E-Rechnung
          </h2>
          <p className="mt-3 text-muted-foreground">
            Von der ersten Angebotszeile bis zur validierten E-Rechnung und dem letzten offenen Posten.
          </p>
        </div>
        <div className="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {FEATURES.map((f) => {
            const Ico = f.icon
            return (
              <div key={f.title} className="rounded-2xl border border-border bg-card p-6 transition-shadow hover:shadow-md hover:shadow-foreground/5">
                <span className="grid h-11 w-11 place-items-center rounded-xl bg-primary/10 text-primary">
                  <Ico size={22} />
                </span>
                <h3 className="mt-4 text-base font-semibold text-foreground">{f.title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-muted-foreground">{f.body}</p>
              </div>
            )
          })}
        </div>
      </section>

      {/* E-Rechnung spotlight */}
      <section id="erechnung" className="border-y border-border bg-card/40">
        <div className="mx-auto grid max-w-6xl items-center gap-12 px-5 py-20 sm:px-6 lg:grid-cols-2">
          <div>
            <p className="text-sm font-semibold uppercase tracking-[0.1em] text-primary">E-Rechnung</p>
            <h2 className="mt-2 text-3xl font-bold tracking-tight text-foreground">
              Vom Staat geprüft — nicht nur von uns behauptet
            </h2>
            <p className="mt-4 text-muted-foreground">
              Andere erzeugen „E-Rechnungen". Numera prüft jede Ausgangsrechnung vor dem Versand
              gegen den offiziellen KoSIT-Validator — dieselbe Instanz, die definiert, was als
              XRechnung gültig ist. Fehler blockieren den Versand und werden verständlich erklärt.
            </p>
            <ul className="mt-6 space-y-3">
              {[
                'XRechnung in UBL und CII — identische Werte, zwei Syntaxen',
                'ZUGFeRD als PDF/A-3 mit eingebettetem XML, wertidentisch',
                'Empfang: hochladen, validieren, lesbar anzeigen, zuordnen',
              ].map((p) => (
                <li key={p} className="flex items-start gap-3 text-sm text-foreground">
                  <span className="mt-0.5 grid h-5 w-5 shrink-0 place-items-center rounded-full bg-primary/15 text-[11px] font-bold text-primary">✓</span>
                  {p}
                </li>
              ))}
            </ul>
          </div>
          <InvoiceMock />
        </div>
      </section>

      {/* Tiers */}
      <section id="tarife" className="mx-auto max-w-6xl px-5 py-20 sm:px-6">
        <div className="mx-auto max-w-2xl text-center">
          <p className="text-sm font-semibold uppercase tracking-[0.1em] text-primary">Tarife</p>
          <h2 className="mt-2 text-3xl font-bold tracking-tight text-foreground">
            Ein Tarif, der mitwächst
          </h2>
          <p className="mt-3 text-muted-foreground">
            Funktionen werden pro Tarif freigeschaltet. Starten Sie klein — die E-Rechnung und das
            Mahnwesen sind ab Tarif L an Bord.
          </p>
        </div>
        <div className="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
          {TIERS.map((tier) => (
            <div
              key={tier.name}
              className={
                'flex flex-col rounded-2xl border bg-card p-6 ' +
                (tier.featured ? 'border-primary shadow-lg shadow-primary/10 ring-1 ring-primary/20' : 'border-border')
              }
            >
              <div className="flex items-center justify-between">
                <span className="text-2xl font-bold tracking-tight text-foreground">Tarif {tier.name}</span>
                {tier.featured && (
                  <span className="rounded-full bg-primary px-2 py-0.5 text-[0.65rem] font-semibold text-primary-foreground">
                    Empfohlen
                  </span>
                )}
              </div>
              <p className="mt-1 text-sm text-muted-foreground">{tier.tagline}</p>
              <ul className="mt-5 flex-1 space-y-2.5">
                {tier.points.map((p) => (
                  <li key={p} className="flex items-start gap-2 text-sm text-foreground">
                    <span className="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                    {p}
                  </li>
                ))}
              </ul>
              <Link
                to="/login"
                className={
                  'mt-6 rounded-lg px-4 py-2.5 text-center text-sm font-semibold transition-colors ' +
                  (tier.featured
                    ? 'bg-primary text-primary-foreground hover:bg-primary/90'
                    : 'border border-border text-foreground hover:bg-secondary')
                }
              >
                Kostenlos starten
              </Link>
            </div>
          ))}
        </div>
        <p className="mt-6 text-center text-xs text-muted-foreground">
          Abrechnung folgt — während der Beta ist Numera kostenlos. Neue Mandanten starten auf Tarif S.
        </p>
      </section>

      {/* Security / CTA band */}
      <section id="sicherheit" className="border-t border-border">
        <div className="mx-auto max-w-6xl px-5 py-20 sm:px-6">
          <div className="overflow-hidden rounded-3xl border border-border bg-card px-8 py-14 text-center shadow-sm">
            <h2 className="mx-auto max-w-2xl text-3xl font-bold tracking-tight text-foreground">
              Rechtskonform gebaut — nicht nachträglich aufgesetzt
            </h2>
            <p className="mx-auto mt-4 max-w-2xl text-muted-foreground">
              Mandantentrennung per Row-Level-Security, unveränderbares Audit-Log, race-sichere
              Rechnungsnummern und eine GoBD-Verfahrensdokumentation. Numera ist GoBD-konform —
              wir behaupten nie „zertifiziert", denn das gibt es nicht.
            </p>
            <div className="mt-8 flex flex-wrap justify-center gap-3">
              <Link
                to="/login"
                className="rounded-lg bg-primary px-6 py-3 text-sm font-semibold text-primary-foreground shadow-sm transition-colors hover:bg-primary/90"
              >
                Kostenlos starten
              </Link>
              <Link
                to="/login"
                className="rounded-lg border border-border bg-card px-6 py-3 text-sm font-semibold text-foreground transition-colors hover:bg-secondary"
              >
                Anmelden
              </Link>
            </div>
          </div>
        </div>
      </section>

      {/* Footer */}
      <footer className="border-t border-border">
        <div className="mx-auto flex max-w-6xl flex-col items-center justify-between gap-4 px-5 py-8 text-sm text-muted-foreground sm:flex-row sm:px-6">
          <Logo />
          <p>© {new Date().getFullYear()} Numera · Rechnungen &amp; E-Rechnung für deutsche Unternehmen</p>
          <div className="flex items-center gap-5">
            <span>GoBD-konform</span>
            <span>EN 16931</span>
            <span>DSGVO</span>
          </div>
        </div>
      </footer>
    </div>
  )
}
