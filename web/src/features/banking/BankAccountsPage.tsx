import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/lib/api'
import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import BankConnectCard from './BankConnectCard'
import StatementImportCard from './StatementImportCard'
import {
  ConsentStatus,
  useBankAccounts,
  useBankConsent,
  useReauthBankConnection,
  useSyncBankAccount,
  type BankAccountListItem,
} from './bankingApi'

function errorDetail(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null
  const body = error.body as
    | { detail?: string; title?: string; errors?: Record<string, string[]> }
    | undefined
  return body?.detail ?? Object.values(body?.errors ?? {})[0]?.[0] ?? body?.title ?? null
}

function formatDateTime(value: string | null, locale: string): string {
  if (!value) return '—'
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? value
    : new Intl.DateTimeFormat(locale, {
        dateStyle: 'medium',
        timeStyle: 'short',
      }).format(parsed)
}

function formatDate(value: string | null, locale: string): string {
  if (!value) return '—'
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? value
    : new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(parsed)
}

function AccountCard({ account }: { account: BankAccountListItem }) {
  const { t, i18n } = useTranslation('banking')
  const locale = i18n.resolvedLanguage === 'en' ? 'en-GB' : 'de-DE'
  const consent = useBankConsent(account.connectionId ?? '', false)
  const sync = useSyncBankAccount()
  const reauth = useReauthBankConnection()
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const consentStatus = consent.data?.status ?? account.consentStatus
  const consentExpiresAt = consent.data?.expiresAt ?? account.consentExpiresAt

  async function refreshConsent() {
    setMessage(null)
    setError(null)
    const result = await consent.refetch()
    if (result.isError) setError(errorDetail(result.error) ?? t('accounts.consentError'))
    else setMessage(t('accounts.consentRefreshed'))
  }

  async function startReauth() {
    if (!account.connectionId) return
    setMessage(null)
    setError(null)
    try {
      const webForm = await reauth.mutateAsync(account.connectionId)
      window.location.assign(webForm.redirectUrl)
    } catch (reauthError) {
      const detail = errorDetail(reauthError)
      const stubUnavailable =
        reauthError instanceof ApiError &&
        reauthError.status === 422 &&
        detail === 'Live-Bankanbindung ist nicht konfiguriert.'
      setError(stubUnavailable ? t('connect.stubFallback') : detail ?? t('accounts.reauthError'))
    }
  }

  async function syncNow() {
    setMessage(null)
    setError(null)
    try {
      await sync.mutateAsync(account.id)
      setMessage(t('accounts.syncQueued'))
    } catch (syncError) {
      setError(errorDetail(syncError) ?? t('accounts.syncError'))
    }
  }

  return (
    <Card className="overflow-hidden">
      <CardHeader className="border-b border-border bg-muted/35">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle>{account.displayName}</CardTitle>
            <p className="mt-1 font-mono text-xs text-muted-foreground">{account.iban}</p>
          </div>
          {account.connectionId ? (
            <Badge
              variant={consentStatus === ConsentStatus.Active ? 'secondary' : 'outline'}
              className={cn(
                consentStatus !== ConsentStatus.Active &&
                  'border-amber-400 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100',
              )}
            >
              {consentExpiresAt
                ? t('accounts.consentExpires', { date: formatDate(consentExpiresAt, locale) })
                : t(`consent.${consentStatus ?? ConsentStatus.Pending}`)}
            </Badge>
          ) : (
            <Badge variant="outline">{t('accounts.importAccount')}</Badge>
          )}
        </div>
      </CardHeader>
      <CardContent className="pt-4">
        <dl className="grid gap-3 text-sm sm:grid-cols-3">
          <div>
            <dt className="text-xs text-muted-foreground">{t('accounts.currency')}</dt>
            <dd className="mt-0.5 font-medium">{account.currency}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground">{t('accounts.lastSynced')}</dt>
            <dd className="mt-0.5 font-medium">{formatDateTime(account.lastSyncedAt, locale)}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground">{t('accounts.consentStatus')}</dt>
            <dd className="mt-0.5 font-medium">
              {account.connectionId
                ? t(`consent.${consentStatus ?? ConsentStatus.Pending}`)
                : t('accounts.notApplicable')}
            </dd>
          </div>
        </dl>

        {account.connectionId && (
          <div className="mt-4 flex flex-wrap gap-2 border-t border-border pt-4">
            <Button disabled={sync.isPending} onClick={() => void syncNow()}>
              {sync.isPending ? t('accounts.syncing') : t('accounts.syncNow')}
            </Button>
            <Button
              variant="outline"
              disabled={reauth.isPending}
              onClick={() => void startReauth()}
            >
              {reauth.isPending ? t('accounts.reauthStarting') : t('accounts.reauth')}
            </Button>
            <Button
              variant="ghost"
              disabled={consent.isFetching}
              onClick={() => void refreshConsent()}
            >
              {consent.isFetching ? t('accounts.checkingConsent') : t('accounts.checkConsent')}
            </Button>
          </div>
        )}

        {error && <p role="alert" className="mt-3 text-sm text-destructive">{error}</p>}
        {message && <p role="status" className="mt-3 text-sm font-medium text-primary">{message}</p>}
      </CardContent>
    </Card>
  )
}

export default function BankAccountsPage() {
  const { t } = useTranslation('banking')
  const accounts = useBankAccounts()

  return (
    <main className="app-main" style={{ maxWidth: '1200px' }}>
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">{t('accounts.title')}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t('accounts.subtitle')}</p>
        </div>
        <Link to="/banking/queue" className={cn(buttonVariants())}>
          {t('actions.openQueue')}
        </Link>
      </div>

      <div className="mb-6 grid gap-5 lg:grid-cols-2">
        <BankConnectCard />
        <StatementImportCard />
      </div>

      <section aria-labelledby="bank-accounts-heading">
        <h2 id="bank-accounts-heading" className="mb-3 text-lg font-semibold">
          {t('accounts.connectedTitle')}
        </h2>
        {accounts.isError && (
          <p role="alert" className="mb-4 text-sm text-destructive">{t('accounts.loadError')}</p>
        )}
        {accounts.isLoading && (
          <p className="text-sm text-muted-foreground">{t('accounts.loading')}</p>
        )}
        <div className="grid gap-4">
          {accounts.data?.map((account) => <AccountCard key={account.id} account={account} />)}
        </div>
        {accounts.data?.length === 0 && (
          <div className="rounded-xl border border-dashed border-border bg-card px-5 py-10 text-center">
            <p className="text-sm font-medium">{t('accounts.empty')}</p>
            <p className="mt-1 text-sm text-muted-foreground">{t('accounts.emptyHint')}</p>
          </div>
        )}
      </section>
    </main>
  )
}
