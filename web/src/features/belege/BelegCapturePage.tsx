import { useRef, useState, type DragEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/lib/api'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { ReceiptStatus, useUploadReceipt } from './belegeApi'

const MAX_UPLOAD_BYTES = 15 * 1024 * 1024
const ACCEPTED_TYPES = new Set([
  'application/pdf',
  'image/jpeg',
  'image/png',
  'image/tiff',
  'image/heif',
  'image/heic',
])
const ACCEPT = 'application/pdf,image/jpeg,image/png,image/tiff,image/heif,image/heic,.heif,.heic'

function isAccepted(file: File): boolean {
  if (ACCEPTED_TYPES.has(file.type)) return true
  return /\.(pdf|jpe?g|png|tiff?|heif|heic)$/i.test(file.name)
}

function apiMessage(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null
  const body = error.body as
    | { detail?: string; errors?: Record<string, string[]> }
    | undefined
  return body?.detail ?? body?.errors?.file?.[0] ?? null
}

export default function BelegCapturePage() {
  const { t } = useTranslation('belege')
  const navigate = useNavigate()
  const upload = useUploadReceipt()
  const fileInput = useRef<HTMLInputElement>(null)
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [dragging, setDragging] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function choose(file: File | null) {
    setError(null)
    if (!file) {
      setSelectedFile(null)
      return
    }
    if (file.size <= 0 || file.size > MAX_UPLOAD_BYTES) {
      setSelectedFile(null)
      setError(t('capture.errors.size'))
      return
    }
    if (!isAccepted(file)) {
      setSelectedFile(null)
      setError(t('capture.errors.type'))
      return
    }
    setSelectedFile(file)
  }

  function drop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault()
    setDragging(false)
    choose(event.dataTransfer.files[0] ?? null)
  }

  async function submit() {
    if (!selectedFile) {
      setError(t('capture.errors.empty'))
      return
    }
    setError(null)
    try {
      const result = await upload.mutateAsync(selectedFile)
      navigate('/belege', {
        state: {
          captureNotice:
            result.status === ReceiptStatus.Duplicate ? 'duplicate' : 'uploaded',
        },
      })
    } catch (uploadError) {
      if (uploadError instanceof ApiError && uploadError.status === 422) {
        navigate('/belege', { state: { captureNotice: 'quarantined' } })
        return
      }
      setError(apiMessage(uploadError) ?? t('capture.errors.upload'))
    }
  }

  return (
    <main className="app-main" style={{ maxWidth: '920px' }}>
      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="mb-1 text-xs font-semibold uppercase tracking-[0.12em] text-primary">
            {t('capture.eyebrow')}
          </p>
          <h1 className="text-2xl font-semibold">{t('capture.title')}</h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            {t('capture.subtitle')}
          </p>
        </div>
        <Link to="/belege" className={cn(buttonVariants({ variant: 'outline' }))}>
          {t('actions.backToQueue')}
        </Link>
      </div>

      <div className="grid gap-5 md:grid-cols-[0.8fr_1.2fr]">
        <Card>
          <CardHeader>
            <CardTitle>{t('capture.camera.title')}</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="grid aspect-[4/3] place-items-center rounded-lg border border-dashed border-primary/40 bg-primary/5 p-6 text-center">
              <div>
                <span className="mx-auto grid h-12 w-12 place-items-center rounded-full bg-primary text-xl text-primary-foreground" aria-hidden="true">
                  ◎
                </span>
                <p className="mt-3 text-sm font-medium">{t('capture.camera.hint')}</p>
              </div>
            </div>
            <label className={cn(buttonVariants(), 'cursor-pointer')}>
              {t('capture.camera.action')}
              <input
                className="sr-only"
                type="file"
                accept="image/*"
                capture="environment"
                onChange={(event) => choose(event.target.files?.[0] ?? null)}
              />
            </label>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t('capture.upload.title')}</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div
              role="button"
              tabIndex={0}
              onClick={() => fileInput.current?.click()}
              onKeyDown={(event) => {
                if (event.key === 'Enter' || event.key === ' ') fileInput.current?.click()
              }}
              onDragEnter={(event) => {
                event.preventDefault()
                setDragging(true)
              }}
              onDragOver={(event) => event.preventDefault()}
              onDragLeave={() => setDragging(false)}
              onDrop={drop}
              className={cn(
                'grid min-h-48 cursor-pointer place-items-center rounded-lg border-2 border-dashed p-6 text-center transition-colors focus-visible:ring-2 focus-visible:ring-ring',
                dragging ? 'border-primary bg-primary/8' : 'border-border bg-muted/25 hover:border-primary/50',
              )}
            >
              <div>
                <p className="font-medium">{t('capture.upload.drop')}</p>
                <p className="mt-1 text-sm text-muted-foreground">
                  {t('capture.upload.formats')}
                </p>
                {selectedFile && (
                  <p className="mt-4 rounded-md bg-card px-3 py-2 text-sm font-semibold text-primary shadow-sm">
                    {selectedFile.name}
                  </p>
                )}
              </div>
              <input
                ref={fileInput}
                className="sr-only"
                type="file"
                accept={ACCEPT}
                onChange={(event) => choose(event.target.files?.[0] ?? null)}
              />
            </div>
            {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
            <Button disabled={!selectedFile || upload.isPending} onClick={() => void submit()}>
              {upload.isPending ? t('capture.upload.uploading') : t('capture.upload.action')}
            </Button>
            <p className="text-xs text-muted-foreground">{t('capture.wormHint')}</p>
          </CardContent>
        </Card>
      </div>
    </main>
  )
}
