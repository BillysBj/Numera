import { forwardRef, useEffect, useMemo, useState, type ComponentProps } from 'react'
import { useNavigate, useParams, Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useForm, useFieldArray, type Path, type Resolver } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  createContact,
  createPartner,
  extractValidationErrors,
  getPartner,
  updatePartner,
  PartnerLanguage,
  TaxCategory,
  type PartnerDetail,
} from '@/lib/api/partners'
import {
  emptyPartnerForm,
  makePartnerSchema,
  toWriteRequest,
  type PartnerFormValues,
} from './partnerSchema'
import { Form } from '@/components/ui/form'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'

const TAX_KEYS = ['S', 'AE', 'K', 'E', 'Z', 'G', 'O'] as const

// Maps a server ValidationProblem field key (PascalCase, e.g. "BillingAddress.City"
// or the synthetic "Roles") onto the react-hook-form path so 400s land on fields.
function mapServerField(key: string): string {
  if (key === 'Roles') return 'isCustomer'
  return key
    .split('.')
    .map((seg) => seg.charAt(0).toLowerCase() + seg.slice(1))
    .join('.')
}

function toFormValues(p: PartnerDetail): PartnerFormValues {
  const addr = (a: PartnerDetail['billingAddress']) => ({
    street: a.street,
    line2: a.line2 ?? '',
    postalCode: a.postalCode,
    city: a.city,
    countryCode: a.countryCode,
    poBox: a.poBox ?? '',
  })
  return {
    name: p.name,
    legalForm: p.legalForm ?? '',
    billingAddress: addr(p.billingAddress),
    hasShippingAddress: !!p.shippingAddress,
    shippingAddress: p.shippingAddress
      ? addr(p.shippingAddress)
      : emptyPartnerForm().shippingAddress,
    vatId: p.vatId ?? '',
    taxNumber: p.taxNumber ?? '',
    email: p.email ?? '',
    phone: p.phone ?? '',
    website: p.website ?? '',
    paymentTermsNetDays: p.paymentTermsNetDays ?? undefined,
    skontoPercent: p.skontoPercent ?? undefined,
    skontoDays: p.skontoDays ?? undefined,
    defaultCurrency: p.defaultCurrency,
    language: p.language,
    defaultTaxCategory: p.defaultTaxCategory ?? null,
    isCustomer: p.isCustomer,
    isSupplier: p.isSupplier,
    customerNumber: p.customerNumber ?? '',
    supplierNumber: p.supplierNumber ?? '',
    contacts: [],
  }
}

