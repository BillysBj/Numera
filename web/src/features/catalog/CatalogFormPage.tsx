import { forwardRef, useEffect, useMemo, useState, type ComponentProps } from 'react'
import { useNavigate, useParams, Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useForm, type Path, type Resolver } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  archiveCatalogItem,
  createCatalogItem,
  getCatalogItem,
  unarchiveCatalogItem,
  updateCatalogItem,
  ApiError,
  CatalogItemKind,
  TaxCategory,
  type CatalogItemDetail,
} from '@/lib/api/catalog'
import { UNIT_CODES, defaultUnitFor, unitLabel } from './units'
import {
  emptyCatalogForm,
  makeCatalogSchema,
  toCatalogWriteRequest,
  type CatalogFormValues,
} from './catalogSchema'
import { Form } from '@/components/ui/form'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'

const TAX_KEYS = ['S', 'AE', 'K', 'E', 'Z', 'G', 'O'] as const

// Maps a server ValidationProblem field key (PascalCase, e.g. "ItemNumber") onto the
// react-hook-form path so 400/409 field errors land on the right control.
function mapServerField(key: string): string {
  return key
    .split('.')
    .map((seg) => seg.charAt(0).toLowerCase() + seg.slice(1))
    .join('.')
}

// Extract {field: message} from a 400 ValidationProblem OR the 409 duplicate-article
// -number Conflict (both carry the same `errors` shape). The shared partner extractor
// only handles 400, so the catalog form has its own that also accepts 409.
function extractFieldErrors(err: unknown): Record<string, string> | null {
  if (!(err instanceof ApiError) || (err.status !== 400 && err.status !== 409)) {
    return null
  }
  const body = err.body as { errors?: Record<string, string[]> } | undefined
  if (!body?.errors) return null
  const out: Record<string, string> = {}
  for (const [key, messages] of Object.entries(body.errors)) {
    if (messages?.length) out[key] = messages[0]
  }
  return out
}

function toFormValues(c: CatalogItemDetail): CatalogFormValues {
  return {
    itemNumber: c.itemNumber,
    name: c.name,
    description: c.description ?? '',
    kind: c.kind,
    unitCode: (UNIT_CODES as readonly string[]).includes(c.unitCode)
      ? (c.unitCode as CatalogFormValues['unitCode'])
      : 'C62',
    netPrice: c.netPrice,
    currency: c.currency,
    taxCategory: c.taxCategory,
    vatRatePercent: c.vatRatePercent ?? undefined,
    costPrice: c.costPrice ?? undefined,
  }
}

