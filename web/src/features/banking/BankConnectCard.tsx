import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/lib/api'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { useConnectBank } from './bankingApi'

function problemDetail(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null
  const body = error.body as
    | { detail?: string; title?: string; errors?: Record<string, string[]> }
    | undefined
  return body?.detail ?? Object.values(body?.errors ?? {})[0]?.[0] ?? body?.title ?? null
}

export default function BankConnectCard() {
  const { t } = useTranslation('banking')
  const connect = useConnectBank()
  const [error, setError] = useState<string | null>(null)
  const [useImport, setUseImport] = useState(false)

  async function startConnect() {
    setError(null)
    setUseImport(false)
    try {
      const webForm = await connect.mutateAsync()
      window.location.assign(webForm.redirectUrl)
    } catch (connectError) {
      const detail = problemDetail(connectError)
      const stubUnavailable =
        connectError instanceof ApiError &&
        connectError.status === 422 &&
        detail === 'Live-Bankanbindung ist nicht konfiguriert.'
      setUseImport(stubUnavailable)
      setError(stubUnavailable ? t('connect.stubFallback') : detail ?? t('connect.error'))
    }
  }

  return (
    <Card className="overflow-hidden border-primary/20">
      <div className="h-1 bg-primary" />
      <CardHeader>
        <CardTitle>{t('connect.title')}</CardTitle>
        <p className="text-sm text-muted-foreground">{t('connect.hint')}</p>
      </CardHeader>
      <CardContent>
        <Button disabled={connect.isPending} onClick={() => void startConnect()}>
          {connect.isPending ? t('connect.starting') : t('connect.action')}
        </Button>
        {error && (
          <p role="alert" className="mt-3 text-sm text-destructive">
            {error}{' '}
            {useImport && (
              <a className="font-semibold underline underline-offset-2" href="#statement-import">
                {t('connect.useImport')}
              </a>
            )}
          </p>
        )}
      </CardContent>
    </Card>
  )
}
