import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useForm } from 'react-hook-form'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, getMe } from '@/lib/api'
import {
  getBelegeMailboxSettings,
  saveBelegeMailboxSettings,
  type BelegeMailboxSettingsDto,
  type UpdateBelegeMailboxRequest,
} from '@/lib/api/belegeMailbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

export default function BelegeMailboxSettingsPage() {
  const { t } = useTranslation('settings')
  const me = useQuery({ queryKey: ['me'], queryFn: getMe })
  if (me.isPending) return <main className="app-main">{t('belegeMailbox.loading')}</main>
  if (me.isError) return <main className="app-main" role="alert">{t('belegeMailbox.loadError')}</main>
  if (me.data?.role !== 'Owner') return <main className="app-main">{t('belegeMailbox.ownerOnly')}</main>
  return <BelegeMailboxForm />
}

function formValues(dto: BelegeMailboxSettingsDto): UpdateBelegeMailboxRequest {
  return {
    host: dto.host ?? '',
    port: dto.port || 993,
    useSsl: dto.useSsl,
    username: dto.username ?? '',
    password: '',
    domain: dto.domain ?? '',
    isActive: dto.isActive,
  }
}

function BelegeMailboxForm() {
  const { t } = useTranslation('settings')
  const queryClient = useQueryClient()
  const existing = useQuery({ queryKey: ['belege-mailbox-settings'], queryFn: getBelegeMailboxSettings })
  const form = useForm<UpdateBelegeMailboxRequest>({
    defaultValues: { host: '', port: 993, useSsl: true, username: '', password: '', domain: '', isActive: false },
  })
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (existing.data) form.reset(formValues(existing.data))
  }, [existing.data, form])

  const submit = form.handleSubmit(async (values) => {
    setSaving(true)
    setSaved(false)
    setError(null)
    try {
      const dto = await saveBelegeMailboxSettings({ ...values, password: values.password || undefined })
      form.reset(formValues(dto))
      queryClient.setQueryData(['belege-mailbox-settings'], dto)
      setSaved(true)
    } catch (cause) {
      const body = cause instanceof ApiError ? (cause.body as { errors?: Record<string, string[]> }) : null
      setError(body?.errors ? Object.values(body.errors).flat().join(' ') : t('belegeMailbox.saveError'))
    } finally {
      setSaving(false)
    }
  })

  if (existing.isPending) return <main className="app-main">{t('belegeMailbox.loading')}</main>
  if (existing.isError) return <main className="app-main" role="alert">{t('belegeMailbox.loadError')}</main>

  return (
    <main className="app-main" style={{ maxWidth: '800px' }}>
      <h1 className="text-2xl font-semibold">{t('belegeMailbox.title')}</h1>
      <p className="mt-1 text-sm text-muted-foreground">{t('belegeMailbox.description')}</p>
      <form onSubmit={submit} className="mt-5 flex flex-col gap-4">
        <fieldset disabled={saving} className="flex flex-col gap-4">
          <Card>
            <CardHeader><CardTitle>{t('belegeMailbox.imap')}</CardTitle></CardHeader>
            <CardContent className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <div>
                <Label htmlFor="bm-host">{t('belegeMailbox.host')}</Label>
                <Input id="bm-host" maxLength={253} {...form.register('host')} />
              </div>
              <div>
                <Label htmlFor="bm-port">{t('belegeMailbox.port')}</Label>
                <Input id="bm-port" type="number" min={1} max={65535} {...form.register('port', { valueAsNumber: true })} />
              </div>
              <div>
                <Label htmlFor="bm-username">{t('belegeMailbox.username')}</Label>
                <Input id="bm-username" maxLength={320} autoComplete="off" {...form.register('username')} />
              </div>
              <div>
                <Label htmlFor="bm-password">
                  {t('belegeMailbox.password')} — {existing.data.hasPassword ? t('belegeMailbox.passwordSet') : t('belegeMailbox.passwordUnset')}
                </Label>
                <Input id="bm-password" type="password" maxLength={4096} autoComplete="new-password" aria-describedby="bm-password-hint" {...form.register('password')} />
                <p id="bm-password-hint" className="mt-1 text-xs text-muted-foreground">{t('belegeMailbox.passwordHint')}</p>
              </div>
              <div>
                <Label htmlFor="bm-domain">{t('belegeMailbox.domain')}</Label>
                <Input id="bm-domain" maxLength={253} aria-describedby="bm-domain-hint" {...form.register('domain')} />
                <p id="bm-domain-hint" className="mt-1 text-xs text-muted-foreground">{t('belegeMailbox.domainHint')}</p>
              </div>
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" {...form.register('useSsl')} />
                {t('belegeMailbox.useSsl')}
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" {...form.register('isActive')} />
                {t('belegeMailbox.isActive')}
              </label>
            </CardContent>
          </Card>
          <p className="text-sm text-muted-foreground">{t('belegeMailbox.hint')}</p>
          <div><Button type="submit">{saving ? t('belegeMailbox.saving') : t('belegeMailbox.save')}</Button></div>
        </fieldset>
      </form>
      {saved && !form.formState.isDirty && <p role="status" className="mt-3 text-primary">{t('belegeMailbox.saved')}</p>}
      {error && <p role="alert" className="mt-3 text-destructive">{error}</p>}
    </main>
  )
}
