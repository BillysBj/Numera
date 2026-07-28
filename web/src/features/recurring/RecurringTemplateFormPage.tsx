import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useFieldArray, useForm, type Path, type Resolver } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import {
  ApiError,
  createTemplate,
  extractValidationErrors,
  getTemplate,
  RecurringEndMode,
  RecurringIntervalUnit,
  RecurringStatus,
  updateTemplate,
  type RecurringTemplate,
  type RecurringTemplateRequest,
} from '@/lib/api/recurring'
import { TaxCategory } from '@/lib/api/documents'
import { listPartners } from '@/lib/api/partners'
import { UNIT_CODES, unitLabel } from '@/features/catalog/units'
import { Form } from '@/components/ui/form'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { hasCapability, useEntitlements } from '@/lib/entitlements'
import { UpgradeHint } from '@/features/shared/UpgradeHint'

const CURRENCIES = ['EUR', 'USD', 'GBP', 'CHF', 'CAD', 'AUD', 'NOK', 'SEK', 'DKK', 'PLN', 'CZK'] as const
const TAX_VALUES = Object.values(TaxCategory) as number[]
const numberField = (message: string, schema: z.ZodNumber) =>
  z.preprocess(
    (value) =>
      value === '' || value === null || value === undefined
        ? undefined
        : Number(value),
    schema.refine(Number.isFinite, message),
  )

type Translate = (key: string) => string
export function makeRecurringTemplateSchema(t: Translate) {
  const line = z.object({
    catalogItemId: z.string().nullish(),
    name: z.string().trim().min(1, t('form.errors.lineName')),
    description: z.string().trim().optional().or(z.literal('')),
    quantity: numberField(
      t('form.errors.required'),
      z.number().positive(t('form.errors.quantityPositive')),
    ),
    unitCode: z.string().trim().min(1, t('form.errors.unitCode')),
    netUnitPrice: numberField(
      t('form.errors.required'),
      z.number().min(0, t('form.errors.netPriceMin')),
    ),
    taxCategory: z.number().int(),
    vatRatePercent: numberField(
      t('form.errors.required'),
      z.number().min(0).max(100),
    ),
  })
  return z
    .object({
      name: z.string().trim().min(1, t('form.errors.name')),
      partnerId: z.string().optional().or(z.literal('')),
      currency: z.enum(CURRENCIES),
      exchangeRate: z.preprocess(
        (value) =>
          value === '' || value === null || value === undefined
            ? undefined
            : Number(value),
        z.number().positive(t('form.errors.exchangeRate')).optional(),
      ),
      exchangeRateDate: z.string().optional().or(z.literal('')),
      intervalUnit: z.number().int(),
      intervalCount: numberField(
        t('form.errors.required'),
        z.number().int().min(1, t('form.errors.intervalCount')),
      ),
      startOn: z.string().min(1, t('form.errors.required')),
      endMode: z.number().int(),
      endDate: z.string().optional().or(z.literal('')),
      maxOccurrences: z.preprocess(
        (value) =>
          value === '' || value === null || value === undefined
            ? undefined
            : Number(value),
        z.number().int().positive(t('form.errors.maxOccurrences')).optional(),
      ),
      autoFinalize: z.boolean(),
      autoSend: z.boolean(),
      status: z.number().int(),
      lines: z.array(line).min(1, t('form.errors.lines')),
    })
    .superRefine((value, ctx) => {
      if (value.currency !== 'EUR') {
        if (!value.exchangeRate)
          ctx.addIssue({ code: 'custom', path: ['exchangeRate'], message: t('form.errors.exchangeRate') })
        if (!value.exchangeRateDate)
          ctx.addIssue({ code: 'custom', path: ['exchangeRateDate'], message: t('form.errors.required') })
      }
      if (value.endMode === RecurringEndMode.UntilDate) {
        if (!value.endDate || value.endDate < value.startOn)
          ctx.addIssue({ code: 'custom', path: ['endDate'], message: t('form.errors.endDate') })
      }
      if (
        value.endMode === RecurringEndMode.AfterCount &&
        !value.maxOccurrences
      )
        ctx.addIssue({ code: 'custom', path: ['maxOccurrences'], message: t('form.errors.maxOccurrences') })
    })
}

