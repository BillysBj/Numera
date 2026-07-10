import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { registerCompany, loginUrl, ApiError } from '../lib/api'

type RegisterState =
  | { status: 'idle' }
  | { status: 'submitting' }
  | { status: 'success' }
  | { status: 'error' }

export default function Login() {
  const { t } = useTranslation('auth')
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
    <main className="app-main">
      <section>
        <h1>{t('login.title')}</h1>
        <p className="muted">{t('login.subtitle')}</p>
        <button type="button" className="primary" onClick={signIn}>
          {t('login.signInButton')}
        </button>
      </section>

      <section>
        <h2>{t('register.title')}</h2>
        <form className="stack" onSubmit={onRegister}>
          <label>
            {t('register.company')}
            <input name="company" type="text" autoComplete="organization" required />
          </label>
          <label>
            {t('register.email')}
            <input name="email" type="email" autoComplete="email" required />
          </label>
          <label>
            {t('register.password')}
            <input name="password" type="password" autoComplete="new-password" required />
          </label>
          <button type="submit" className="primary" disabled={state.status === 'submitting'}>
            {state.status === 'submitting' ? t('register.submitting') : t('register.submit')}
          </button>
          {state.status === 'success' && <p className="muted">{t('register.success')}</p>}
          {state.status === 'error' && <p className="muted">{t('register.error')}</p>}
        </form>
      </section>
    </main>
  )
}
