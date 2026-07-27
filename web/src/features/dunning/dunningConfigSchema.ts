import { z } from 'zod'

export type Translate = (key: string) => string

const numeric = z.preprocess((value) => Number(value), z.number())

export function makeDunningConfigSchema(t: Translate) {
  return z
    .object({
      levels: z
        .array(
          z.object({
            level: numeric.pipe(z.number().int()),
            name: z.string().trim().min(1, t('errors.required')),
            daysAfterDue: numeric.pipe(
              z.number().int().min(0, t('errors.days')),
            ),
            fee: numeric.pipe(z.number().min(0, t('errors.fee'))),
            chargeInterest: z.boolean(),
            interestRatePercent: numeric.pipe(
              z.number().min(0, t('errors.rate')),
            ),
            templateTextDe: z.string().trim().min(1, t('errors.required')),
            templateTextEn: z.string().trim().min(1, t('errors.required')),
          }),
        )
        .min(1, t('errors.ladder')),
    })
    .superRefine(({ levels }, ctx) => {
      if (levels.some((item, index) => item.level !== index)) {
        ctx.addIssue({
          code: 'custom',
          message: t('errors.ladder'),
          path: ['levels'],
        })
      }
      if (
        levels.some(
          (item, index) =>
            index > 0 && item.daysAfterDue < levels[index - 1].daysAfterDue,
        )
      ) {
        ctx.addIssue({
          code: 'custom',
          message: t('errors.days'),
          path: ['levels'],
        })
      }
    })
}

export const dunningConfigSchema = makeDunningConfigSchema((key) => key)
export type DunningConfigFormValues = z.infer<typeof dunningConfigSchema>
