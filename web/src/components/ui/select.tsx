import * as React from 'react'
import { cn } from '@/lib/utils'

/**
 * A native-`<select>` wrapper styled to match the shadcn look. Kept dependency-free
 * (no Radix) — a plain accessible dropdown is sufficient for the partner filters and
 * form enum fields, and avoids a portal/popover dependency.
 */
const Select = React.forwardRef<
  HTMLSelectElement,
  React.ComponentProps<'select'>
>(({ className, children, ...props }, ref) => (
  <select
    ref={ref}
    className={cn(
      'flex h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50',
      className,
    )}
    {...props}
  >
    {children}
  </select>
))
Select.displayName = 'Select'

export { Select }