const recurringSchema = makeRecurringTemplateSchema((key) => key)
export type RecurringFormValues = z.infer<typeof recurringSchema>

const emptyLine = () => ({
  catalogItemId: null,
  name: '',
  description: '',
  quantity: 1,
  unitCode: 'C62',
  netUnitPrice: undefined as unknown as number,
  taxCategory: TaxCategory.S,
  vatRatePercent: 19,
})
const defaults = (): RecurringFormValues => ({
  name: '',
  partnerId: '',
  currency: 'EUR',
  exchangeRate: undefined,
  exchangeRateDate: '',
  intervalUnit: RecurringIntervalUnit.Monthly,
  intervalCount: 1,
  startOn: new Date().toISOString().slice(0, 10),
  endMode: RecurringEndMode.Never,
  endDate: '',
  maxOccurrences: undefined,
  autoFinalize: true,
  autoSend: false,
  status: RecurringStatus.Active,
  lines: [emptyLine()],
})

function fromTemplate(value: RecurringTemplate): RecurringFormValues {
  return {
    ...value,
    currency: CURRENCIES.includes(
      value.currency as (typeof CURRENCIES)[number],
    )
      ? (value.currency as (typeof CURRENCIES)[number])
      : 'EUR',
    partnerId: value.partnerId ?? '',
    exchangeRate: value.exchangeRate ?? undefined,
    exchangeRateDate: value.exchangeRateDate?.slice(0, 10) ?? '',
    startOn: value.startOn.slice(0, 10),
    endDate: value.endDate?.slice(0, 10) ?? '',
    maxOccurrences: value.maxOccurrences ?? undefined,
    lines: value.lines.map((line) => ({
      catalogItemId: line.catalogItemId ?? null,
      name: line.name,
      description: line.description ?? '',
      quantity: line.quantity,
      unitCode: line.unitCode,
      netUnitPrice: line.netUnitPrice,
      taxCategory: line.taxCategory,
      vatRatePercent: line.vatRatePercent,
    })),
  }
}

const serverPath = (key: string) =>
  key
    .replace(/\[(\d+)\]/g, '.$1')
    .split('.')
    .map((part) => part.charAt(0).toLowerCase() + part.slice(1))
    .join('.')

