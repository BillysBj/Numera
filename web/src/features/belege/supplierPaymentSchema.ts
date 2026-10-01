import { z } from 'zod'
import { SupplierPaymentMethod } from './belegeApi'

export type Translate = (key: string) => string

const METHODS = Object.values(SupplierPaymentMethod)

// Mirror of features/payments/paymentSchema.ts, scoped to a single receipt: the
// amount must be positive and must not exceed the receipt's still-open amount.
export function makeSupplierPaymentSchema(openAmount: number, t: Translate) {
  return z
    .object({
      amount: z.preprocess(
        (value) => (value === '' ? Number.NaN : Number(value)),
        z.number().positive(t('payments.validation.amountPositive')),
      ),
      valueDate: z.string().min(1, t('payments.validation.valueDateRequired')),
      method: z
        .number()
        .refine(
          (value): value is SupplierPaymentMethod =>
            METHODS.includes(value as SupplierPaymentMethod),
          t('payments.validation.methodInvalid'),
        ),
      reference: z
        .string()
        .trim()
        .optional()
        .transform((value) => value || undefined),
    })
    .refine((value) => value.amount <= openAmount, {
      message: t('payments.validation.amountMax'),
      path: ['amount'],
    })
}

export const supplierPaymentSchema = makeSupplierPaymentSchema(
  Number.MAX_SAFE_INTEGER,
  (key) => key,
)
export type SupplierPaymentFormValues = z.infer<typeof supplierPaymentSchema>
