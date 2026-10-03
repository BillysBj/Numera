import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useForm } from 'react-hook-form'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, getMe } from '@/lib/api'
import {
  getEmailSettings, saveEmailSettings, sendTestEmail,
  type EmailSettingsDto, type UpdateEmailSettingsRequest,
} from '@/lib/api/emailSettings'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

export default function EmailSettingsPage() {
  const { t } = useTranslation('emailSettings')
  const me = useQuery({ queryKey: ['me'], queryFn: getMe })
  if (me.isPending) return <main className="app-main">{t('loading')}</main>
  if (me.isError) return <main className="app-main" role="alert">{t('loadError')}</main>
  if (me.data?.role !== 'Owner') return <main className="app-main">{t('ownerOnly')}</main>
  return <EmailSettingsForm ownerEmail={me.data.user.email ?? ''} />
}

function formValues(settings: EmailSettingsDto): UpdateEmailSettingsRequest {
  // Enumerate writable fields explicitly: the password never enters the query cache.
  return {
    host: settings.host ?? '', port: settings.port, useSsl: settings.useSsl,
    username: settings.username ?? '', password: '', fromAddress: settings.fromAddress ?? '',
    fromName: settings.fromName ?? '', invoiceSubject: settings.invoiceSubject ?? '',
    invoiceBody: settings.invoiceBody ?? '', dunningSubject: settings.dunningSubject ?? '',
    dunningBody: settings.dunningBody ?? '',
  }
}

function EmailSettingsForm({ ownerEmail }: { ownerEmail: string }) {
  const { t } = useTranslation('emailSettings')
  const queryClient = useQueryClient()
  const existing = useQuery({ queryKey: ['email-settings'], queryFn: getEmailSettings })
  const form = useForm<UpdateEmailSettingsRequest>()
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<string | null>(null)
  const [recipient, setRecipient] = useState(ownerEmail)
  useEffect(() => {
    if (existing.data) form.reset(formValues(existing.data))
  }, [existing.data, form])

  const submit = form.handleSubmit(async (values) => {
    setSaving(true)
    setSaved(false)
    setError(null)
    setTestResult(null)
    try {
      // Direct call avoids retaining the password in React Query mutation variables.
      const dto = await saveEmailSettings({ ...values, password: values.password || undefined })
      form.reset(formValues(dto))
      queryClient.setQueryData(['email-settings'], dto)
      setSaved(true)
    } catch (cause) {
      const body = cause instanceof ApiError ? cause.body as { errors?: Record<string, string[]> } : null
      setError(body?.errors ? Object.values(body.errors).flat().join(' ') : t('saveError'))
    } finally {
      setSaving(false)
    }
  })

  async function test() {
    setTesting(true)
    setError(null)
    setTestResult(null)
    try {
      const result = await sendTestEmail(recipient)
      if (result.success) setTestResult(t('testSuccess'))
      else setError(result.error || t('testError'))
    } catch {
      setError(t('testError'))
    } finally {
      setTesting(false)
    }
  }

  if (existing.isPending) return <main className="app-main">{t('loading')}</main>
  if (existing.isError) return <main className="app-main" role="alert">{t('loadError')}</main>
  const smtpEnabled = Boolean(form.watch('host')?.trim())
  const busy = saving || testing
  return (
    <main className="app-main" style={{ maxWidth: '1000px' }}>
      <h1 className="text-2xl font-semibold">{t('title')}</h1>
      <p className="mt-1 text-sm text-muted-foreground">{t('description')}</p>
      <form onSubmit={submit} className="mt-5 flex flex-col gap-4">
        <fieldset disabled={busy} className="flex flex-col gap-4">
          <Card>
            <CardHeader><CardTitle>{t('smtp')}</CardTitle></CardHeader>
            <CardContent className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <div><Label htmlFor="host">{t('host')}</Label><Input id="host" maxLength={253} {...form.register('host')} /></div>
              <div><Label htmlFor="port">{t('port')}</Label><Input id="port" type="number" min={1} max={65535} required={smtpEnabled} {...form.register('port', { valueAsNumber: true })} /></div>
              <div><Label htmlFor="username">{t('username')}</Label><Input id="username" maxLength={320} autoComplete="off" {...form.register('username')} /></div>
              <div>
                <Label htmlFor="smtp-password">{t('password')} — {existing.data.hasPassword ? t('passwordSet') : t('passwordUnset')}</Label>
                <Input id="smtp-password" type="password" maxLength={4096} autoComplete="new-password" aria-describedby="password-hint" {...form.register('password')} />
                <p id="password-hint" className="mt-1 text-xs text-muted-foreground">{t('passwordHint')}</p>
              </div>
              <div><Label htmlFor="fromAddress">{t('fromAddress')}</Label><Input id="fromAddress" type="email" maxLength={320} required={smtpEnabled} {...form.register('fromAddress')} /></div>
              <div><Label htmlFor="fromName">{t('fromName')}</Label><Input id="fromName" maxLength={200} {...form.register('fromName')} /></div>
              <label className="flex items-center gap-2 text-sm"><input type="checkbox" {...form.register('useSsl')} />{t('useSsl')}</label>
            </CardContent>
          </Card>
          <p className="text-sm text-muted-foreground">{t('templateHint')}</p>
          <p className="text-sm text-muted-foreground break-words">{t('placeholders')}</p>
          {(['invoice', 'dunning'] as const).map((kind) => (
            <Card key={kind}>
              <CardHeader><CardTitle>{t(kind)}</CardTitle></CardHeader>
              <CardContent className="flex flex-col gap-3">
                <div><Label htmlFor={`${kind}Subject`}>{t('subject')}</Label><Input id={`${kind}Subject`} maxLength={500} {...form.register(`${kind}Subject`)} /></div>
                <div><Label htmlFor={`${kind}Body`}>{t('body')}</Label><Textarea id={`${kind}Body`} rows={8} maxLength={50000} {...form.register(`${kind}Body`)} /></div>
                {kind === 'dunning' && <p className="text-xs text-muted-foreground">{t('dunningPlaceholders')}</p>}
              </CardContent>
            </Card>
          ))}
          <div><Button type="submit">{saving ? t('saving') : t('save')}</Button></div>
        </fieldset>
      </form>
      <Card className="mt-5">
        <CardHeader><CardTitle>{t('test')}</CardTitle></CardHeader>
        <CardContent>
          <form onSubmit={(event) => { event.preventDefault(); void test() }} className="flex flex-col gap-3">
            <Label htmlFor="testRecipient">{t('recipient')}</Label>
            <Input id="testRecipient" type="email" maxLength={320} value={recipient} disabled={busy} onChange={(event) => setRecipient(event.target.value)} />
            <p className="text-xs text-muted-foreground">{t('testHint')}</p>
            <div><Button type="submit" variant="outline" disabled={busy || form.formState.isDirty}>{testing ? t('testing') : t('test')}</Button></div>
          </form>
        </CardContent>
      </Card>
      {saved && !form.formState.isDirty && <p role="status" className="mt-3">{t('saved')}</p>}
      {testResult && <p role="status" className="mt-3">{testResult}</p>}
      {error && <p role="alert" className="mt-3 text-destructive">{error}</p>}
    </main>
  )
}
