import type { ComponentType, SVGProps } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { getMe } from '../lib/api'
import { useEntitlements } from '../lib/entitlements'
import { useOnline } from '../lib/useOnline'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { AreaChart, Donut, Legend, Sparkline, type Segment } from '../components/charts'
import { IconOpenItems, IconDocuments, IconDunning } from '../components/icons'

// --- Illustrative sample data (clearly labelled in the UI) ------------------
// Numera has no aggregate/stats endpoint yet; until real documents exist the
// overview renders this sample set so the layout + charts are visible. The same
// components consume real figures once an aggregates API is wired.
const REVENUE = [4200, 3800, 5100, 4700, 6200, 5800, 7100, 6600, 8200, 7400, 9100, 9800]

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

  const eur0 = new Intl.NumberFormat(loc, {
    style: 'currency',
    currency: 'EUR',
    maximumFractionDigits: 0,
  })
  const money = (v: number) => eur0.format(v)

  const revenueYear = REVENUE.reduce((a, b) => a + b, 0)
  const aging: Segment[] = [
    { label: t('dashboard.aging.notDue'), value: 6800, color: '#0c7c6d' },
    { label: t('dashboard.aging.d1_30'), value: 3450, color: '#f59e0b' },
    { label: t('dashboard.aging.d31_60'), value: 1400, color: '#fb7a45' },
    { label: t('dashboard.aging.d60plus'), value: 800, color: '#ef4444' },
  ]
  const agingTotal = aging.reduce((a, s) => a + s.value, 0)
  const overdue = agingTotal - aging[0].value
  const docStatus: Segment[] = [
    { label: t('dashboard.docStatus.paid'), value: 58, color: '#0c7c6d' },
    { label: t('dashboard.docStatus.finalized'), value: 22, color: '#3b82f6' },
    { label: t('dashboard.docStatus.draft'), value: 14, color: '#94a3b8' },
    { label: t('dashboard.docStatus.cancelled'), value: 6, color: '#ef4444' },
  ]
  const docTotal = docStatus.reduce((a, s) => a + s.value, 0)

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

      {/* Sample-data notice */}
      <div className="mb-6 flex items-center gap-2 rounded-lg border border-warning/30 bg-warning/10 px-3.5 py-2.5 text-sm text-warning">
        <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-warning" />
        {t('dashboard.demoNotice')}
      </div>

      {/* KPI row */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label={t('dashboard.kpi.revenueYear')}
          value={money(revenueYear)}
          delta="+12,4 %"
          up
          icon={IconDocuments}
          spark={REVENUE}
        />
        <StatCard
          label={t('dashboard.kpi.openItems')}
          value={money(agingTotal)}
          delta="+3,1 %"
          up
          icon={IconOpenItems}
          spark={[5, 6, 5.5, 7, 6.8, 8, 9, 8.6, 10, 11, 11.6, 12.4]}
        />
        <StatCard
          label={t('dashboard.kpi.overdue')}
          value={money(overdue)}
          delta="-8,0 %"
          up={false}
          icon={IconDunning}
          spark={[6, 5.5, 6.2, 5, 5.4, 4.8, 5.2, 4.6, 4.9, 4.2, 4.6, 4.25]}
        />
        <StatCard
          label={t('dashboard.kpi.documentsMonth')}
          value="24"
          delta="+5"
          up
          icon={IconDocuments}
          spark={[12, 14, 13, 16, 15, 18, 17, 19, 18, 21, 22, 24]}
        />
      </div>

      {/* Charts */}
      <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>{t('dashboard.charts.revenue')}</CardTitle>
          </CardHeader>
          <CardContent>
            <AreaChart data={REVENUE} labels={lastMonths(12, loc)} formatValue={money} />
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
  delta,
  up,
  icon: Icon,
  spark,
}: {
  label: string
  value: string
  delta: string
  up: boolean
  icon: ComponentType<SVGProps<SVGSVGElement> & { size?: number }>
  spark: number[]
}) {
  return (
    <Card>
      <CardContent className="flex flex-col gap-3 pt-5">
        <div className="flex items-center justify-between">
          <span className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
            <Icon size={16} className="text-primary" />
            {label}
          </span>
        </div>
        <div className="flex items-end justify-between gap-2">
          <div>
            <p className="text-2xl font-bold tracking-tight text-foreground tnum">{value}</p>
            <span
              className={
                'mt-1 inline-flex items-center gap-1 text-xs font-semibold ' +
                (up ? 'text-primary' : 'text-destructive')
              }
            >
              {up ? '▲' : '▼'} {delta}
            </span>
          </div>
          <div className={'h-9 w-24 ' + (up ? 'text-primary' : 'text-destructive')}>
            <Sparkline data={spark} className="h-full w-full" strokeClass="" />
          </div>
        </div>
      </CardContent>
    </Card>
  )
}
