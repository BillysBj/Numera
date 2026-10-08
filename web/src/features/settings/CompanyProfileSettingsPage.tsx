import {
  forwardRef,
  useEffect,
  useMemo,
  useState,
  type ChangeEvent,
  type ComponentProps,
} from 'react'
import { useTranslation } from 'react-i18next'
import { useForm, type Path, type Resolver } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  companyLogoUrl,
  extractValidationErrors,
  getCompanyProfile,
  updateCompanyProfile,
  uploadCompanyLogo,
  ApiError,
  LOGO_ACCEPTED_TYPES,
  LOGO_MAX_BYTES,
  TaxCategory,
  type CompanyProfileDto,
} from '@/lib/api/companyProfile'
import {
  emptyCompanyProfileForm,
  makeCompanyProfileSchema,
  toUpdateCompanyProfileRequest,
  type CompanyProfileFormValues,
} from './companyProfileSchema'
import { Form } from '@/components/ui/form'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { Checkbox } from '@/components/ui/checkbox'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { getMe } from '@/lib/api'
import { cn } from '@/lib/utils'

const TAX_KEYS = ['S', 'AE', 'K', 'E', 'Z', 'G', 'O'] as const

// Maps a server ValidationProblem field key (PascalCase, e.g. "Address.Street") onto the
// react-hook-form path so 400 field errors land on the right control.
function mapServerField(key: string): string {
  return key
    .split('.')
    .map((seg) => seg.charAt(0).toLowerCase() + seg.slice(1))
    .join('.')
}

function toFormValues(p: CompanyProfileDto): CompanyProfileFormValues {
  return {
    legalName: p.legalName ?? '',
    address: {
      street: p.address?.street ?? '',
      line2: p.address?.line2 ?? undefined,
      postalCode: p.address?.postalCode ?? '',
      city: p.address?.city ?? '',
      countryCode: p.address?.countryCode ?? 'DE',
      poBox: p.address?.poBox ?? undefined,
    },
    vatId: p.vatId ?? undefined,
    taxNumber: p.taxNumber ?? undefined,
    isKleinunternehmer: p.isKleinunternehmer,
    defaultPaymentTermsNetDays: p.defaultPaymentTermsNetDays ?? undefined,
    defaultTaxCategory: p.defaultTaxCategory ?? TaxCategory.S,
    iban: p.iban ?? undefined,
    bic: p.bic ?? undefined,
    bankName: p.bankName ?? undefined,
    registerCourt: p.registerCourt ?? undefined,
    registerNumber: p.registerNumber ?? undefined,
    managingDirector: p.managingDirector ?? undefined,
    invoiceFooterText: p.invoiceFooterText ?? undefined,
    deliveryNoteFooterText: p.deliveryNoteFooterText ?? undefined,
    contactEmail: p.contactEmail ?? undefined,
    contactPhone: p.contactPhone ?? undefined,
  }
}

