import { Routes, Route, Navigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import LanguageSwitcher from './components/LanguageSwitcher'

// App-shell chrome shared by every route: brand + language switch reachable
// from both Login and Dashboard.
function AppHeader() {
  const { t } = useTranslation('common')
  return (
    <header className="app-header">
      <span className="brand">{t('app.name')}</span>
      <LanguageSwitcher />
    </header>
  )
}

// Placeholder shell used until the real pages are wired (Task 3).
function Placeholder({ titleKey }: { titleKey: string }) {
  const { t } = useTranslation('common')
  return (
    <main className="app-main">
      <h1>{t(titleKey)}</h1>
      <p className="muted">{t('app.tagline')}</p>
    </main>
  )
}

export default function App() {
  return (
    <>
      <AppHeader />
      <Routes>
        <Route path="/login" element={<Placeholder titleKey="nav.login" />} />
        <Route path="/dashboard" element={<Placeholder titleKey="dashboard.title" />} />
        <Route path="/" element={<Navigate to="/dashboard" replace />} />
        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </>
  )
}
