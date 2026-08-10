import { useState, type ComponentType, type SVGProps } from 'react'
import { Routes, Route, Navigate, Link, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { getMe } from './lib/api'
import { useTheme } from './lib/useTheme'
import { cn } from './lib/utils'
import LanguageSwitcher from './components/LanguageSwitcher'
import {
  IconDashboard,
  IconDocuments,
  IconOpenItems,
  IconDunning,
  IconRecurring,
  IconInbound,
  IconPartners,
  IconCatalog,
  IconSettings,
  IconTeam,
  IconSun,
  IconMoon,
  IconMenu,
  IconClose,
} from './components/icons'
import Landing from './pages/Landing'
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
import OpenItemsListPage from './features/openItems/OpenItemsListPage'
import InboundListPage from './features/inbound/InboundListPage'
import InboundDetailPage from './features/inbound/InboundDetailPage'
import CompanyProfileSettingsPage from './features/settings/CompanyProfileSettingsPage'
import DunningConfigSettingsPage from './features/dunning/DunningConfigSettingsPage'
import RecurringTemplateListPage from './features/recurring/RecurringTemplateListPage'
import RecurringTemplateFormPage from './features/recurring/RecurringTemplateFormPage'
import TeamPage from './features/team/TeamPage'
import UstVaPruefansichtPage from './features/reports/UstVaPruefansichtPage'
import EuerReportPage from './features/reports/EuerReportPage'
import BelegReviewQueuePage from './features/belege/BelegReviewQueuePage'
import BelegCapturePage from './features/belege/BelegCapturePage'
import BelegReviewPage from './features/belege/BelegReviewPage'
import BankAccountsPage from './features/banking/BankAccountsPage'
import ReconciliationQueuePage from './features/banking/ReconciliationQueuePage'
import ReconciliationMatchPage from './features/banking/ReconciliationMatchPage'
import BillingPage from './features/billing/BillingPage'
import DegradationBanner from './features/billing/DegradationBanner'

type Icon = ComponentType<SVGProps<SVGSVGElement> & { size?: number }>
interface NavItem {
  to: string
  label: string
  icon: Icon
}
interface NavGroup {
  label?: string
  items: NavItem[]
}

function useNavGroups(): NavGroup[] {
  const { t } = useTranslation('common')
  const { t: tp } = useTranslation('partners')
  const { t: tc } = useTranslation('catalog')
  const { t: td } = useTranslation('documents')
  const { t: to } = useTranslation('openItems')
  const { t: ti } = useTranslation('inbound')
  const { t: ts } = useTranslation('settings')
  const { t: tdu } = useTranslation('dunning')
  const { t: tr } = useTranslation('recurring')
  const { t: tt } = useTranslation('team')
  const { t: trep } = useTranslation('reports')
  const { t: tb } = useTranslation('belege')
  const { t: tbank } = useTranslation('banking')
  const { t: tbilling } = useTranslation('billing')

  return [
    { items: [{ to: '/dashboard', label: t('nav.dashboard'), icon: IconDashboard }] },
    {
      label: t('nav.sections.sales'),
      items: [
        { to: '/documents', label: td('nav'), icon: IconDocuments },
        { to: '/open-items', label: to('nav'), icon: IconOpenItems },
        { to: '/settings/dunning', label: tdu('nav'), icon: IconDunning },
        { to: '/recurring', label: tr('nav'), icon: IconRecurring },
        { to: '/inbound', label: ti('nav'), icon: IconInbound },
      ],
    },
    {
      label: tb('nav.group'),
      items: [
        { to: '/belege', label: tb('nav.queue'), icon: IconDocuments },
        { to: '/belege/capture', label: tb('nav.capture'), icon: IconInbound },
      ],
    },
    {
      label: tbank('nav.group'),
      items: [
        { to: '/banking', label: tbank('nav.accounts'), icon: IconOpenItems },
        { to: '/banking/queue', label: tbank('nav.queue'), icon: IconDashboard },
      ],
    },
    {
      label: trep('nav.group'),
      items: [
        { to: '/reports/ustva', label: trep('nav.ustva'), icon: IconDocuments },
        { to: '/reports/euer', label: trep('nav.euer'), icon: IconDashboard },
      ],
    },
    {
      label: t('nav.sections.masterData'),
      items: [
        { to: '/partners', label: tp('nav'), icon: IconPartners },
        { to: '/catalog', label: tc('nav'), icon: IconCatalog },
      ],
    },
    {
      label: t('nav.sections.system'),
      items: [
        { to: '/settings', label: ts('nav'), icon: IconSettings },
        { to: '/billing', label: tbilling('nav.billing'), icon: IconOpenItems },
        { to: '/team', label: tt('nav'), icon: IconTeam },
      ],
    },
  ]
}

// The nav item whose path is the longest prefix of the current route (so
// /settings/dunning resolves to Dunning, not Settings; /documents/:id to Belege).
function activePath(items: NavItem[], pathname: string): string | null {
  let best: string | null = null
  for (const it of items) {
    if (pathname === it.to || pathname.startsWith(it.to + '/')) {
      if (!best || it.to.length > best.length) best = it.to
    }
  }
  return best
}

function BrandMark() {
  const { t } = useTranslation('common')
  return (
    <Link to="/dashboard" className="flex items-center gap-2.5 px-1 group">
      <span
        className="grid h-9 w-9 place-items-center rounded-[0.7rem] bg-primary text-primary-foreground text-[1.05rem] font-bold shadow-sm"
        aria-hidden="true"
      >
        N
      </span>
      <span className="flex flex-col leading-none">
        <span className="text-[1.05rem] font-bold tracking-tight text-sidebar-foreground">
          {t('app.name')}
        </span>
        <span className="mt-1 text-[0.7rem] font-medium text-sidebar-muted">
          {t('app.tagline')}
        </span>
      </span>
    </Link>
  )
}

function SidebarNav({ onNavigate }: { onNavigate?: () => void }) {
  const location = useLocation()
  const groups = useNavGroups()
  const active = activePath(
    groups.flatMap((g) => g.items),
    location.pathname,
  )

  return (
    <nav className="flex flex-1 flex-col gap-6 overflow-y-auto px-3 py-5">
      {groups.map((group, gi) => (
        <div key={gi} className="flex flex-col gap-1">
          {group.label && (
            <p className="px-3 pb-1 text-[0.68rem] font-semibold uppercase tracking-[0.09em] text-sidebar-muted">
              {group.label}
            </p>
          )}
          {group.items.map((item) => {
            const isActive = active === item.to
            const Ico = item.icon
            return (
              <Link
                key={item.to}
                to={item.to}
                onClick={onNavigate}
                aria-current={isActive ? 'page' : undefined}
                className={cn(
                  'group relative flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
                  isActive
                    ? 'bg-sidebar-accent text-sidebar-accent-foreground'
                    : 'text-sidebar-foreground/75 hover:bg-sidebar-accent/50 hover:text-sidebar-foreground',
                )}
              >
                {isActive && (
                  <span className="absolute left-0 top-1/2 h-5 w-[3px] -translate-y-1/2 rounded-full bg-primary" />
                )}
                <Ico
                  size={19}
                  className={cn(
                    'shrink-0 transition-colors',
                    isActive ? 'text-primary' : 'text-sidebar-muted group-hover:text-sidebar-foreground',
                  )}
                />
                <span className="truncate">{item.label}</span>
              </Link>
            )
          })}
        </div>
      ))}
    </nav>
  )
}

function ThemeToggle() {
  const { theme, toggle } = useTheme()
  const { t } = useTranslation('common')
  const dark = theme === 'dark'
  return (
    <button
      type="button"
      onClick={toggle}
      aria-label={dark ? t('ui.themeLight') : t('ui.themeDark')}
      title={dark ? t('ui.themeLight') : t('ui.themeDark')}
      className="grid h-9 w-9 place-items-center rounded-lg border border-border bg-card text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground"
    >
      {dark ? <IconSun size={18} /> : <IconMoon size={18} />}
    </button>
  )
}

function PlanBadge() {
  const { t } = useTranslation('common')
  const me = useQuery({ queryKey: ['me'], queryFn: getMe })
  const plan = me.data?.tenant?.plan
  if (!plan) return null
  return (
    <span
      title={t('plan.indicator', { plan })}
      className="hidden items-center gap-1.5 rounded-full border border-primary/25 bg-primary/8 px-2.5 py-1 text-xs font-semibold text-primary sm:inline-flex tnum"
    >
      <span className="h-1.5 w-1.5 rounded-full bg-primary" />
      {t('plan.badge', { plan })}
    </span>
  )
}

function UserChip() {
  const me = useQuery({ queryKey: ['me'], queryFn: getMe })
  const email = me.data?.user?.email ?? ''
  const initial = (email.trim()[0] ?? '·').toUpperCase()
  return (
    <div className="flex items-center gap-2 pl-1">
      <span
        className="grid h-8 w-8 place-items-center rounded-full bg-secondary text-xs font-semibold text-secondary-foreground"
        aria-hidden="true"
      >
        {initial}
      </span>
      <span className="hidden max-w-[13rem] truncate text-sm text-muted-foreground lg:block">
        {email}
      </span>
    </div>
  )
}

function Topbar({ title, onOpenMenu }: { title: string; onOpenMenu: () => void }) {
  const { t } = useTranslation('common')
  return (
    <header className="sticky top-0 z-30 flex h-15 items-center gap-3 border-b border-border bg-card/85 px-4 backdrop-blur supports-[backdrop-filter]:bg-card/70 sm:px-6">
      <button
        type="button"
        onClick={onOpenMenu}
        aria-label={t('ui.openMenu')}
        className="grid h-9 w-9 place-items-center rounded-lg border border-border text-muted-foreground md:hidden"
      >
        <IconMenu size={18} />
      </button>
      <h1 className="mr-auto truncate text-[0.95rem] font-semibold text-foreground">
        {title}
      </h1>
      <PlanBadge />
      <LanguageSwitcher />
      <ThemeToggle />
      <UserChip />
    </header>
  )
}

function AppShell() {
  const location = useLocation()
  const groups = useNavGroups()
  const [mobileOpen, setMobileOpen] = useState(false)

  const active = activePath(groups.flatMap((g) => g.items), location.pathname)
  const title =
    groups.flatMap((g) => g.items).find((i) => i.to === active)?.label ?? 'Numera'

  return (
    <div className="min-h-dvh">
      {/* Desktop sidebar (fixed) + mobile drawer */}
      {mobileOpen && (
        <button
          type="button"
          aria-hidden="true"
          tabIndex={-1}
          onClick={() => setMobileOpen(false)}
          className="fixed inset-0 z-40 bg-foreground/40 backdrop-blur-sm md:hidden"
        />
      )}
      <aside
        className={cn(
          'fixed inset-y-0 left-0 z-50 flex w-[264px] flex-col border-r border-sidebar-border bg-sidebar transition-transform duration-200 md:translate-x-0',
          mobileOpen ? 'translate-x-0 shadow-2xl' : '-translate-x-full',
        )}
      >
        <div className="flex h-15 items-center justify-between border-b border-sidebar-border px-4">
          <BrandMark />
          <button
            type="button"
            onClick={() => setMobileOpen(false)}
            className="grid h-8 w-8 place-items-center rounded-lg text-sidebar-muted md:hidden"
            aria-label="Menü schließen"
          >
            <IconClose size={18} />
          </button>
        </div>
        <SidebarNav onNavigate={() => setMobileOpen(false)} />
      </aside>

      {/* Content column */}
      <div className="flex min-h-dvh flex-col md:pl-[264px]">
        <Topbar title={title} onOpenMenu={() => setMobileOpen(true)} />
        <DegradationBanner />
        <div className="flex-1">
          <Routes>
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
            <Route path="/open-items" element={<OpenItemsListPage />} />
            <Route path="/inbound" element={<InboundListPage />} />
            <Route path="/inbound/:id" element={<InboundDetailPage />} />
            <Route path="/settings" element={<CompanyProfileSettingsPage />} />
            <Route path="/settings/dunning" element={<DunningConfigSettingsPage />} />
            <Route path="/recurring" element={<RecurringTemplateListPage />} />
            <Route path="/recurring/new" element={<RecurringTemplateFormPage />} />
            <Route path="/recurring/:id/edit" element={<RecurringTemplateFormPage />} />
            <Route path="/team" element={<TeamPage />} />
            <Route path="/reports/ustva" element={<UstVaPruefansichtPage />} />
            <Route path="/reports/euer" element={<EuerReportPage />} />
            <Route path="/belege" element={<BelegReviewQueuePage />} />
            <Route path="/belege/capture" element={<BelegCapturePage />} />
            <Route path="/belege/:id" element={<BelegReviewPage />} />
            <Route path="/banking" element={<BankAccountsPage />} />
            <Route path="/banking/queue" element={<ReconciliationQueuePage />} />
            <Route path="/banking/tx/:id" element={<ReconciliationMatchPage />} />
            <Route path="/billing" element={<BillingPage />} />
            <Route path="/billing/success" element={<BillingPage />} />
            <Route path="/billing/cancel" element={<BillingPage />} />
            <Route path="/" element={<Navigate to="/dashboard" replace />} />
            <Route path="*" element={<Navigate to="/dashboard" replace />} />
          </Routes>
        </div>
      </div>
    </div>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Landing />} />
      <Route path="/login" element={<Login />} />
      <Route path="/*" element={<AppShell />} />
    </Routes>
  )
}
