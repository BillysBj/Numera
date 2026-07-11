import * as React from 'react'
import { cn } from '@/lib/utils'
import { Button } from './button'

/**
 * A minimal, dependency-free modal dialog (no Radix). Rendered inline with a
 * backdrop; `open`/`onOpenChange` mirror the shadcn API so call sites read the
 * same. Sufficient for the contact/note add/edit forms and archive confirms.
 */
export interface DialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title?: React.ReactNode
  description?: React.ReactNode
  children?: React.ReactNode
}

function Dialog({ open, onOpenChange, title, description, children }: DialogProps) {
  React.useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onOpenChange(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onOpenChange])

  if (!open) return null

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      role="dialog"
      aria-modal="true"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onOpenChange(false)
      }}
    >
      <div
        className={cn(
          'w-full max-w-lg rounded-lg border border-border bg-card p-6 text-card-foreground shadow-lg',
        )}
      >
        {(title || description) && (
          <div className="mb-4 flex flex-col gap-1.5">
            {title && <h2 className="text-lg font-semibold">{title}</h2>}
            {description && (
              <p className="text-sm text-muted-foreground">{description}</p>
            )}
          </div>
        )}
        {children}
      </div>
    </div>
  )
}

function DialogFooter({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn('mt-6 flex justify-end gap-2', className)}
      {...props}
    />
  )
}

export { Dialog, DialogFooter, Button }
