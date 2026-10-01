import type { ComponentType, SVGProps } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { getMe, getDashboard } from '../lib/api'
import { useEntitlements } from '../lib/entitlements'
import { useOnline } from '../lib/useOnline'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { AreaChart, Donut, Legend, type Segment } from '../components/charts'
import { IconOpenItems, IconDocuments, IconDunning } from '../components/icons'

function lastMonths(count: number, loc: string): string[] {
  const fmt = new Intl.DateTimeFormat(loc, { month: 'short' })
  const now = new Date()
  return Array.from({ length: count }, (_, i) => {
    const d = new Date(now.getFullYear(), now.getMonth() - (count - 1 - i), 1)
    return fmt.format(d).replace('.', '')
  })
}

export default function Dashboard() {
  const { t, i18n } = useTranslation('common')
  const online = useOnline()
  const loc = (i18n.resolvedLanguage ?? 'de') === 'en' ? 'en-GB' : 'de-DE'

  const me = useQuery({ queryKey: ['me'], queryFn: getMe, enabled: online })
  const caps = useEntitlements()
  const dash = useQuery({
    queryKey: ['dashboard'],
    queryFn: getDashboard,
    enabled: online,
  })

  const eur0 = new Intl.NumberFormat(loc, {
    style: 'currency',
    currency: 'EUR',
    maximumFractionDigits: 0,
  })
  const money = (v: number) => eur0.format(v)

  const d = dash.data
  const revenue = d?.revenueByMonth.map((m) => m.net) ?? Array(12).fill(0)

  const aging: Segment[] = [
    { label: t('dashboard.aging.notDue'), value: d?.aging.notDue ?? 0, color: '#0c7c6d' },
    { label: t('dashboard.aging.d1_30'), value: d?.aging.d1_30 ?? 0, color: '#f59e0b' },
    { label: t('dashboard.aging.d31_60'), value: d?.aging.d31_60 ?? 0, color: '#fb7a45' },
    { label: t('dashboard.aging.d60plus'), value: d?.aging.d60Plus ?? 0, color: '#ef4444' },
  ]
  const agingTotal = d?.openItemsTotal ?? 0

  const docStatus: Segment[] = [
    { label: t('dashboard.docStatus.paid'), value: d?.docStatus.paid ?? 0, color: '#0c7c6d' },
    { label: t('dashboard.docStatus.finalized'), value: d?.docStatus.finalized ?? 0, color: '#3b82f6' },
    { label: t('dashboard.docStatus.draft'), value: d?.docStatus.draft ?? 0, color: '#94a3b8' },
    { label: t('dashboard.docStatus.cancelled'), value: d?.docStatus.cancelled ?? 0, color: '#ef4444' },
  ]
  const docTotal = docStatus.reduce((a, s) => a + s.value, 0)
  const isEmpty = dash.isSuccess && docTotal === 0 && agingTotal === 0 && (d?.revenueYear ?? 0) === 0

  const name = me.data?.user?.email?.split('@')[0]
  const plan = me.data?.tenant?.plan
  const capCount = caps.data?.capabilities.length ?? 0

  return (
    <main className="app-main">
      {!online && <div className="offline-banner mb-6 rounded-lg">{t('offline.banner')}</div>}

      {/* Page header */}
      <div className="mb-6 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-foreground">
            {name ? t('dashboard.welcome', { name }) : t('dashboard.title')}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">{t('dashboard.subtitle')}</p>
        </div>
        <span className="rounded-lg border border-border bg-card px-3 py-1.5 text-xs font-medium text-muted-foreground">
          {t('dashboard.period')}
        </span>
      </div>

      {/* Empty-state notice — only until the first documents exist. */}
      {isEmpty && (
        <div className="mb-6 flex items-center gap-2 rounded-lg border border-border bg-muted px-3.5 py-2.5 text-sm text-muted-foreground">
          <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-muted-foreground/60" />
          {t('dashboard.emptyNotice')}
        </div>
      )}

      {/* KPI row */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label={t('dashboard.kpi.revenueYear')}
          value={money(d?.revenueYear ?? 0)}
          icon={IconDocuments}
        />
        <StatCard
          label={t('dashboard.kpi.openItems')}
          value={money(agingTotal)}
          icon={IconOpenItems}
        />
        <StatCard
          label={t('dashboard.kpi.overdue')}
          value={money(d?.overdueTotal ?? 0)}
          icon={IconDunning}
          accentDestructive={(d?.overdueTotal ?? 0) > 0}
        />
        <StatCard
          label={t('dashboard.kpi.documentsMonth')}
          value={String(d?.documentsThisMonth ?? 0)}
          icon={IconDocuments}
        />
      </div>

      {/* Charts */}
      <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>{t('dashboard.charts.revenue')}</CardTitle>
          </CardHeader>
          <CardContent>
            <AreaChart data={revenue} labels={lastMonths(12, loc)} formatValue={money} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t('dashboard.charts.aging')}</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col items-center gap-5">
            <Donut
              segments={aging}
              centerLabel={money(agingTotal)}
              centerSub={t('dashboard.aging.center')}
            />
            <div className="w-full">
              <Legend segments={aging} formatValue={money} />
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Status + plan */}
      <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle>{t('dashboard.charts.status')}</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col items-center gap-5">
            <Donut
              segments={docStatus}
              centerLabel={String(docTotal)}
              centerSub={t('dashboard.docStatus.center')}
            />
            <div className="w-full">
              <Legend segments={docStatus} />
            </div>
          </CardContent>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>{t('dashboard.planCard.title')}</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-4">
              <span className="grid h-14 w-14 place-items-center rounded-xl bg-primary/10 text-xl font-bold text-primary tnum">
                {plan ?? '—'}
              </span>
              <div>
                <p className="text-lg font-semibold text-foreground">
                  {plan ? t('plan.badge', { plan }) : t('dashboard.planCard.unknown')}
                </p>
                <p className="text-sm text-muted-foreground">
                  {t('dashboard.planCard.capabilities', { count: capCount })}
                </p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    </main>
  )
}

function StatCard({
  label,
  value,
  icon: Icon,
  accentDestructive,
}: {
  label: string
  value: string
  icon: ComponentType<SVGProps<SVGSVGElement> & { size?: number }>
  accentDestructive?: boolean
}) {
  return (
    <Card>
      <CardContent className="flex flex-col gap-3 pt-5">
        <span className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
          <Icon size={16} className="text-primary" />
          {label}
        </span>
        <p
          className={
            'text-2xl font-bold tracking-tight tnum ' +
            (accentDestructive ? 'text-destructive' : 'text-foreground')
          }
        >
          {value}
        </p>
      </CardContent>
    </Card>
  )
}
