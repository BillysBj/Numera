import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { registerCompany, loginUrl, ApiError } from '../lib/api'
import { useTheme } from '../lib/useTheme'
import LanguageSwitcher from '../components/LanguageSwitcher'
import { IconSun, IconMoon } from '../components/icons'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

type RegisterState =
  | { status: 'idle' }
  | { status: 'submitting' }
  | { status: 'success' }
  | { status: 'error' }

export default function Login() {
  const { t } = useTranslation('auth')
  const { t: tc } = useTranslation('common')
  const { theme, toggle } = useTheme()
  const [state, setState] = useState<RegisterState>({ status: 'idle' })

  // Sign in = full-page navigation to the BFF OIDC challenge. The SPA never
  // handles credentials or tokens directly; the BFF sets the session cookie.
  const signIn = () => {
    window.location.assign(loginUrl())
  }

  const onRegister = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    setState({ status: 'submitting' })
    try {
      await registerCompany({
        companyName: String(form.get('company') ?? ''),
        email: String(form.get('email') ?? ''),
        password: String(form.get('password') ?? ''),
      })
      setState({ status: 'success' })
    } catch (err) {
      // eslint-disable-next-line no-console
      console.error('register failed', err instanceof ApiError ? err.status : err)
      setState({ status: 'error' })
    }
  }

  return (
    <main className="relative flex min-h-dvh flex-col items-center justify-center overflow-hidden px-4 py-10">
      {/* Ambient brand glow — restrained, sets the tone without decoration. */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-x-0 -top-40 h-80 bg-[radial-gradient(60%_100%_at_50%_0%,color-mix(in_oklab,var(--primary)_22%,transparent),transparent)]"
      />

      <div className="absolute right-4 top-4 flex items-center gap-2">
        <LanguageSwitcher />
        <button
          type="button"
          onClick={toggle}
          aria-label={theme === 'dark' ? tc('ui.themeLight') : tc('ui.themeDark')}
          className="grid h-9 w-9 place-items-center rounded-lg border border-border bg-card text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground"
        >
          {theme === 'dark' ? <IconSun size={18} /> : <IconMoon size={18} />}
        </button>
      </div>

      <div className="relative w-full max-w-[26rem]">
        {/* Brand */}
        <div className="mb-8 flex flex-col items-center text-center">
          <span className="grid h-12 w-12 place-items-center rounded-[0.85rem] bg-primary text-xl font-bold text-primary-foreground shadow-sm">
            N
          </span>
          <h1 className="mt-4 text-2xl font-bold tracking-tight text-foreground">
            {t('login.title')}
          </h1>
          <p className="mt-1.5 text-sm text-muted-foreground">{t('login.subtitle')}</p>
        </div>

        {/* Card */}
        <div className="rounded-xl border border-border bg-card p-6 shadow-sm">
          <Button type="button" className="w-full" size="lg" onClick={signIn}>
            {t('login.signInButton')}
          </Button>

          <div className="my-5 flex items-center gap-3 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            <span className="h-px flex-1 bg-border" />
            {t('register.title')}
            <span className="h-px flex-1 bg-border" />
          </div>

          <form className="flex flex-col gap-3.5" onSubmit={onRegister}>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="company">{t('register.company')}</Label>
              <Input id="company" name="company" type="text" autoComplete="organization" required />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="email">{t('register.email')}</Label>
              <Input id="email" name="email" type="email" autoComplete="email" required />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="password">{t('register.password')}</Label>
              <Input id="password" name="password" type="password" autoComplete="new-password" required />
            </div>
            <Button
              type="submit"
              variant="outline"
              className="mt-1 w-full"
              disabled={state.status === 'submitting'}
            >
              {state.status === 'submitting' ? t('register.submitting') : t('register.submit')}
            </Button>
            {state.status === 'success' && (
              <p className="text-sm text-primary">{t('register.success')}</p>
            )}
            {state.status === 'error' && (
              <p className="text-sm text-destructive">{t('register.error')}</p>
            )}
          </form>
        </div>

        <p className="mt-6 text-center text-xs text-muted-foreground">
          {tc('app.name')} · {tc('app.tagline')}
        </p>
      </div>
    </main>
  )
}
