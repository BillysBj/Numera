import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'

import {
  listAbschlaege,
  type SalesDocumentListItem,
} from '@/lib/api/documents'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogFooter } from '@/components/ui/dialog'

interface AbschlagSelectDialogProps {
  open: boolean
  partnerId: string
  selectedIds: string[]
  onChange: (ids: string[], documents: SalesDocumentListItem[]) => void
  onOpenChange: (open: boolean) => void
}

export function AbschlagSelectDialog({
  open,
  partnerId,
  selectedIds,
  onChange,
  onOpenChange,
}: AbschlagSelectDialogProps) {
  const { t } = useTranslation('documents')
  const query = useQuery({
    queryKey: ['abschlaege', partnerId],
    queryFn: () => listAbschlaege(partnerId),
    enabled: open && !!partnerId,
  })
  const items = query.data?.items ?? []
  const money = (item: SalesDocumentListItem) =>
    new Intl.NumberFormat('de-DE', {
      style: 'currency',
      currency: item.currency,
    }).format(item.totalGross)

  const toggle = (id: string, checked: boolean) => {
    const ids = checked
      ? [...selectedIds, id]
      : selectedIds.filter((selected) => selected !== id)
    onChange(ids, items.filter((item) => ids.includes(item.id)))
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={t('form.abschlagPicker.title')}
      description={t('form.abschlagPicker.description')}
    >
      {!partnerId ? (
        <p className="text-sm text-muted-foreground">
          {t('form.abschlagPicker.partnerRequired')}
        </p>
      ) : query.isLoading ? (
        <p className="text-sm text-muted-foreground">{t('table.loading')}</p>
      ) : items.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          {t('form.abschlagPicker.empty')}
        </p>
      ) : (
        <div className="flex max-h-80 flex-col gap-2 overflow-auto">
          {items.map((item) => (
            <label
              key={item.id}
              className="flex cursor-pointer items-center gap-3 rounded-md border border-border p-3"
            >
              <Checkbox
                checked={selectedIds.includes(item.id)}
                onCheckedChange={(checked) => toggle(item.id, checked)}
              />
              <span className="flex-1">
                {item.documentNumber ?? '—'} · {item.documentDate}
              </span>
              <span className="tabular-nums">{money(item)}</span>
            </label>
          ))}
        </div>
      )}
      <DialogFooter>
        <Button type="button" onClick={() => onOpenChange(false)}>
          {t('form.abschlagPicker.done')}
        </Button>
      </DialogFooter>
    </Dialog>
  )
}
