import { Link, Navigate, useParams } from 'react-router-dom'
import { CHAPTERS, chapterBySlug } from './handbuchContent'

// Benutzerhandbuch — a single chapter subpage (/handbuch/:slug). Renders the chapter
// body plus breadcrumb and previous/next navigation. Unknown slugs redirect to the
// overview.

export default function ManualChapterPage() {
  const { slug } = useParams<{ slug: string }>()
  const chapter = chapterBySlug(slug)
  if (!chapter) {
    return <Navigate to="/handbuch" replace />
  }

  const index = CHAPTERS.findIndex((c) => c.slug === chapter.slug)
  const prev = index > 0 ? CHAPTERS[index - 1] : null
  const next = index < CHAPTERS.length - 1 ? CHAPTERS[index + 1] : null
  const { Body } = chapter

  return (
    <main className="app-main" style={{ maxWidth: '920px' }}>
      <nav className="mb-4 text-xs text-muted-foreground">
        <Link to="/handbuch" className="text-primary hover:underline">
          Benutzerhandbuch
        </Link>
        <span className="px-1.5">/</span>
        <span>{chapter.title}</span>
      </nav>

      <header className="mb-2 border-b border-border pb-3">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          Kapitel {chapter.number} von {CHAPTERS.length}
        </p>
        <h1 className="mt-1 text-2xl font-semibold text-foreground">{chapter.title}</h1>
      </header>

      <article className="pb-8">
        <Body />
      </article>

      <nav className="mt-8 grid gap-3 border-t border-border pt-5 sm:grid-cols-2">
        {prev ? (
          <Link
            to={`/handbuch/${prev.slug}`}
            className="rounded-lg border border-border bg-card p-3 transition-colors hover:border-primary/40 hover:bg-primary/5"
          >
            <span className="block text-xs text-muted-foreground">← Zurück</span>
            <span className="mt-0.5 block text-sm font-medium text-foreground">
              {prev.number}. {prev.title}
            </span>
          </Link>
        ) : (
          <span />
        )}
        {next ? (
          <Link
            to={`/handbuch/${next.slug}`}
            className="rounded-lg border border-border bg-card p-3 text-right transition-colors hover:border-primary/40 hover:bg-primary/5"
          >
            <span className="block text-xs text-muted-foreground">Weiter →</span>
            <span className="mt-0.5 block text-sm font-medium text-foreground">
              {next.number}. {next.title}
            </span>
          </Link>
        ) : (
          <span />
        )}
      </nav>

      <p className="mt-6 text-center">
        <Link to="/handbuch" className="text-sm text-primary hover:underline">
          Zur Kapitelübersicht
        </Link>
      </p>
    </main>
  )
}
