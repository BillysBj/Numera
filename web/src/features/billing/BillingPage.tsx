import { useEffect, useMemo, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useLocation, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { ApiError } from '@/lib/api'

import { openPortal, useBillingStatus } from './billingApi'
import PricingCards from './PricingCards'

const DAY_IN_MS = 24 * 60 * 60 * 1000

function remainingTrialDays(trialEndsAt: string | null): number {
  if (!trialEndsAt) return 0
  const end = new Date(trialEndsAt).getTime()
  if (Number.isNaN(end)) return 0
  return Math.max(0, Math.ceil((end - Date.now()) / DAY_IN_MS))
}

export default function BillingPage() {
  const { t } = useTranslation('billing')
  const location = useLocation()
  const [searchParams] = useSearchParams()
  const queryClient = useQueryClient()
  const billing = useBillingStatus()
  const [portalPending, setPortalPending] = useState(false)
  const [portalError, setPortalError] = useState<string | null>(null)

  const sessionId = searchParams.get('session_id')
  const checkoutSucceeded = location.pathname === '/billing/success' && Boolean(sessionId)
  const checkoutCanceled = location.pathname === '/billing/cancel'

  useEffect(() => {
    if (!checkoutSucceeded) return
    void Promise.all([
      queryClient.invalidateQueries({ queryKey: ['billing-status'] }),
      queryClient.invalidateQueries({ queryKey: ['entitlements'] }),
    ])
  }, [checkoutSucceeded, queryClient, sessionId])

  const trialDays = useMemo(
    () => remainingTrialDays(billing.data?.trialEndsAt ?? null),
    [billing.data?.trialEndsAt],
  )

  async function manageBilling() {
    setPortalError(null)
    setPortalPending(true)
    try {
      await openPortal()
    } catch (portalRequestError) {
      setPortalError(
        portalRequestError instanceof ApiError && portalRequestError.status === 409
          ? t('messages.portalRequiresPlan')
          : t('messages.portalError'),
      )
      setPortalPending(false)
    }
  }

  return (
    <main className="app-main" style={{ maxWidth: '1200px' }}>
      <div className="mb-6">
        <h1 className="text-2xl font-semibold">{t('page.title')}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t('page.subtitle')}</p>
      </div>

      {checkoutSucceeded && (
        <div
          role="status"
          aria-live="polite"
          className="mb-5 rounded-lg border border-primary/30 bg-primary/8 px-4 py-3 text-sm font-medium text-primary"
        >
          {t('messages.success')}
        </div>
      )}
      {checkoutCanceled && (
        <div
          role="status"
          className="mb-5 rounded-lg border border-border bg-card px-4 py-3 text-sm text-muted-foreground"
        >
          {t('messages.cancel')}
        </div>
      )}

      {billing.isLoading && (
        <p className="mb-5 text-sm text-muted-foreground">{t('page.loading')}</p>
      )}
      {billing.isError && (
        <p role="alert" className="mb-5 text-sm text-destructive">
          {t('page.loadError')}
        </p>
      )}

      {billing.data && (
        <>
          <Card className="mb-7 overflow-hidden">
            <div className="h-1 bg-primary" />
            <CardHeader className="flex-row flex-wrap items-start justify-between gap-4 border-b border-border bg-muted/35">
              <div>
                <p className="text-xs font-semibold uppercase tracking-[0.1em] text-muted-foreground">
                  {t('page.currentPlan')}
                </p>
                <CardTitle className="mt-2 text-2xl">
                  {t(`plans.labels.${billing.data.plan}`)}
                </CardTitle>
              </div>
              <Badge variant={billing.data.degraded ? 'outline' : 'secondary'}>
                {billing.data.degraded ? t('page.readOnly') : t('page.accessActive')}
              </Badge>
            </CardHeader>
            <CardContent className="pt-4">
              <dl className="grid gap-4 text-sm sm:grid-cols-2">
                <div>
                  <dt className="text-xs text-muted-foreground">{t('trial.label')}</dt>
                  <dd className="mt-1 font-medium">
                    {billing.data.trialActive
                      ? t('trial.remaining', { count: trialDays })
                      : t('trial.inactive')}
                  </dd>
                </div>
                <div>
                  <dt className="text-xs text-muted-foreground">{t('subscription.label')}</dt>
                  <dd className="mt-1 font-medium">
                    {billing.data.subscriptionStatus
                      ? t(`subscription.status.${billing.data.subscriptionStatus}`, {
                          defaultValue: billing.data.subscriptionStatus,
                        })
                      : t('subscription.none')}
                  </dd>
                </div>
              </dl>

              {billing.data.hasSubscription && (
                <div className="mt-5 border-t border-border pt-4">
                  <Button
                    variant="outline"
                    disabled={portalPending}
                    onClick={() => void manageBilling()}
                  >
                    {portalPending ? t('buttons.redirecting') : t('buttons.manage')}
                  </Button>
                </div>
              )}
              {portalError && (
                <p role="alert" className="mt-3 text-sm text-destructive">
                  {portalError}
                </p>
              )}
            </CardContent>
          </Card>

          <PricingCards currentPlan={billing.data.plan} />
        </>
      )}
    </main>
  )
}