export default function PartnerFormPage() {
  const { t } = useTranslation('partners')
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { id } = useParams<{ id: string }>()
  const isEdit = !!id
  const [serverError, setServerError] = useState<string | null>(null)

  const schema = useMemo(() => makePartnerSchema(t), [t])
  const form = useForm<PartnerFormValues>({
    resolver: zodResolver(schema) as unknown as Resolver<PartnerFormValues>,
    defaultValues: emptyPartnerForm(),
  })
  const { register, handleSubmit, watch, reset, setError, control } = form
  const errors = form.formState.errors

  const existing = useQuery({
    queryKey: ['partner', id],
    queryFn: () => getPartner(id!),
    enabled: isEdit,
  })

  useEffect(() => {
    if (existing.data) reset(toFormValues(existing.data))
  }, [existing.data, reset])

  const contacts = useFieldArray({ control, name: 'contacts' })
  const hasShipping = watch('hasShippingAddress')

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      const body = toWriteRequest(values)
      let targetId = id
      if (isEdit) {
        await updatePartner(id!, body)
      } else {
        const created = await createPartner(body)
        targetId = created.id
        for (const c of values.contacts) {
          await createContact(created.id, {
            salutation: c.salutation || null,
            firstName: c.firstName || null,
            lastName: c.lastName,
            email: c.email || null,
            phone: c.phone || null,
            position: c.position || null,
            isPrimary: c.isPrimary,
          })
        }
      }
      await queryClient.invalidateQueries({ queryKey: ['partners'] })
      navigate(`/partners/${targetId}`)
    } catch (err) {
      const fieldErrors = extractValidationErrors(err)
      if (fieldErrors) {
        for (const [k, message] of Object.entries(fieldErrors)) {
          setError(mapServerField(k) as Path<PartnerFormValues>, { message })
        }
      } else {
        setServerError(t('form.errors.server'))
      }
    }
  })

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

  const addressFields = (prefix: 'billingAddress' | 'shippingAddress') => {
    const e = errors[prefix]
    return (
      <div className="grid grid-cols-2 gap-3">
        <TextField
          className="col-span-2"
          label={t('form.fields.street')}
          error={e?.street?.message}
          {...register(`${prefix}.street`)}
        />
        <TextField
          className="col-span-2"
          label={t('form.fields.line2')}
          {...register(`${prefix}.line2`)}
        />
        <TextField
          label={t('form.fields.postalCode')}
          error={e?.postalCode?.message}
          {...register(`${prefix}.postalCode`)}
        />
        <TextField
          label={t('form.fields.city')}
          error={e?.city?.message}
          {...register(`${prefix}.city`)}
        />
        <TextField
          label={t('form.fields.countryCode')}
          error={e?.countryCode?.message}
          {...register(`${prefix}.countryCode`)}
        />
        <TextField
          label={t('form.fields.poBox')}
          {...register(`${prefix}.poBox`)}
        />
      </div>
    )
  }

  return (
    <main className="app-main" style={{ maxWidth: '820px' }}>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">
          {isEdit ? t('form.editTitle') : t('form.createTitle')}
        </h1>
        <Link to="/partners" className={cn(buttonVariants({ variant: 'outline' }))}>
          {t('actions.back')}
        </Link>
      </div>

      <Form {...form}>
        <form onSubmit={onSubmit} className="flex flex-col gap-5">
          <Card>
            <CardHeader>
              <CardTitle>{t('form.sections.master')}</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-3">
              <TextField
                className="col-span-2"
                label={t('form.fields.name')}
                error={errors.name?.message}
                {...register('name')}
              />
              <TextField
                label={t('form.fields.legalForm')}
                {...register('legalForm')}
              />
              <div className="flex flex-col gap-1.5">
                <Label>{t('form.fields.language')}</Label>
                <Select
                  {...register('language', { setValueAs: (v) => Number(v) })}
                >
                  <option value={PartnerLanguage.De}>
                    {t('form.language.de')}
                  </option>
                  <option value={PartnerLanguage.En}>
                    {t('form.language.en')}
                  </option>
                </Select>
              </div>
              <TextField
                label={t('form.fields.email')}
                {...register('email')}
              />
              <TextField
                label={t('form.fields.phone')}
                {...register('phone')}
              />
              <TextField
                label={t('form.fields.website')}
                {...register('website')}
              />
              <div className="col-span-2 flex flex-wrap gap-4 pt-1">
                <label className="flex items-center gap-2 text-sm">
                  <input type="checkbox" {...register('isCustomer')} />
                  {t('form.fields.isCustomer')}
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input type="checkbox" {...register('isSupplier')} />
                  {t('form.fields.isSupplier')}
                </label>
              </div>
              {errors.isCustomer?.message && (
                <p className="col-span-2 text-sm text-destructive">
                  {errors.isCustomer.message}
                </p>
              )}
              <TextField
                label={t('form.fields.customerNumber')}
                {...register('customerNumber')}
              />
              <TextField
                label={t('form.fields.supplierNumber')}
                {...register('supplierNumber')}
              />
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t('form.sections.billingAddress')}</CardTitle>
            </CardHeader>
            <CardContent>{addressFields('billingAddress')}</CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t('form.sections.shippingAddress')}</CardTitle>
              <label className="flex items-center gap-2 text-sm font-normal">
                <input type="checkbox" {...register('hasShippingAddress')} />
                {t('form.sections.shippingToggle')}
              </label>
            </CardHeader>
            {hasShipping && (
              <CardContent>{addressFields('shippingAddress')}</CardContent>
            )}
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t('form.sections.tax')}</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-3">
              <TextField
                label={t('form.fields.vatId')}
                error={errors.vatId?.message}
                {...register('vatId')}
              />
              <TextField
                label={t('form.fields.taxNumber')}
                {...register('taxNumber')}
              />
              <div className="flex flex-col gap-1.5">
                <Label>{t('form.fields.defaultTaxCategory')}</Label>
                <Select
                  {...register('defaultTaxCategory', {
                    setValueAs: (v) => (v === '' ? null : Number(v)),
                  })}
                >
                  <option value="">{t('form.taxCategory.none')}</option>
                  {TAX_KEYS.map((k) => (
                    <option key={k} value={TaxCategory[k]}>
                      {t(`form.taxCategory.${k}`)}
                    </option>
                  ))}
                </Select>
              </div>
              <TextField
                label={t('form.fields.defaultCurrency')}
                error={errors.defaultCurrency?.message}
                {...register('defaultCurrency')}
              />
              <TextField
                type="number"
                label={t('form.fields.paymentTermsNetDays')}
                {...register('paymentTermsNetDays')}
              />
              <TextField
                type="number"
                label={t('form.fields.skontoPercent')}
                error={errors.skontoPercent?.message}
                {...register('skontoPercent')}
              />
              <TextField
                type="number"
                label={t('form.fields.skontoDays')}
                {...register('skontoDays')}
              />
            </CardContent>
          </Card>

          {!isEdit && (
            <Card>
              <CardHeader>
                <CardTitle>{t('form.sections.contacts')}</CardTitle>
              </CardHeader>
              <CardContent className="flex flex-col gap-3">
                {contacts.fields.length === 0 && (
                  <p className="text-sm text-muted-foreground">
                    {t('form.contact.none')}
                  </p>
                )}
                {contacts.fields.map((f, i) => (
                  <div
                    key={f.id}
                    className="grid grid-cols-2 gap-3 rounded-md border border-border p-3"
                  >
                    <TextField
                      label={t('form.contact.firstName')}
                      {...register(`contacts.${i}.firstName`)}
                    />
                    <TextField
                      label={t('form.contact.lastName')}
                      error={errors.contacts?.[i]?.lastName?.message}
                      {...register(`contacts.${i}.lastName`)}
                    />
                    <TextField
                      label={t('form.contact.email')}
                      {...register(`contacts.${i}.email`)}
                    />
                    <TextField
                      label={t('form.contact.phone')}
                      {...register(`contacts.${i}.phone`)}
                    />
                    <div className="col-span-2 flex items-center justify-between">
                      <label className="flex items-center gap-2 text-sm">
                        <input
                          type="checkbox"
                          {...register(`contacts.${i}.isPrimary`)}
                        />
                        {t('form.contact.isPrimary')}
                      </label>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => contacts.remove(i)}
                      >
                        {t('actions.delete')}
                      </Button>
                    </div>
                  </div>
                ))}
                <Button
                  variant="outline"
                  size="sm"
                  className="self-start"
                  onClick={() =>
                    contacts.append({
                      salutation: '',
                      firstName: '',
                      lastName: '',
                      email: '',
                      phone: '',
                      position: '',
                      isPrimary: contacts.fields.length === 0,
                    })
                  }
                >
                  {t('form.contact.add')}
                </Button>
              </CardContent>
            </Card>
          )}

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
              to="/partners"
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
