import { Link } from 'react-router-dom'
import { CHAPTERS } from './handbuchContent'

// Benutzerhandbuch — overview page (/handbuch). Lists all chapters as cards that
// link to their subpage (/handbuch/:slug). Content lives in handbuchContent.tsx.

export default function UserManualPage() {
  return (
    <main className="app-main" style={{ maxWidth: '920px' }}>
      <header className="mb-6">
        <h1 className="text-2xl font-semibold text-foreground">Benutzerhandbuch</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          Diese Anleitung führt Sie durch alle Bereiche von Numera — vom Einrichten der Stammdaten
          und des Kontenrahmens über die rechtskonforme Rechnung und E-Rechnung bis zu Offenen
          Posten, Mahnwesen, Eingangsbelegen, E-Mail-Versand und den steuerlichen Auswertungen.
          Wählen Sie ein Kapitel, um die ausführliche Schritt-für-Schritt-Anleitung zu öffnen.
        </p>
      </header>

      <ol className="grid gap-3 sm:grid-cols-2">
        {CHAPTERS.map((c) => (
          <li key={c.slug}>
            <Link
              to={`/handbuch/${c.slug}`}
              className="block h-full rounded-lg border border-border bg-card p-4 transition-colors hover:border-primary/40 hover:bg-primary/5"
            >
              <div className="flex items-baseline gap-2">
                <span className="text-xs font-semibold text-muted-foreground">
                  {c.number}.
                </span>
                <span className="text-sm font-semibold text-foreground">{c.title}</span>
              </div>
              <p className="mt-1.5 text-xs leading-relaxed text-muted-foreground">
                {c.summary}
              </p>
            </Link>
          </li>
        ))}
      </ol>

      <p className="mt-8 border-t border-border pt-4 text-xs text-muted-foreground">
        Hinweis: „GoBD-konform" bedeutet, dass Numera nach den GoBD-Grundsätzen gebaut ist. Eine
        „GoBD-Zertifizierung" gibt es nicht und wird nicht behauptet. Für steuerliche Einzelfragen
        wenden Sie sich an Ihren Steuerberater.
      </p>
    </main>
  )
}
