import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { getMe, getEntitlements } from '../lib/api'
import { useOnline } from '../lib/useOnline'

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
            {t('dashboard.welcome', { name: me.data.user?.email ?? '' })}
            {' — '}
            <strong>{me.data.tenant?.name}</strong>
          </p>
        )}

        <section>
          <h2>{t('dashboard.entitlements')}</h2>
          {entitlements.data && entitlements.data.capabilities.length > 0 ? (
            <ul>
              {entitlements.data.capabilities.map((c) => (
                <li key={c}>{c}</li>
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