// The §14 issuer settings form: RHF + zodResolver over the 03-01 GET/PUT upsert API. The
// server stays authoritative; this form's zod exactly-one-tax-id gate + server-error
// mapping mirror the finalize prerequisites (a compliant invoice cannot be finalized
// without this profile).
export default function CompanyProfileSettingsPage() {
  const { t } = useTranslation('settings')
  const queryClient = useQueryClient()
  const me = useQuery({ queryKey: ['me'], queryFn: getMe })
  const isOwner = me.data?.role === 'Owner'
  const [serverError, setServerError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const schema = useMemo(() => makeCompanyProfileSchema(t), [t])
  const form = useForm<CompanyProfileFormValues>({
    resolver: zodResolver(
      schema,
    ) as unknown as Resolver<CompanyProfileFormValues>,
    defaultValues: emptyCompanyProfileForm(),
  })
  const { register, handleSubmit, reset, setError, setValue, watch } = form
  const errors = form.formState.errors
  const isKleinunternehmer = watch('isKleinunternehmer')

  const existing = useQuery({
    queryKey: ['company-profile'],
    queryFn: getCompanyProfile,
  })

  useEffect(() => {
    if (existing.data) reset(toFormValues(existing.data))
  }, [existing.data, reset])

  const mutation = useMutation({
    mutationFn: (values: CompanyProfileFormValues) =>
      updateCompanyProfile(toUpdateCompanyProfileRequest(values)),
    onSuccess: async (dto) => {
      setSaved(true)
      queryClient.setQueryData(['company-profile'], dto)
      await queryClient.invalidateQueries({ queryKey: ['company-profile'] })
    },
  })

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    setSaved(false)
    try {
      await mutation.mutateAsync(values)
    } catch (err) {
      const fieldErrors = extractValidationErrors(err)
      if (fieldErrors) {
        for (const [k, message] of Object.entries(fieldErrors)) {
          setError(mapServerField(k) as Path<CompanyProfileFormValues>, {
            message,
          })
        }
      } else {
        setServerError(t('errors.server'))
      }
    }
  })

  if (existing.isLoading) {
    return (
      <main className="app-main">
        <p className="text-muted-foreground">{t('loading')}</p>
      </main>
    )
  }
  if (existing.isError) {
    return (
      <main className="app-main">
        <p className="text-destructive">{t('loadError')}</p>
      </main>
    )
  }

  return (
    <main className="app-main" style={{ maxWidth: '820px' }}>
      <div className="mb-4">
        <h1 className="text-2xl font-semibold">{t('title')}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t('frozenNote')}</p>
      </div>

      {/* DSGVO Art. 20 data export (09-01). Owner-only both here and on the server; a direct
          same-origin GET streams the ZIP (the session cookie flows on navigation). */}
      {isOwner && (
        <Card className="mb-5">
          <CardHeader>
            <CardTitle>{t('export.title')}</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            <p className="text-sm text-muted-foreground">{t('export.description')}</p>
            <a
              href="/api/export"
              className={cn(buttonVariants({ variant: 'outline' }), 'w-fit')}
            >
              {t('export.download')}
            </a>
          </CardContent>
        </Card>
      )}

      <Form {...form}>
        <form onSubmit={onSubmit} className="flex flex-col gap-5">
          <Card>
            <CardHeader>
              <CardTitle>{t('sections.master')}</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-3">
              <TextField
                className="col-span-2"
                label={t('fields.legalName')}
                error={errors.legalName?.message}
                {...register('legalName')}
              />
              <TextField
                className="col-span-2"
                label={t('fields.street')}
                error={errors.address?.street?.message}
                {...register('address.street')}
              />
              <TextField
                className="col-span-2"
                label={t('fields.line2')}
                {...register('address.line2')}
              />
              <TextField
                label={t('fields.postalCode')}
                error={errors.address?.postalCode?.message}
                {...register('address.postalCode')}
              />
              <TextField
                label={t('fields.city')}
                error={errors.address?.city?.message}
                {...register('address.city')}
              />
              <TextField
                label={t('fields.countryCode')}
                error={errors.address?.countryCode?.message}
                {...register('address.countryCode')}
              />
              <TextField
                label={t('fields.poBox')}
                {...register('address.poBox')}
              />
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t('sections.tax')}</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-3">
              <TextField
                label={t('fields.vatId')}
                error={errors.vatId?.message}
                {...register('vatId')}
              />
              <TextField
                label={t('fields.taxNumber')}
                error={errors.taxNumber?.message}
                {...register('taxNumber')}
              />
              <label className="col-span-2 flex items-start gap-2 text-sm">
                <Checkbox
                  className="mt-0.5"
                  checked={isKleinunternehmer}
                  onCheckedChange={(c) => setValue('isKleinunternehmer', c)}
                />
                <span>
                  <span className="font-medium">{t('fields.kleinunternehmer')}</span>
                  <span className="block text-muted-foreground">
                    {t('hints.kleinunternehmer')}
                  </span>
                </span>
              </label>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t('sections.payment')}</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-3">
              <TextField
                type="number"
                step="1"
                label={t('fields.defaultPaymentTermsNetDays')}
                error={errors.defaultPaymentTermsNetDays?.message}
                {...register('defaultPaymentTermsNetDays')}
              />
              <div className="flex flex-col gap-1.5">
                <Label>{t('fields.defaultTaxCategory')}</Label>
                <Select
                  {...register('defaultTaxCategory', {
                    setValueAs: (v) => Number(v),
                  })}
                >
                  {TAX_KEYS.map((k) => (
                    <option key={k} value={TaxCategory[k]}>
                      {t(`taxCategory.${k}`)}
                    </option>
                  ))}
                </Select>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t('sections.bank')}</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-3">
              <TextField label={t('fields.iban')} {...register('iban')} />
              <TextField label={t('fields.bic')} {...register('bic')} />
              <TextField
                className="col-span-2"
                label={t('fields.bankName')}
                {...register('bankName')}
              />
              <TextField
                label={t('fields.registerCourt')}
                {...register('registerCourt')}
              />
              <TextField
                label={t('fields.registerNumber')}
                {...register('registerNumber')}
              />
              <TextField
                className="col-span-2"
                label={t('fields.managingDirector')}
                {...register('managingDirector')}
              />
              <TextField
                label={t('fields.contactEmail')}
                {...register('contactEmail')}
              />
              <TextField
                label={t('fields.contactPhone')}
                {...register('contactPhone')}
              />
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t('sections.closingTexts')}</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-col gap-3">
              {(['invoiceFooterText', 'deliveryNoteFooterText'] as const).map((field) => (
                <div key={field} className="flex flex-col gap-1.5">
                  <Label htmlFor={field}>{t(`fields.${field}`)}</Label>
                  <Textarea
                    id={field}
                    rows={4}
                    maxLength={2000}
                    aria-describedby={`${field}-hint`}
                    aria-invalid={!!errors[field]}
                    {...register(field)}
                  />
                  <p id={`${field}-hint`} className="text-sm text-muted-foreground">
                    {t(`hints.${field}`)}
                  </p>
                  {errors[field] && (
                    <p className="text-sm text-destructive">{errors[field]?.message}</p>
                  )}
                </div>
              ))}
            </CardContent>
          </Card>

          {serverError && (
            <p className="text-sm text-destructive">{serverError}</p>
          )}
          {saved && (
            <p className="text-sm text-emerald-600">{t('saved')}</p>
          )}

          <div className="flex gap-2">
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t('saving') : t('save')}
            </Button>
          </div>
        </form>
      </Form>

      {/* Logo upload lives OUTSIDE the RHF §14 form — it is a separate binary upload with its
          own endpoint (PUT /company-profile/logo), not part of the profile submit. */}
      <div className="mt-5">
        <LogoSection />
      </div>
    </main>
  )
}

