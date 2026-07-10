import { Routes, Route, Navigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import LanguageSwitcher from './components/LanguageSwitcher'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'

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

export default function App() {
  return (
    <>
      <AppHeader />
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/dashboard" element={<Dashboard />} />
        <Route path="/" element={<Navigate to="/dashboard" replace />} />
        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </>
  )
}
