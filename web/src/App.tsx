import { Routes, Route, Navigate, Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import LanguageSwitcher from './components/LanguageSwitcher'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import PartnerListPage from './features/partners/PartnerListPage'
import PartnerFormPage from './features/partners/PartnerFormPage'
import PartnerDetailPage from './features/partners/PartnerDetailPage'
import CatalogListPage from './features/catalog/CatalogListPage'
import CatalogFormPage from './features/catalog/CatalogFormPage'
import DocumentListPage from './features/documents/DocumentListPage'
import DocumentFormPage from './features/documents/DocumentFormPage'
import DocumentDetailPage from './features/documents/DocumentDetailPage'

// App-shell chrome shared by every route: brand + primary nav + language switch,
// reachable from Login, Dashboard and the partner pages.
function AppHeader() {
  const { t } = useTranslation('common')
  const { t: tp } = useTranslation('partners')
  const { t: tc } = useTranslation('catalog')
  const { t: td } = useTranslation('documents')
  return (
    <header className="app-header">
      <span className="brand">{t('app.name')}</span>
      <nav style={{ display: 'flex', gap: '1rem', marginRight: 'auto', marginLeft: '1.5rem' }}>
        <Link to="/dashboard">{t('dashboard.title')}</Link>
        <Link to="/partners">{tp('nav')}</Link>
        <Link to="/catalog">{tc('nav')}</Link>
        <Link to="/documents">{td('nav')}</Link>
      </nav>
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
        <Route path="/partners" element={<PartnerListPage />} />
        <Route path="/partners/new" element={<PartnerFormPage />} />
        <Route path="/partners/:id/edit" element={<PartnerFormPage />} />
        <Route path="/partners/:id" element={<PartnerDetailPage />} />
        <Route path="/catalog" element={<CatalogListPage />} />
        <Route path="/catalog/new" element={<CatalogFormPage />} />
        <Route path="/catalog/:id" element={<CatalogFormPage />} />
        <Route path="/documents" element={<DocumentListPage />} />
        <Route path="/documents/new" element={<DocumentFormPage />} />
        <Route path="/documents/:id/edit" element={<DocumentFormPage />} />
        <Route path="/documents/:id" element={<DocumentDetailPage />} />
        <Route path="/" element={<Navigate to="/dashboard" replace />} />
        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </>
  )
}
