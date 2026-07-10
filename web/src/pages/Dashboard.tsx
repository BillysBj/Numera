import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { getMe, getEntitlements } from '../lib/api'

// Track connectivity to warn that financial data is online-only. The app shell
// works offline (PWA), but /api is NetworkOnly — no stale financial reads.
function useOnline() {
  const [online, setOnline] = useState(() =>
    typeof navigator === 'undefined' ? true : navigator.onLine,
  )
  useEffect(() => {
    const on = () => setOnline(true)
    const off = () => setOnline(false)
    window.addEventListener('online', on)
    window.addEventListener('offline', off)
    return () => {
      window.removeEventListener('online', on)
      window.removeEventListener('offline', off)
    }
  }, [])
  return online
}

export default function Dashboard() {
  const { t } = useTranslation('common')
  const online = useOnline()

  const me = useQuery({ queryKey: ['me'], queryFn: getMe, enabled: online })
  const entitlements = useQuery({
    queryKey: ['entitlements'],
    queryFn: getEntitlements,
    enabled: online,
  })

  return (
    <>
      {!online && <div className="offline-banner">{t('offline.banner')}</div>}
      <main className="app-main">
        <h1>{t('dashboard.title')}</h1>

        {(me.isLoading || entitlements.isLoading) && online && (
          <p className="muted">{t('dashboard.loading')}</p>
        )}

        {(me.isError || entitlements.isError) && (
          <p className="muted">{t('dashboard.error')}</p>
        )}

        {me.data && (
          <p>
            {t('dashboard.welcome', { name: me.data.email })}
            {' — '}
            <strong>{me.data.tenantName}</strong>
          </p>
        )}

        <section>
          <h2>{t('dashboard.entitlements')}</h2>
          {entitlements.data && entitlements.data.features.length > 0 ? (
            <ul>
              {entitlements.data.features.map((f) => (
                <li key={f}>{f}</li>
              ))}
            </ul>
          ) : (
            <p className="muted">{t('dashboard.noEntitlements')}</p>
          )}
        </section>
      </main>
    </>
  )
}
