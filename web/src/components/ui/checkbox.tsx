import * as React from 'react'
import { cn } from '@/lib/utils'

/**
 * A native-checkbox-backed control (no Radix dependency) that still reads/writes
 * a boolean `checked`/`onCheckedChange` pair like the shadcn primitive, so form
 * wiring stays identical.
 */
export interface CheckboxProps
  extends Omit<React.ComponentProps<'input'>, 'type' | 'onChange'> {
  onCheckedChange?: (checked: boolean) => void
}

const Checkbox = React.forwardRef<HTMLInputElement, CheckboxProps>(
  ({ className, onCheckedChange, ...props }, ref) => (
    <input
      ref={ref}
      type="checkbox"
      className={cn(
        'h-4 w-4 shrink-0 rounded border border-input accent-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}
      onChange={(e) => onCheckedChange?.(e.target.checked)}
      {...props}
    />
  ),
)
Checkbox.displayName = 'Checkbox'

export { Checkbox }
