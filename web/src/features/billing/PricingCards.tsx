import { useState } from 'react'
import { useTranslation } from 'react-i18next'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { ApiError } from '@/lib/api'
import type { PlanName } from '@/lib/entitlements'
import { cn } from '@/lib/utils'

import { startCheckout } from './billingApi'

const PAID_PLANS: readonly PlanName[] = ['S', 'M', 'L', 'XL']

function problemDetail(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null
  const body = error.body as
    | { detail?: string; error?: string; title?: string; errors?: Record<string, string[]> }
    | undefined
  return (
    body?.detail ??
    body?.error ??
    Object.values(body?.errors ?? {})[0]?.[0] ??
    body?.title ??
    null
  )
}

export interface PricingCardsProps {
  currentPlan: PlanName
}

export default function PricingCards({ currentPlan }: PricingCardsProps) {
  const { t } = useTranslation('billing')
  const [pendingPlan, setPendingPlan] = useState<PlanName | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function choosePlan(plan: PlanName) {
    setError(null)
    setPendingPlan(plan)
    try {
      await startCheckout(plan)
    } catch (checkoutError) {
      setError(problemDetail(checkoutError) ?? t('messages.checkoutError'))
      setPendingPlan(null)
    }
  }

  return (
    <section aria-labelledby="pricing-heading">
      <div className="mb-4">
        <h2 id="pricing-heading" className="text-lg font-semibold">
          {t('plans.title')}
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">{t('plans.subtitle')}</p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {PAID_PLANS.map((plan) => {
          const isCurrent = plan === currentPlan
          const isPending = plan === pendingPlan
          return (
            <Card
              key={plan}
              className={cn(
                'relative flex min-h-52 flex-col overflow-hidden',
                isCurrent && 'border-primary ring-1 ring-primary/25',
              )}
            >
              <div className={cn('h-1 bg-border', isCurrent && 'bg-primary')} />
              <CardHeader className="flex-row items-start justify-between gap-3">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted-foreground">
                    {t('plans.tier')}
                  </p>
                  <CardTitle className="mt-2 text-3xl tnum">
                    {t(`plans.labels.${plan}`)}
                  </CardTitle>
                </div>
                {isCurrent && <Badge variant="secondary">{t('plans.current')}</Badge>}
              </CardHeader>
              <CardContent className="mt-auto">
                <p className="mb-5 min-h-10 text-sm text-muted-foreground">
                  {t(`plans.descriptions.${plan}`)}
                </p>
                <Button
                  className="w-full"
                  variant={isCurrent ? 'secondary' : 'default'}
                  disabled={isCurrent || pendingPlan !== null}
                  onClick={() => void choosePlan(plan)}
                >
                  {isCurrent
                    ? t('buttons.current')
                    : isPending
                      ? t('buttons.redirecting')
                      : currentPlan === 'Free'
                        ? t('buttons.start')
                        : t('buttons.switch')}
                </Button>
              </CardContent>
            </Card>
          )
        })}
      </div>

      {error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          {error}
        </p>
      )}
    </section>
  )
}
