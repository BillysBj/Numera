import {
  forwardRef,
  useEffect,
  useMemo,
  useState,
  type ComponentProps,
} from 'react'
import { useNavigate, useParams, Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
  useFieldArray,
  useForm,
  type Path,
  type Resolver,
} from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import {
  keepPreviousData,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import {
  createSalesDocument,
  createAbschlag,
  createFinalInvoice,
  getSalesDocument,
  lookupCatalogItems,
  updateSalesDocument,
  ApiError,
  DocumentStatus,
  DocumentType,
  TaxCategory,
  type CatalogLineItem,
  type SalesDocumentDetail,
  type SalesDocumentListItem,
} from '@/lib/api/documents'
import { UNIT_CODES, unitLabel } from '../catalog/units'
import {
  computePreviewTotals,
  emptyDocumentForm,
  emptyLine,
  makeDocumentSchema,
  toCreateRequest,
  type DocumentFormValues,
} from './documentSchema'
import { Form } from '@/components/ui/form'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { hasCapability, useEntitlements } from '@/lib/entitlements'
import { UpgradeHint } from '@/features/shared/UpgradeHint'
import { AbschlagSelectDialog } from './AbschlagSelectDialog'

const TAX_KEYS = ['S', 'AE', 'K', 'E', 'Z', 'G', 'O'] as const
const TYPE_VALUES = Object.values(DocumentType) as number[]
const CURRENCIES = ['EUR', 'USD', 'GBP', 'CHF', 'CAD', 'AUD', 'NOK', 'SEK', 'DKK', 'PLN', 'CZK']

const eurFmt = new Intl.NumberFormat('de-DE', {
  style: 'currency',
  currency: 'EUR',
})

// Maps a server ValidationProblem field key (PascalCase, e.g. "Lines[0].Name") onto the
// react-hook-form path (lines.0.name) so 400 field errors land on the right control.
function mapServerField(key: string): string {
  return key
    .replace(/\[(\d+)\]/g, '.$1')
    .split('.')
    .map((seg) => (seg === '' ? seg : seg.charAt(0).toLowerCase() + seg.slice(1)))
    .join('.')
}

// Extract {field: message} from a 400 ValidationProblem. The 409 non-draft Conflict is a
// plain ProblemDetails (no `errors`), so it returns null and the caller shows a banner.
function extractFieldErrors(err: unknown): Record<string, string> | null {
  if (!(err instanceof ApiError) || err.status !== 400) return null
  const body = err.body as { errors?: Record<string, string[]> } | undefined
  if (!body?.errors) return null
  const out: Record<string, string> = {}
  for (const [key, messages] of Object.entries(body.errors)) {
    if (messages?.length) out[key] = messages[0]
  }
  return out
}

function toFormValues(d: SalesDocumentDetail): DocumentFormValues {
  return {
    documentType: d.documentType,
    partnerId: d.partnerId ?? '',
    documentDate: d.documentDate.slice(0, 10),
    currency: CURRENCIES.includes(d.currency)
      ? (d.currency as DocumentFormValues['currency'])
      : 'EUR',
    exchangeRate: d.exchangeRate ?? undefined,
    exchangeRateDate: d.exchangeRateDate?.slice(0, 10) ?? '',
    notes: d.notes ?? '',
    buyerReference: d.buyerReference ?? '',
    lines: d.lines.map((l) => ({
      catalogItemId: l.catalogItemId ?? null,
      name: l.name,
      description: l.description ?? '',
      quantity: l.quantity,
      unitCode: l.unitCode,
      netUnitPrice: l.netUnitPrice,
      taxCategory: l.taxCategory,
      vatRatePercent: l.vatRatePercent,
    })),
  }
}

export default function DocumentFormPage() {
  const { t } = useTranslation('documents')
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { id } = useParams<{ id: string }>()
  const isEdit = !!id
  const [serverError, setServerError] = useState<string | null>(null)
  const [pickerOpen, setPickerOpen] = useState(false)
  const [abschlagPickerOpen, setAbschlagPickerOpen] = useState(false)
  const [abschlagIds, setAbschlagIds] = useState<string[]>([])
  const [selectedAbschlaege, setSelectedAbschlaege] = useState<
    SalesDocumentListItem[]
  >([])
  const entitlements = useEntitlements()
  const canFx = hasCapability(
    entitlements.data?.capabilities,
    'ForeignCurrencyInvoicing',
  )
  const canDownPayment = hasCapability(
    entitlements.data?.capabilities,
    'DownPaymentInvoices',
  )

  const schema = useMemo(() => makeDocumentSchema(t), [t])
  const form = useForm<DocumentFormValues>({
    resolver: zodResolver(schema) as unknown as Resolver<DocumentFormValues>,
    defaultValues: emptyDocumentForm(),
  })
  const { register, handleSubmit, reset, setError, control, watch } = form
  const errors = form.formState.errors
  const { fields, append, remove } = useFieldArray({ control, name: 'lines' })

  const existing = useQuery({
    queryKey: ['document', id],
    queryFn: () => getSalesDocument(id!),
    enabled: isEdit,
  })

  const isReadOnly = isEdit && existing.data && existing.data.status !== DocumentStatus.Draft

  useEffect(() => {
    if (existing.data) reset(toFormValues(existing.data))
  }, [existing.data, reset])

  // Live preview totals — the SERVER recomputes authoritatively on save.
  const watchedLines = watch('lines')
  const documentType = watch('documentType')
  const currency = watch('currency')
  const partnerId = watch('partnerId')
  const totals = useMemo(
    () =>
      computePreviewTotals(
        (watchedLines ?? []).map((l) => ({
          quantity: Number(l.quantity),
          netUnitPrice: Number(l.netUnitPrice),
          taxCategory: Number(l.taxCategory),
          vatRatePercent: Number(l.vatRatePercent),
        })),
      ),
    [watchedLines],
  )

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      const body = toCreateRequest(values)
      if (isEdit) {
        await updateSalesDocument(id!, body)
      } else if (values.documentType === DocumentType.Abschlagsrechnung) {
        await createAbschlag(body)
      } else if (values.documentType === DocumentType.Schlussrechnung) {
        const { documentType: _, ...finalBody } = body
        void _
        await createFinalInvoice({
          ...finalBody,
          abschlagDocumentIds: abschlagIds,
        })
      } else {
        await createSalesDocument(body)
      }
      await queryClient.invalidateQueries({ queryKey: ['documents'] })
      navigate('/documents')
    } catch (err) {
      const fieldErrors = extractFieldErrors(err)
      if (fieldErrors) {
        for (const [k, message] of Object.entries(fieldErrors)) {
          setError(mapServerField(k) as Path<DocumentFormValues>, { message })
        }
      } else if (err instanceof ApiError && err.status === 409) {
        setServerError(t('form.errors.conflict'))
      } else {
        setServerError(t('form.errors.server'))
      }
    }
  })

  // Snapshot a catalog pick onto a fresh line (CATL-02 seam — the catalog is not the
  // source of truth once the line exists).
  const onPickCatalog = (item: CatalogLineItem) => {
    append({
      catalogItemId: item.id,
      name: item.name,
      description: '',
      quantity: 1,
      unitCode: (UNIT_CODES as readonly string[]).includes(item.unitCode)
        ? item.unitCode
        : 'C62',
      netUnitPrice: item.netPrice,
      taxCategory: item.taxCategory,
      vatRatePercent: item.vatRatePercent ?? 0,
    })
    setPickerOpen(false)
  }

  if (isEdit && existing.isLoading) {
    return (
      <main className="app-main">
        <p className="text-muted-foreground">{t('table.loading')}</p>
      </main>
    )
  }
  if (isEdit && existing.isError) {
    return (
      <main className="app-main">
        <p className="text-destructive">{t('loadError')}</p>
      </main>
    )
  }

  return (
    <main className="app-main" style={{ maxWidth: '960px' }}>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">
          {isEdit ? t('form.editTitle') : t('form.createTitle')}
        </h1>
        <Link
          to="/documents"
          className={cn(buttonVariants({ variant: 'outline' }))}
        >
          {t('actions.back')}
        </Link>
      </div>

      {isReadOnly && (
        <div className="mb-4 rounded-md border border-border bg-muted p-4">
          <p className="text-sm">{t('form.readonly.notice')}</p>
          <Link
            to={`/documents/${id}`}
            className="text-sm text-primary hover:underline"
          >
            {t('form.readonly.toDetail')}
          </Link>
        </div>
      )}

      <Form {...form}>
        <form onSubmit={onSubmit} className="flex flex-col gap-5">
          <Card>
            <CardHeader>
              <CardTitle>{t('form.sections.header')}</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-1.5">
                <Label>{t('form.fields.documentType')}</Label>
                <Select
                  disabled={isReadOnly}
                  {...register('documentType', { setValueAs: (v) => Number(v) })}
                >
                  {TYPE_VALUES.map((v) => (
                    <option
                      key={v}
                      value={v}
                      disabled={
                        (v === DocumentType.Abschlagsrechnung ||
                          v === DocumentType.Schlussrechnung) &&
                        !canDownPayment
                      }
                    >
                      {t(`type.${v}`)}
                    </option>
                  ))}
                </Select>
              </div>
              <TextField
                label={t('form.fields.documentDate')}
                type="date"
                disabled={isReadOnly}
                error={errors.documentDate?.message}
                {...register('documentDate')}
              />
              <div className="flex flex-col gap-1.5">
                <Label>{t('form.fields.currency')}</Label>
                <Select
                  disabled={isReadOnly}
                  {...register('currency')}
                >
                  {CURRENCIES.map((code) => (
                    <option key={code} value={code} disabled={code !== 'EUR' && !canFx}>
                      {code}
                    </option>
                  ))}
                </Select>
              </div>
              {currency !== 'EUR' && (
                <>
                  <TextField
                    label={t('form.fields.exchangeRate', { currency })}
                    type="number"
                    step="0.000001"
                    disabled={isReadOnly}
                    error={errors.exchangeRate?.message}
                    {...register('exchangeRate')}
                  />
                  <TextField
                    label={t('form.fields.exchangeRateDate')}
                    type="date"
                    disabled={isReadOnly}
                    error={errors.exchangeRateDate?.message}
                    {...register('exchangeRateDate')}
                  />
                </>
              )}
              <TextField
                label={t('form.fields.partnerId')}
                placeholder={t('form.partnerPlaceholder')}
                disabled={isReadOnly}
                error={errors.partnerId?.message}
                {...register('partnerId')}
              />
              <TextField
                label={t('form.fields.buyerReference')}
                disabled={isReadOnly}
                error={errors.buyerReference?.message}
                {...register('buyerReference')}
              />
              <div className="col-span-2 flex flex-col gap-1.5">
                <Label>{t('form.fields.notes')}</Label>
                <Input disabled={isReadOnly} {...register('notes')} />
              </div>
              {!canDownPayment && (
                <UpgradeHint className="col-span-2" />
              )}
              {!canFx && (
                <UpgradeHint className="col-span-2" />
              )}
            </CardContent>
          </Card>

          {documentType === DocumentType.Schlussrechnung && (
            <Card>
              <CardHeader className="flex flex-row items-center justify-between">
                <CardTitle>{t('form.sections.prepayments')}</CardTitle>
                {!isReadOnly && canDownPayment && (
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => setAbschlagPickerOpen(true)}
                    disabled={!partnerId}
                  >
                    {t('form.abschlagPicker.open')}
                  </Button>
                )}
              </CardHeader>
              <CardContent className="flex flex-col gap-2 text-sm">
                {selectedAbschlaege.map((item) => (
                  <div key={item.id} className="flex justify-between">
                    <span>{item.documentNumber}</span>
                    <span>{eurFmt.format(item.totalGross)}</span>
                  </div>
                ))}
                <div className="flex justify-between border-t border-border pt-2 font-medium">
                  <span>{t('form.totals.residual')}</span>
                  <span>
                    {eurFmt.format(
                      totals.gross -
                        selectedAbschlaege.reduce(
                          (sum, item) => sum + item.totalGross,
                          0,
                        ),
                    )}
                  </span>
                </div>
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader className="flex flex-row items-center justify-between">
              <CardTitle>{t('form.sections.lines')}</CardTitle>
              {!isReadOnly && (
                <div className="flex gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => append(emptyLine())}
                  >
                    {t('actions.addLine')}
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => setPickerOpen(true)}
                  >
                    {t('actions.addFromCatalog')}
                  </Button>
                </div>
              )}
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              {fields.length === 0 && (
                <p className="text-sm text-muted-foreground">
                  {t('form.noLines')}
                </p>
              )}
              {typeof errors.lines?.message === 'string' && (
                <p className="text-sm text-destructive">{errors.lines.message}</p>
              )}

              {fields.map((field, i) => (
                <div
                  key={field.id}
                  className="grid grid-cols-12 items-end gap-2 rounded-md border border-border p-3"
                >
                  <div className="col-span-12 flex flex-col gap-1.5 md:col-span-4">
                    <Label>{t('form.fields.lineName')}</Label>
                    <Input
                      disabled={isReadOnly}
                      {...register(`lines.${i}.name` as const)}
                    />
                    {errors.lines?.[i]?.name?.message && (
                      <p className="text-xs text-destructive">
                        {errors.lines[i]?.name?.message}
                      </p>
                    )}
                  </div>
                  <div className="col-span-4 flex flex-col gap-1.5 md:col-span-1">
                    <Label>{t('form.fields.quantity')}</Label>
                    <Input
                      type="number"
                      step="0.01"
                      disabled={isReadOnly}
                      {...register(`lines.${i}.quantity` as const)}
                    />
                  </div>
                  <div className="col-span-4 flex flex-col gap-1.5 md:col-span-2">
                    <Label>{t('form.fields.unitCode')}</Label>
                    <Select
                      disabled={isReadOnly}
                      {...register(`lines.${i}.unitCode` as const)}
                    >
                      {UNIT_CODES.map((code) => (
                        <option key={code} value={code}>
                          {unitLabel(code)}
                        </option>
                      ))}
                    </Select>
                  </div>
                  <div className="col-span-4 flex flex-col gap-1.5 md:col-span-2">
                    <Label>{t('form.fields.netUnitPrice')}</Label>
                    <Input
                      type="number"
                      step="0.01"
                      disabled={isReadOnly}
                      {...register(`lines.${i}.netUnitPrice` as const)}
                    />
                  </div>
                  <div className="col-span-6 flex flex-col gap-1.5 md:col-span-2">
                    <Label>{t('form.fields.taxCategory')}</Label>
                    <Select
                      disabled={isReadOnly}
                      {...register(`lines.${i}.taxCategory` as const, {
                        setValueAs: (v) => Number(v),
                      })}
                    >
                      {TAX_KEYS.map((k) => (
                        <option key={k} value={TaxCategory[k]}>
                          {t(`form.taxCategory.${k}`)}
                        </option>
                      ))}
                    </Select>
                  </div>
                  <div className="col-span-4 flex flex-col gap-1.5 md:col-span-1">
                    <Label>{t('form.fields.vatRatePercent')}</Label>
                    <Input
                      type="number"
                      step="0.01"
                      disabled={isReadOnly}
                      {...register(`lines.${i}.vatRatePercent` as const)}
                    />
                  </div>
                  {!isReadOnly && (
                    <div className="col-span-2 md:col-span-12 md:flex md:justify-end">
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        onClick={() => remove(i)}
                      >
                        {t('actions.removeLine')}
                      </Button>
                    </div>
                  )}
                </div>
              ))}

              {/* Live preview — final totals computed on save. */}
              <div className="ml-auto flex w-full max-w-xs flex-col gap-1 border-t border-border pt-3 text-sm">
                <div className="flex justify-between">
                  <span className="text-muted-foreground">
                    {t('form.totals.net')}
                  </span>
                  <span className="tabular-nums">{eurFmt.format(totals.net)}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-muted-foreground">
                    {t('form.totals.tax')}
                  </span>
                  <span className="tabular-nums">{eurFmt.format(totals.tax)}</span>
                </div>
                <div className="flex justify-between font-medium">
                  <span>{t('form.totals.gross')}</span>
                  <span className="tabular-nums">
                    {eurFmt.format(totals.gross)}
                  </span>
                </div>
                <p className="mt-1 text-xs text-muted-foreground">
                  {t('form.totals.note')}
                </p>
              </div>
            </CardContent>
          </Card>

          {serverError && <p className="text-sm text-destructive">{serverError}</p>}

          {!isReadOnly && (
            <div className="flex gap-2">
              <Button type="submit" disabled={form.formState.isSubmitting}>
                {form.formState.isSubmitting
                  ? t('actions.saving')
                  : t('actions.save')}
              </Button>
              <Link
                to="/documents"
                className={cn(buttonVariants({ variant: 'ghost' }))}
              >
                {t('actions.cancel')}
              </Link>
            </div>
          )}
        </form>
      </Form>

      {pickerOpen && (
        <CatalogPicker onPick={onPickCatalog} onClose={() => setPickerOpen(false)} />
      )}
      <AbschlagSelectDialog
        open={abschlagPickerOpen}
        partnerId={partnerId ?? ''}
        selectedIds={abschlagIds}
        onChange={(ids, docs) => {
          setAbschlagIds(ids)
          setSelectedAbschlaege(docs)
        }}
        onOpenChange={setAbschlagPickerOpen}
      />
    </main>
  )
}

