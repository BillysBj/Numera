import { forwardRef, useEffect, useMemo, useState, type ComponentProps } from 'react'
import { useTranslation } from 'react-i18next'
import { useFieldArray, useForm, type Path, type Resolver } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ApiError,
  getConfig,
  saveConfig,
  type DunningLevelConfigDto,
} from '@/lib/api/dunning'
import {
  makeDunningConfigSchema,
  type DunningConfigFormValues,
} from './dunningConfigSchema'
import { Form } from '@/components/ui/form'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Checkbox } from '@/components/ui/checkbox'
import { Textarea } from '@/components/ui/textarea'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'

function validationErrors(error: unknown): Record<string, string> | null {
  if (!(error instanceof ApiError) || error.status !== 400) return null
  const body = error.body as { errors?: Record<string, string[]> } | undefined
  if (!body?.errors) return null
  return Object.fromEntries(
    Object.entries(body.errors)
      .filter(([, messages]) => messages.length > 0)
      .map(([key, messages]) => [key, messages[0]]),
  )
}

export default function DunningConfigSettingsPage() {
  const { t } = useTranslation('dunning')
  const queryClient = useQueryClient()
  const [serverError, setServerError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const schema = useMemo(() => makeDunningConfigSchema(t), [t])
  const form = useForm<DunningConfigFormValues>({
    resolver: zodResolver(schema) as unknown as Resolver<DunningConfigFormValues>,
    defaultValues: { levels: [] },
  })
  const { fields } = useFieldArray({ control: form.control, name: 'levels' })
  const existing = useQuery({ queryKey: ['dunning-config'], queryFn: getConfig })

  useEffect(() => {
    if (existing.data) form.reset({ levels: existing.data })
  }, [existing.data, form])

  const mutation = useMutation({
    mutationFn: (values: DunningConfigFormValues) =>
      saveConfig(values.levels as DunningLevelConfigDto[]),
    onSuccess: async (levels) => {
      setSaved(true)
      queryClient.setQueryData(['dunning-config'], levels)
      await queryClient.invalidateQueries({ queryKey: ['dunning-config'] })
    },
  })

  const onSubmit = form.handleSubmit(async (values) => {
    setSaved(false)
    setServerError(null)
    try {
      await mutation.mutateAsync(values)
    } catch (error) {
      const errors = validationErrors(error)
      if (errors) {
        for (const message of Object.values(errors)) {
          form.setError('levels' as Path<DunningConfigFormValues>, { message })
        }
      } else {
        setServerError(t('settings.serverError'))
      }
    }
  })

  if (existing.isLoading) {
    return <main className="app-main"><p>{t('settings.loading')}</p></main>
  }
  if (existing.isError) {
    return <main className="app-main"><p className="text-destructive">{t('settings.loadError')}</p></main>
  }

  const errors = form.formState.errors
  return (
    <main className="app-main" style={{ maxWidth: '1100px' }}>
      <h1 className="text-2xl font-semibold">{t('settings.title')}</h1>
      <p className="mt-1 text-sm text-muted-foreground">{t('settings.description')}</p>

      <Form {...form}>
        <form onSubmit={onSubmit} className="mt-5 flex flex-col gap-4">
          {fields.map((field, index) => (
            <Card key={field.id}>
              <CardHeader>
                <CardTitle>
                  {t('settings.level')} {form.watch(`levels.${index}.level`)}
                </CardTitle>
              </CardHeader>
              <CardContent className="grid grid-cols-1 gap-3 md:grid-cols-3">
                <input type="hidden" {...form.register(`levels.${index}.level`)} />
                <TextField
                  label={t('settings.name')}
                  error={errors.levels?.[index]?.name?.message}
                  {...form.register(`levels.${index}.name`)}
                />
                <TextField
                  type="number"
                  step="1"
                  label={t('settings.daysAfterDue')}
                  error={errors.levels?.[index]?.daysAfterDue?.message}
                  {...form.register(`levels.${index}.daysAfterDue`)}
                />
                <TextField
                  type="number"
                  step="0.01"
                  label={t('settings.fee')}
                  error={errors.levels?.[index]?.fee?.message}
                  {...form.register(`levels.${index}.fee`)}
                />
                <label className="flex items-center gap-2 text-sm">
                  <Checkbox
                    checked={form.watch(`levels.${index}.chargeInterest`)}
                    onCheckedChange={(checked) =>
                      form.setValue(`levels.${index}.chargeInterest`, checked)
                    }
                  />
                  {t('settings.chargeInterest')}
                </label>
                <TextField
                  type="number"
                  step="0.01"
                  label={t('settings.interestRatePercent')}
                  error={errors.levels?.[index]?.interestRatePercent?.message}
                  {...form.register(`levels.${index}.interestRatePercent`)}
                />
                <div />
                <TextAreaField
                  label={t('settings.templateTextDe')}
                  error={errors.levels?.[index]?.templateTextDe?.message}
                  {...form.register(`levels.${index}.templateTextDe`)}
                />
                <TextAreaField
                  label={t('settings.templateTextEn')}
                  error={errors.levels?.[index]?.templateTextEn?.message}
                  {...form.register(`levels.${index}.templateTextEn`)}
                />
              </CardContent>
            </Card>
          ))}

          {errors.levels?.message && (
            <p className="text-sm text-destructive">{errors.levels.message}</p>
          )}
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          {saved && <p role="status" className="text-sm text-emerald-600">{t('settings.saved')}</p>}
          <Button type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? t('settings.saving') : t('settings.save')}
          </Button>
        </form>
      </Form>
    </main>
  )
}

interface TextFieldProps extends ComponentProps<'input'> {
  label: string
  error?: string
}
const TextField = forwardRef<HTMLInputElement, TextFieldProps>(
  ({ label, error, className, ...props }, ref) => (
    <label className={cn('flex flex-col gap-1.5', className)}>
      <Label>{label}</Label>
      <Input ref={ref} {...props} />
      {error && <span className="text-sm text-destructive">{error}</span>}
    </label>
  ),
)
TextField.displayName = 'TextField'

interface TextAreaFieldProps extends ComponentProps<'textarea'> {
  label: string
  error?: string
}
const TextAreaField = forwardRef<HTMLTextAreaElement, TextAreaFieldProps>(
  ({ label, error, className, ...props }, ref) => (
    <label className={cn('flex flex-col gap-1.5 md:col-span-3', className)}>
      <Label>{label}</Label>
      <Textarea ref={ref} {...props} />
      {error && <span className="text-sm text-destructive">{error}</span>}
    </label>
  ),
)
TextAreaField.displayName = 'TextAreaField'
