import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { ApiError, apiBlob, getMe } from '@/lib/api'
import {
  cancelAccountDeletion,
  getAccountDeletionStatus,
  requestAccountDeletion,
  type AccountDeletionStatus,
} from '@/lib/api/accountDeletion'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

export default function AccountDeletionSettingsPage() {
  const { t } = useTranslation('settings')
  const me = useQuery({ queryKey: ['me'], queryFn: getMe })
  if (me.isPending) return <main className="app-main">{t('account.loading')}</main>
  if (me.isError) return <main className="app-main" role="alert">{t('account.loadError')}</main>
  if (me.data?.role !== 'Owner') return <main className="app-main">{t('account.ownerOnly')}</main>
  return <AccountDeletion tenantName={me.data.tenant?.name ?? ''} />
}

function AccountDeletion({ tenantName }: { tenantName: string }) {
  const { t, i18n } = useTranslation('settings')
  const status = useQuery({ queryKey: ['account-deletion'], queryFn: getAccountDeletionStatus })
  const [confirmation, setConfirmation] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fmt = (iso: string | null) =>
    iso ? new Date(iso).toLocaleString(i18n.language) : null

  async function exportData() {
    setBusy(true)
    setError(null)
    try {
      const blob = await apiBlob('/export')
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = 'numera-export.zip'
      anchor.click()
      URL.revokeObjectURL(url)
      await status.refetch()
    } catch {
      setError(t('account.errors.generic'))
    } finally {
      setBusy(false)
    }
  }

  async function requestDeletion() {
    setBusy(true)
    setError(null)
    try {
      await requestAccountDeletion(confirmation)
      setConfirmation('')
      await status.refetch()
    } catch (cause) {
      setError(messageFor(cause))
    } finally {
      setBusy(false)
    }
  }

  async function cancelDeletion() {
    setBusy(true)
    setError(null)
    try {
      await cancelAccountDeletion()
      await status.refetch()
    } catch {
      setError(t('account.errors.generic'))
    } finally {
      setBusy(false)
    }
  }

  function messageFor(cause: unknown): string {
    if (cause instanceof ApiError) {
      const body = cause.body as { error?: string } | undefined
      if (cause.status === 409 && body?.error === 'export_required') return t('account.errors.exportRequired')
      if (cause.status === 400) return t('account.errors.nameMismatch')
    }
    return t('account.errors.generic')
  }

  if (status.isPending) return <main className="app-main">{t('account.loading')}</main>
  if (status.isError || !status.data) return <main className="app-main" role="alert">{t('account.loadError')}</main>
  const data: AccountDeletionStatus = status.data

  return (
    <main className="app-main" style={{ maxWidth: '760px' }}>
      <h1 className="text-2xl font-semibold">{t('account.title')}</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        {t('account.description', { graceDays: data.graceDays })}
      </p>

      {data.pendingDeletion ? (
        <Card className="mt-5 border-destructive">
          <CardHeader><CardTitle className="text-destructive">{t('account.pendingTitle')}</CardTitle></CardHeader>
          <CardContent className="flex flex-col gap-4">
            <p className="text-sm">{t('account.pendingText', { date: fmt(data.scheduledFor) ?? '' })}</p>
            <div>
              <Button variant="secondary" disabled={busy} onClick={() => void cancelDeletion()}>
                {busy ? t('account.cancelling') : t('account.cancelButton')}
              </Button>
            </div>
          </CardContent>
        </Card>
      ) : (
        <>
          <p className="mt-4 rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
            {t('account.warning')}
          </p>

          <Card className="mt-5">
            <CardHeader><CardTitle>{t('account.exportStep')}</CardTitle></CardHeader>
            <CardContent className="flex flex-col gap-3">
              <p className="text-sm text-muted-foreground">{t('account.exportText')}</p>
              <p className="text-sm">
                {data.hasFreshExport
                  ? t('account.exportFresh')
                  : data.lastExportAt
                    ? t('account.exportStale')
                    : t('account.neverExported')}
              </p>
              {data.lastExportAt && (
                <p className="text-xs text-muted-foreground">
                  {t('account.lastExport', { date: fmt(data.lastExportAt) ?? '' })}
                </p>
              )}
              <div>
                <Button variant="outline" disabled={busy} onClick={() => void exportData()}>
                  {t('account.exportButton')}
                </Button>
              </div>
            </CardContent>
          </Card>

          <Card className="mt-5">
            <CardHeader><CardTitle>{t('account.confirmStep')}</CardTitle></CardHeader>
            <CardContent className="flex flex-col gap-3">
              <Label htmlFor="confirm-name">{t('account.confirmLabel', { name: tenantName })}</Label>
              <Input
                id="confirm-name"
                value={confirmation}
                disabled={busy || !data.hasFreshExport}
                placeholder={t('account.confirmPlaceholder')}
                onChange={(event) => setConfirmation(event.target.value)}
              />
              <div>
                <Button
                  variant="destructive"
                  disabled={busy || !data.hasFreshExport || confirmation.trim().length === 0}
                  onClick={() => void requestDeletion()}
                >
                  {busy ? t('account.requesting') : t('account.requestButton')}
                </Button>
              </div>
            </CardContent>
          </Card>
        </>
      )}

      {error && <p role="alert" className="mt-4 text-sm text-destructive">{error}</p>}
    </main>
  )
}