// --- Catalog picker (CATL-02 seam) -------------------------------------------
interface CatalogPickerProps {
  onPick: (item: CatalogLineItem) => void
  onClose: () => void
}
function CatalogPicker({ onPick, onClose }: CatalogPickerProps) {
  const { t } = useTranslation('documents')
  const [search, setSearch] = useState('')
  const [debounced, setDebounced] = useState('')

  useEffect(() => {
    const id = setTimeout(() => setDebounced(search), 250)
    return () => clearTimeout(id)
  }, [search])

  const query = useQuery({
    queryKey: ['catalog-picker', debounced],
    queryFn: () => lookupCatalogItems(debounced || undefined),
    placeholderData: keepPreviousData,
  })

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-black/40 p-4 pt-24"
      onClick={onClose}
    >
      <div
        className="w-full max-w-lg rounded-md border border-border bg-background p-4 shadow-lg"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-lg font-semibold">
            {t('form.catalogPicker.title')}
          </h2>
          <Button variant="ghost" size="sm" onClick={onClose}>
            {t('form.catalogPicker.close')}
          </Button>
        </div>
        <Input
          autoFocus
          placeholder={t('form.catalogPicker.search')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <ul className="mt-3 max-h-72 divide-y divide-border overflow-y-auto">
          {(query.data ?? []).length === 0 ? (
            <li className="py-3 text-sm text-muted-foreground">
              {t('form.catalogPicker.empty')}
            </li>
          ) : (
            query.data!.map((item) => (
              <li key={item.id}>
                <button
                  type="button"
                  className="flex w-full items-center justify-between gap-3 py-2 text-left text-sm hover:bg-muted"
                  onClick={() => onPick(item)}
                >
                  <span>
                    <span className="font-medium">{item.name}</span>
                    <span className="ml-2 text-muted-foreground">
                      {item.itemNumber}
                    </span>
                  </span>
                  <span className="tabular-nums text-muted-foreground">
                    {eurFmt.format(item.netPrice)}
                  </span>
                </button>
              </li>
            ))
          )}
        </ul>
      </div>
    </div>
  )
}

// --- Local field helper: shadcn Label + Input + inline error message. --------
interface TextFieldProps extends ComponentProps<'input'> {
  label: string
  error?: string
}
const TextField = forwardRef<HTMLInputElement, TextFieldProps>(
  ({ label, error, className, ...props }, ref) => (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <Label>{label}</Label>
      <Input ref={ref} {...props} />
      {error && <p className="text-sm text-destructive">{error}</p>}
    </div>
  ),
)
TextField.displayName = 'TextField'