// --- Logo / Briefpapier upload (separate binary upload, its own endpoint) -----
function LogoSection() {
  const { t } = useTranslation('settings')
  const [logoError, setLogoError] = useState<string | null>(null)
  const [logoSaved, setLogoSaved] = useState(false)
  const [file, setFile] = useState<File | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  // Cache-bust token for the current-logo preview; 0 → plain URL (first render).
  const [version, setVersion] = useState(0)
  const [hasLogo, setHasLogo] = useState(true)

  const upload = useMutation({
    mutationFn: (f: File) => uploadCompanyLogo(f),
    onSuccess: () => {
      setLogoSaved(true)
      setLogoError(null)
      if (previewUrl) URL.revokeObjectURL(previewUrl)
      setFile(null)
      setPreviewUrl(null)
      setHasLogo(true)
      setVersion(Date.now())
    },
    onError: (err) => {
      setLogoSaved(false)
      if (err instanceof ApiError && err.status === 409) {
        setLogoError(t('logo.noProfile'))
      } else if (err instanceof ApiError && err.status === 400) {
        setLogoError(t('logo.invalidType'))
      } else {
        setLogoError(t('errors.server'))
      }
    },
  })

  const onPick = (e: ChangeEvent<HTMLInputElement>) => {
    setLogoSaved(false)
    setLogoError(null)
    const picked = e.target.files?.[0] ?? null
    if (previewUrl) URL.revokeObjectURL(previewUrl)
    if (!picked) {
      setFile(null)
      setPreviewUrl(null)
      return
    }
    // Client-side validation mirroring the server (PNG/JPG, <= 1 MB).
    if (!LOGO_ACCEPTED_TYPES.includes(picked.type as (typeof LOGO_ACCEPTED_TYPES)[number])) {
      setFile(null)
      setPreviewUrl(null)
      setLogoError(t('logo.invalidType'))
      return
    }
    if (picked.size > LOGO_MAX_BYTES) {
      setFile(null)
      setPreviewUrl(null)
      setLogoError(t('logo.tooLarge'))
      return
    }
    setFile(picked)
    setPreviewUrl(URL.createObjectURL(picked))
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('logo.title')}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <p className="text-sm text-muted-foreground">{t('logo.hint')}</p>

        <div className="flex items-center gap-4">
          <div className="flex h-24 w-40 items-center justify-center overflow-hidden rounded-md border border-border bg-muted">
            {previewUrl ? (
              <img
                src={previewUrl}
                alt=""
                className="max-h-24 max-w-full object-contain"
              />
            ) : hasLogo ? (
              <img
                src={companyLogoUrl(version || undefined)}
                alt=""
                className="max-h-24 max-w-full object-contain"
                onError={() => setHasLogo(false)}
              />
            ) : (
              <span className="px-2 text-center text-xs text-muted-foreground">
                {t('logo.none')}
              </span>
            )}
          </div>

          <div className="flex flex-col gap-2">
            <Input
              type="file"
              accept="image/png,image/jpeg"
              onChange={onPick}
              className="max-w-xs"
            />
            <Button
              type="button"
              disabled={!file || upload.isPending}
              onClick={() => file && upload.mutate(file)}
            >
              {upload.isPending ? t('logo.uploading') : t('logo.upload')}
            </Button>
          </div>
        </div>

        {logoError && <p className="text-sm text-destructive">{logoError}</p>}
        {logoSaved && <p className="text-sm text-emerald-600">{t('logo.saved')}</p>}
      </CardContent>
    </Card>
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
