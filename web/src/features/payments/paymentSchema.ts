import { z } from 'zod'
import { PaymentMethod } from '@/lib/api/payments'

export type Translate = (key: string) => string

const PAYMENT_METHODS = Object.values(PaymentMethod)

export function makePaymentSchema(openAmount: number, t: Translate) {
  return z
    .object({
      amount: z.preprocess(
        (value) => (value === '' ? Number.NaN : Number(value)),
        z.number().positive(t('validation.amountPositive')),
      ),
      valueDate: z.string().min(1, t('validation.valueDateRequired')),
      method: z
        .number()
        .refine(
          (value): value is PaymentMethod =>
            PAYMENT_METHODS.includes(value as PaymentMethod),
          t('validation.methodInvalid'),
        ),
      reference: z
        .string()
        .trim()
        .optional()
        .transform((value) => value || undefined),
    })
    .refine((value) => value.amount <= openAmount, {
      message: t('validation.amountMax'),
      path: ['amount'],
    })
}

export const paymentSchema = makePaymentSchema(
  Number.MAX_SAFE_INTEGER,
  (key) => key,
)
export type PaymentFormValues = z.infer<typeof paymentSchema>
