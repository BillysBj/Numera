import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

import { buttonVariants } from '@/components/ui/button'
import { cn } from '@/lib/utils'

import { useBillingStatus } from './billingApi'

export default function DegradationBanner() {
  const { t } = useTranslation('billing')
  const billing = useBillingStatus()

  if (billing.data?.degraded !== true) return null

  return (
    <aside
      role="status"
      aria-live="polite"
      className="border-b border-amber-300 bg-amber-50 px-4 py-3 text-amber-950 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100"
    >
      <div className="mx-auto flex max-w-[1200px] flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <p className="text-sm font-semibold">{t('banner.title')}</p>
          <p className="mt-0.5 text-sm">{t('banner.text')}</p>
        </div>
        <Link
          to="/billing"
          className={cn(
            buttonVariants({ variant: 'outline', size: 'sm' }),
            'shrink-0 border-amber-500 bg-amber-50 hover:bg-amber-100 dark:border-amber-700 dark:bg-amber-950 dark:hover:bg-amber-900',
          )}
        >
          {t('banner.action')}
        </Link>
      </div>
    </aside>
  )
}
