import { useTranslation } from 'react-i18next'

import { cn } from '@/lib/utils'

export interface UpgradeHintProps {
  className?: string
}

/** Informational cosmetic gate; it never replaces server-side authorization. */
export function UpgradeHint({ className }: UpgradeHintProps) {
  const { t } = useTranslation('common')

  return (
    <div
      role="status"
      className={cn(
        'rounded-md border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-950',
        className,
      )}
    >
      {t('entitlements.upgradeHint', {
        defaultValue: 'Diese Funktion ist ab Tarif L verfügbar',
        defaultValue_en: 'This feature is available from plan L',
      })}
    </div>
  )
}
