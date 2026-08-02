import { useTranslation } from 'react-i18next'

import { cn } from '@/lib/utils'

export interface UpgradeHintProps {
  className?: string
  /** The tier that unlocks the feature. Drives the copy; defaults to 'L' for back-compat. */
  requiredTier?: 'M' | 'L' | 'XL'
}

/** Informational cosmetic gate; it never replaces server-side authorization. */
export function UpgradeHint({ className, requiredTier = 'L' }: UpgradeHintProps) {
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
        tier: requiredTier,
        defaultValue: 'Diese Funktion ist ab Tarif {{tier}} verfügbar',
      })}
    </div>
  )
}