export default function CatalogFormPage() {
  const { t } = useTranslation('catalog')
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { id } = useParams<{ id: string }>()
  const isEdit = !!id
  const [serverError, setServerError] = useState<string | null>(null)

  const schema = useMemo(() => makeCatalogSchema(t), [t])
  const form = useForm<CatalogFormValues>({
    resolver: zodResolver(schema) as unknown as Resolver<CatalogFormValues>,
    defaultValues: emptyCatalogForm(),
  })
  const { register, handleSubmit, reset, setError, setValue, watch } = form
  const errors = form.formState.errors
  const kind = watch('kind')

  const existing = useQuery({
    queryKey: ['catalog-item', id],
    queryFn: () => getCatalogItem(id!),
    enabled: isEdit,
  })

  useEffect(() => {
    if (existing.data) reset(toFormValues(existing.data))
  }, [existing.data, reset])

  // Archive/unarchive (edit mode only) — no hard delete for catalog items.
  const archiveMutation = useMutation({
    mutationFn: () =>
      existing.data?.archived
        ? unarchiveCatalogItem(id!)
        : archiveCatalogItem(id!),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['catalog'] })
      await queryClient.invalidateQueries({ queryKey: ['catalog-item', id] })
    },
  })

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      const body = toCatalogWriteRequest(values)
      if (isEdit) {
        await updateCatalogItem(id!, body)
      } else {
        await createCatalogItem(body)
      }
      await queryClient.invalidateQueries({ queryKey: ['catalog'] })
      navigate('/catalog')
    } catch (err) {
      const fieldErrors = extractFieldErrors(err)
      if (fieldErrors) {
        for (const [k, message] of Object.entries(fieldErrors)) {
          setError(mapServerField(k) as Path<CatalogFormValues>, { message })
        }
      } else {
        setServerError(t('form.errors.server'))
      }
    }
  })

  // When the kind toggles, snap the unit to the kind's sensible default (C62 / HUR),
  // mirroring the server's UnitOfMeasure.DefaultFor.
  const onKindChange = (kind: CatalogItemKind) => {
    setValue('kind', kind)
    setValue('unitCode', defaultUnitFor(kind), { shouldValidate: false })
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
    <main className="app-main" style={{ maxWidth: '820px' }}>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">
          {isEdit ? t('form.editTitle') : t('form.createTitle')}
        </h1>
        <div className="flex gap-2">
          {isEdit && (
            <Button
              variant="outline"
              disabled={archiveMutation.isPending}
              onClick={() => {
                const msg = existing.data?.archived
                  ? t('confirmUnarchive')
                  : t('confirmArchive')
                if (window.confirm(msg)) archiveMutation.mutate()
              }}
            >
              {existing.data?.archived
                ? t('actions.unarchive')
                : t('actions.archive')}
            </Button>
          )}
          <Link to="/catalog" className={cn(buttonVariants({ variant: 'outline' }))}>
            {t('actions.back')}
          </Link>
        </div>
      </div>

      <Form {...form}>
        <form onSubmit={onSubmit} className="flex flex-col gap-5">
          <Card>
            <CardHeader>
              <CardTitle>{t('form.sections.master')}</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-3">
              <TextField
                label={t('form.fields.itemNumber')}
                error={errors.itemNumber?.message}
                {...register('itemNumber')}
              />
              <div className="flex flex-col gap-1.5">
                <Label>{t('form.fields.kind')}</Label>
                <Select
                  value={kind}
                  onChange={(e) =>
                    onKindChange(Number(e.target.value) as CatalogItemKind)
                  }
                >
                  <option value={CatalogItemKind.Product}>
                    {t('form.kind.product')}
                  </option>
                  <option value={CatalogItemKind.Service}>
                    {t('form.kind.service')}
                  </option>
                </Select>
              </div>
              <TextField
                className="col-span-2"
                label={t('form.fields.name')}
                error={errors.name?.message}
                {...register('name')}
              />
              <div className="col-span-2 flex flex-col gap-1.5">
                <Label>{t('form.fields.description')}</Label>
                <Input {...register('description')} />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label>{t('form.fields.unitCode')}</Label>
                <Select {...register('unitCode')}>
                  {UNIT_CODES.map((code) => (
                    <option key={code} value={code}>
                      {unitLabel(code)}
                    </option>
                  ))}
                </Select>
                {errors.unitCode?.message && (
                  <p className="text-sm text-destructive">
                    {errors.unitCode.message}
                  </p>
                )}
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t('form.sections.pricing')}</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-3">
              <TextField
                type="number"
                step="0.01"
                label={t('form.fields.netPrice')}
                error={errors.netPrice?.message}
                {...register('netPrice')}
              />
              <TextField
                label={t('form.fields.currency')}
                error={errors.currency?.message}
                {...register('currency')}
              />
              <div className="flex flex-col gap-1.5">
                <Label>{t('form.fields.taxCategory')}</Label>
                <Select
                  {...register('taxCategory', { setValueAs: (v) => Number(v) })}
                >
                  {TAX_KEYS.map((k) => (
                    <option key={k} value={TaxCategory[k]}>
                      {t(`form.taxCategory.${k}`)}
                    </option>
                  ))}
                </Select>
              </div>
              <TextField
                type="number"
                step="0.01"
                label={t('form.fields.vatRatePercent')}
                error={errors.vatRatePercent?.message}
                {...register('vatRatePercent')}
              />
              <TextField
                type="number"
                step="0.01"
                label={t('form.fields.costPrice')}
                error={errors.costPrice?.message}
                {...register('costPrice')}
              />
            </CardContent>
          </Card>

          {serverError && (
            <p className="text-sm text-destructive">{serverError}</p>
          )}

          <div className="flex gap-2">
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting
                ? t('actions.saving')
                : t('actions.save')}
            </Button>
            <Link
              to="/catalog"
              className={cn(buttonVariants({ variant: 'ghost' }))}
            >
              {t('actions.cancel')}
            </Link>
          </div>
        </form>
      </Form>
    </main>
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