export default function RecurringTemplateFormPage() {
  const { t } = useTranslation('recurring')
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const entitlements = useEntitlements()
  const allowed = hasCapability(entitlements.data?.capabilities, 'RecurringInvoices')
  const [serverError, setServerError] = useState(false)
  const schema = useMemo(() => makeRecurringTemplateSchema(t), [t])
  const form = useForm<RecurringFormValues>({
    resolver: zodResolver(schema) as unknown as Resolver<RecurringFormValues>,
    defaultValues: defaults(),
  })
  const { register, handleSubmit, setError, reset, watch, control } = form
  const errors = form.formState.errors
  const { fields, append, remove } = useFieldArray({ control, name: 'lines' })
  const existing = useQuery({
    queryKey: ['recurring-template', id],
    queryFn: () => getTemplate(id!),
    enabled: allowed && !!id,
  })
  const partners = useQuery({
    queryKey: ['partner-picker', 'recurring'],
    queryFn: () => listPartners({ pageSize: 100, role: 'customer' }),
    enabled: allowed,
  })
  useEffect(() => {
    if (existing.data) reset(fromTemplate(existing.data))
  }, [existing.data, reset])
  const currency = watch('currency')
  const endMode = watch('endMode')
  const autoFinalize = watch('autoFinalize')

  const onSubmit = handleSubmit(async (values) => {
    setServerError(false)
    const nullable = (value?: string) => value?.trim() || null
    const body: RecurringTemplateRequest = {
      ...values,
      intervalUnit: values.intervalUnit as RecurringIntervalUnit,
      endMode: values.endMode as RecurringEndMode,
      status: values.status as RecurringStatus,
      name: values.name.trim(),
      partnerId: nullable(values.partnerId),
      exchangeRate: values.currency === 'EUR' ? null : values.exchangeRate,
      exchangeRateDate:
        values.currency === 'EUR' ? null : nullable(values.exchangeRateDate),
      endDate:
        values.endMode === RecurringEndMode.UntilDate
          ? nullable(values.endDate)
          : null,
      maxOccurrences:
        values.endMode === RecurringEndMode.AfterCount
          ? values.maxOccurrences
          : null,
      lines: values.lines.map((line) => ({
        ...line,
        catalogItemId: line.catalogItemId ?? null,
        name: line.name.trim(),
        description: nullable(line.description),
        taxCategory: line.taxCategory as TaxCategory,
      })),
    }
    try {
      if (id) await updateTemplate(id, body)
      else await createTemplate(body)
      await queryClient.invalidateQueries({ queryKey: ['recurring-templates'] })
      navigate('/recurring')
    } catch (error) {
      const fieldErrors = extractValidationErrors(error)
      if (fieldErrors) {
        for (const [field, message] of Object.entries(fieldErrors))
          setError(serverPath(field) as Path<RecurringFormValues>, { message })
      } else {
        void (error instanceof ApiError)
        setServerError(true)
      }
    }
  })

  if (entitlements.isLoading)
    return <main className="app-main">{t('loading')}</main>
  if (!allowed)
    return (
      <main className="app-main">
        <h1 className="mb-4 text-2xl font-semibold">{t('form.createTitle')}</h1>
        <UpgradeHint />
      </main>
    )
  if (id && existing.isLoading)
    return <main className="app-main">{t('loading')}</main>
  if (id && existing.isError)
    return <main className="app-main text-destructive">{t('loadError')}</main>

  return (
    <main className="app-main" style={{ maxWidth: '960px' }}>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">
          {t(id ? 'form.editTitle' : 'form.createTitle')}
        </h1>
        <Link to="/recurring" className={cn(buttonVariants({ variant: 'outline' }))}>
          {t('actions.back')}
        </Link>
      </div>
      {serverError && <p role="alert" className="mb-3 text-destructive">{t('saveError')}</p>}
      <Form {...form}>
        <form onSubmit={onSubmit} className="flex flex-col gap-5">
          <Card>
            <CardHeader><CardTitle>{t('form.sections.details')}</CardTitle></CardHeader>
            <CardContent className="grid grid-cols-2 gap-3">
              <Field label={t('form.fields.name')} error={errors.name?.message} {...register('name')} />
              <div className="flex flex-col gap-1.5">
                <Label>{t('form.fields.partner')}</Label>
                <Select {...register('partnerId')}>
                  <option value="">{t('form.noPartner')}</option>
                  {partners.data?.items.map((partner) => (
                    <option key={partner.id} value={partner.id}>{partner.name}</option>
                  ))}
                </Select>
              </div>
              <div className="flex flex-col gap-1.5">
                <Label>{t('form.fields.currency')}</Label>
                <Select {...register('currency')}>
                  {CURRENCIES.map((value) => <option key={value}>{value}</option>)}
                </Select>
              </div>
              {currency !== 'EUR' && (
                <>
                  <Field type="number" step="0.000001" label={t('form.fields.exchangeRate')} error={errors.exchangeRate?.message} {...register('exchangeRate')} />
                  <Field type="date" label={t('form.fields.exchangeRateDate')} error={errors.exchangeRateDate?.message} {...register('exchangeRateDate')} />
                </>
              )}
            </CardContent>
          </Card>
          <Card>
            <CardHeader><CardTitle>{t('form.sections.schedule')}</CardTitle></CardHeader>
            <CardContent className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-1.5">
                <Label>{t('form.fields.intervalUnit')}</Label>
                <Select {...register('intervalUnit', { setValueAs: Number })}>
                  {Object.values(RecurringIntervalUnit).map((value) => <option key={value} value={value}>{t(`interval.${value}`)}</option>)}
                </Select>
              </div>
              <Field type="number" min="1" label={t('form.fields.intervalCount')} error={errors.intervalCount?.message} {...register('intervalCount')} />
              <Field type="date" label={t('form.fields.startOn')} error={errors.startOn?.message} {...register('startOn')} />
              <div className="flex flex-col gap-1.5">
                <Label>{t('form.fields.endMode')}</Label>
                <Select {...register('endMode', { setValueAs: Number })}>
                  {Object.values(RecurringEndMode).map((value) => <option key={value} value={value}>{t(`endMode.${value}`)}</option>)}
                </Select>
              </div>
              {endMode === RecurringEndMode.UntilDate && <Field type="date" label={t('form.fields.endDate')} error={errors.endDate?.message} {...register('endDate')} />}
              {endMode === RecurringEndMode.AfterCount && <Field type="number" min="1" label={t('form.fields.maxOccurrences')} error={errors.maxOccurrences?.message} {...register('maxOccurrences')} />}
            </CardContent>
          </Card>
          <Card>
            <CardHeader><CardTitle>{t('form.sections.lines')}</CardTitle></CardHeader>
            <CardContent className="flex flex-col gap-4">
              {fields.map((field, index) => (
                <div key={field.id} className="grid grid-cols-6 gap-2 rounded-md border p-3">
                  <Field className="col-span-3" label={t('form.fields.lineName')} error={errors.lines?.[index]?.name?.message} {...register(`lines.${index}.name`)} />
                  <Field label={t('form.fields.quantity')} type="number" step="0.001" error={errors.lines?.[index]?.quantity?.message} {...register(`lines.${index}.quantity`)} />
                  <div className="flex flex-col gap-1.5">
                    <Label>{t('form.fields.unit')}</Label>
                    <Select {...register(`lines.${index}.unitCode`)}>
                      {UNIT_CODES.map((unit) => <option key={unit} value={unit}>{unitLabel(unit)}</option>)}
                    </Select>
                  </div>
                  <Button type="button" variant="ghost" onClick={() => remove(index)}>{t('actions.remove')}</Button>
                  <Field className="col-span-2" label={t('form.fields.price')} type="number" min="0" step="0.01" error={errors.lines?.[index]?.netUnitPrice?.message} {...register(`lines.${index}.netUnitPrice`)} />
                  <div className="flex flex-col gap-1.5">
                    <Label>{t('form.fields.taxCategory')}</Label>
                    <Select {...register(`lines.${index}.taxCategory`, { setValueAs: Number })}>
                      {TAX_VALUES.map((value) => <option key={value} value={value}>{Object.keys(TaxCategory)[value]}</option>)}
                    </Select>
                  </div>
                  <Field label={t('form.fields.vatRate')} type="number" min="0" max="100" step="0.01" {...register(`lines.${index}.vatRatePercent`)} />
                  <Field className="col-span-2" label={t('form.fields.description')} {...register(`lines.${index}.description`)} />
                </div>
              ))}
              {typeof errors.lines?.message === 'string' && <p className="text-sm text-destructive">{errors.lines.message}</p>}
              <Button type="button" variant="outline" onClick={() => append(emptyLine())}>{t('actions.addLine')}</Button>
            </CardContent>
          </Card>
          <Card>
            <CardHeader><CardTitle>{t('form.sections.automation')}</CardTitle></CardHeader>
            <CardContent className="flex flex-col gap-3">
              <label className="flex items-center gap-2">
                <input type="checkbox" {...register('autoFinalize')} />
                {t('form.fields.autoFinalize')}
              </label>
              {!autoFinalize && <p role="status" className="text-sm text-muted-foreground">{t('form.autoFinalizeOff')}</p>}
              <label className="flex items-center gap-2">
                <input type="checkbox" {...register('autoSend')} />
                {t('form.fields.autoSend')}
              </label>
            </CardContent>
          </Card>
          <Button type="submit" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('actions.saving') : t('actions.save')}
          </Button>
        </form>
      </Form>
    </main>
  )
}

function Field({
  label,
  error,
  className,
  ...props
}: React.ComponentProps<'input'> & { label: string; error?: string }) {
  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <Label>{label}</Label>
      <Input {...props} />
      {error && <p className="text-sm text-destructive">{error}</p>}
    </div>
  )
}
