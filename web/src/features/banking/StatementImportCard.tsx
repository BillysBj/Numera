import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/lib/api'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { useImportBankStatement } from './bankingApi'

const MAX_FILE_SIZE = 10 * 1024 * 1024

function errorDetail(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null
  const body = error.body as
    | { detail?: string; title?: string; errors?: Record<string, string[]> }
    | undefined
  return body?.detail ?? Object.values(body?.errors ?? {})[0]?.[0] ?? body?.title ?? null
}

export default function StatementImportCard() {
  const { t } = useTranslation('banking')
  const statementImport = useImportBankStatement()
  const [file, setFile] = useState<File | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function upload() {
    setError(null)
    if (!file) {
      setError(t('import.errors.empty'))
      return
    }
    if (file.size <= 0 || file.size > MAX_FILE_SIZE) {
      setError(t('import.errors.size'))
      return
    }
    try {
      await statementImport.mutateAsync({ file })
    } catch (uploadError) {
      setError(errorDetail(uploadError) ?? t('import.errors.upload'))
    }
  }

  return (
    <Card id="statement-import" className="scroll-mt-20 overflow-hidden">
      <CardHeader>
        <CardTitle>{t('import.title')}</CardTitle>
        <p className="text-sm text-muted-foreground">{t('import.hint')}</p>
      </CardHeader>
      <CardContent>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <label className="flex min-w-0 flex-1 flex-col gap-1.5 text-sm font-medium">
            {t('import.fileLabel')}
            <Input
              type="file"
              accept=".csv,.sta,.mt940,.txt,.xml,text/csv,application/mt940,application/x-mt940,application/xml,text/xml"
              onChange={(event) => {
                setFile(event.target.files?.[0] ?? null)
                setError(null)
                statementImport.reset()
              }}
            />
          </label>
          <Button disabled={!file || statementImport.isPending} onClick={() => void upload()}>
            {statementImport.isPending ? t('import.uploading') : t('import.action')}
          </Button>
        </div>
        <p className="mt-2 text-xs text-muted-foreground">{t('import.formats')}</p>
        {error && <p role="alert" className="mt-3 text-sm text-destructive">{error}</p>}
        {statementImport.data && (
          <p role="status" className="mt-3 text-sm font-medium text-primary">
            {t('import.success', { count: statementImport.data.insertedCount })}
          </p>
        )}
      </CardContent>
    </Card>
  )
}
