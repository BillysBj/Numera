import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { useReceiptMailbox } from './belegeApi'

export default function MailboxAddressCard() {
  const { t } = useTranslation('belege')
  const mailbox = useReceiptMailbox()
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle')

  async function copy() {
    if (!mailbox.data?.address) return
    try {
      await navigator.clipboard.writeText(mailbox.data.address)
      setCopyState('copied')
    } catch {
      setCopyState('failed')
    }
  }

  return (
    <Card className="overflow-hidden border-primary/20">
      <div className="h-1 bg-primary" />
      <CardHeader className="flex-row items-center justify-between gap-3">
        <CardTitle>{t('mailbox.title')}</CardTitle>
        {mailbox.data && (
          <Badge variant={mailbox.data.isActive ? 'secondary' : 'destructive'}>
            {mailbox.data.isActive ? t('mailbox.active') : t('mailbox.inactive')}
          </Badge>
        )}
      </CardHeader>
      <CardContent>
        {mailbox.isLoading && (
          <p className="text-sm text-muted-foreground">{t('mailbox.loading')}</p>
        )}
        {mailbox.isError && (
          <p role="alert" className="text-sm text-destructive">{t('mailbox.error')}</p>
        )}
        {mailbox.data && (
          <>
            <div className="flex flex-col gap-2 sm:flex-row">
              <code className="min-w-0 flex-1 overflow-x-auto rounded-md bg-muted px-3 py-2 text-sm font-semibold text-foreground">
                {mailbox.data.address}
              </code>
              <Button variant="outline" onClick={() => void copy()}>
                {copyState === 'copied' ? t('mailbox.copied') : t('mailbox.copy')}
              </Button>
            </div>
            <p className="mt-3 text-sm text-muted-foreground">{t('mailbox.hint')}</p>
            {copyState === 'failed' && (
              <p role="alert" className="mt-2 text-xs text-destructive">
                {t('mailbox.copyError')}
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}
