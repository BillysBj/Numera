import { z } from 'zod'
import {
  PartnerLanguage,
  TaxCategory,
  type PartnerWriteRequest,
} from '@/lib/api/partners'

// Client-side zod schema MIRRORING the 02-04 server FluentValidation rules
// (PartnerValidators.cs). The server stays authoritative — this is UX only: it
// blocks obviously-invalid input before the round-trip and localises messages.

/** Minimal translator signature (i18next `t` is assignable to this). */
export type Translate = (key: string) => string

// Optional numeric field: an empty input becomes `undefined`; otherwise a number.
const optionalNumber = (extra: (s: z.ZodNumber) => z.ZodNumber = (s) => s) =>
  z.preprocess(
    (v) => (v === '' || v === null || v === undefined ? undefined : Number(v)),
    extra(z.number()).optional(),
  )

// A light EU VAT-ID plausibility check (2-letter country prefix + 2..12 alnum).
// The real ISO 7064 / VIES check lives on the server (offline-only, never gating).
const VAT_ID_RE = /^[A-Za-z]{2}[A-Za-z0-9]{2,12}$/

function addressSchema(t: Translate, required: boolean) {
  const str = (max: number) =>
    required
      ? z.string().trim().min(1, t('form.errors.required')).max(max)
      : z.string().trim().max(max).optional()
  return z.object({
    street: str(200),
    line2: z.string().trim().max(200).optional().or(z.literal('')),
    postalCode: str(20),
    city: str(120),
    countryCode: required
      ? z.string().trim().length(2, t('form.errors.countryCode'))
      : z
          .string()
          .trim()
          .length(2, t('form.errors.countryCode'))
          .optional()
          .or(z.literal('')),
    poBox: z.string().trim().max(50).optional().or(z.literal('')),
  })
}

export function makePartnerSchema(t: Translate) {
  return z
    .object({
      name: z
        .string()
        .trim()
        .min(1, t('form.errors.required'))
        .max(200, t('form.errors.nameMax')),
      legalForm: z.string().trim().max(100).optional().or(z.literal('')),
      billingAddress: addressSchema(t, true),
      hasShippingAddress: z.boolean(),
      // Shipping fields are OPTIONAL at the field level; the cross-field refine
      // below makes them required only when `hasShippingAddress` is toggled on.
      shippingAddress: addressSchema(t, false).optional(),
      vatId: z
        .string()
        .trim()
        .optional()
        .or(z.literal(''))
        .refine(
          (v) => !v || VAT_ID_RE.test(v.replace(/\s/g, '')),
          t('form.errors.vatId'),
        ),
      taxNumber: z.string().trim().max(50).optional().or(z.literal('')),
      email: z.string().trim().max(200).optional().or(z.literal('')),
      phone: z.string().trim().max(50).optional().or(z.literal('')),
      website: z.string().trim().max(200).optional().or(z.literal('')),
      paymentTermsNetDays: optionalNumber((s) => s.int().min(0).max(365)),
      skontoPercent: optionalNumber((s) =>
        s.min(0, t('form.errors.skontoRange')).max(100, t('form.errors.skontoRange')),
      ),
      skontoDays: optionalNumber((s) => s.int().min(0).max(365)),
      defaultCurrency: z
        .string()
        .trim()
        .length(3, t('form.errors.currency'))
        .default('EUR'),
      language: z.number().int(),
      defaultTaxCategory: z.number().int().nullable().optional(),
      isCustomer: z.boolean(),
      isSupplier: z.boolean(),
      customerNumber: z.string().trim().max(50).optional().or(z.literal('')),
      supplierNumber: z.string().trim().max(50).optional().or(z.literal('')),
      // Contacts entered while creating a partner; persisted via the separate
      // contacts API after the partner is saved (they are not part of the write DTO).
      contacts: z
        .array(
          z.object({
            salutation: z.string().trim().max(50).optional().or(z.literal('')),
            firstName: z.string().trim().max(120).optional().or(z.literal('')),
            lastName: z.string().trim().min(1, t('form.errors.required')).max(120),
            email: z.string().trim().max(200).optional().or(z.literal('')),
            phone: z.string().trim().max(50).optional().or(z.literal('')),
            position: z.string().trim().max(120).optional().or(z.literal('')),
            isPrimary: z.boolean(),
          }),
        )
        .default([]),
    })
    .refine((v) => v.isCustomer || v.isSupplier, {
      message: t('form.errors.roleRequired'),
      path: ['isCustomer'],
    })
    .refine(
      (v) =>
        !v.hasShippingAddress ||
        (!!v.shippingAddress &&
          !!v.shippingAddress.street &&
          !!v.shippingAddress.postalCode &&
          !!v.shippingAddress.city &&
          v.shippingAddress.countryCode?.length === 2),
      { message: t('form.errors.required'), path: ['shippingAddress', 'street'] },
    )
}

// A default instance (identity translator) purely for static type inference.
export const partnerSchema = makePartnerSchema((k) => k)
export type PartnerFormValues = z.infer<typeof partnerSchema>

/** Empty defaults for the create form (German default language, EUR). */
export function emptyPartnerForm(): PartnerFormValues {
  return {
    name: '',
    legalForm: '',
    billingAddress: {
      street: '',
      line2: '',
      postalCode: '',
      city: '',
      countryCode: 'DE',
      poBox: '',
    },
    hasShippingAddress: false,
    shippingAddress: {
      street: '',
      line2: '',
      postalCode: '',
      city: '',
      countryCode: 'DE',
      poBox: '',
    },
    vatId: '',
    taxNumber: '',
    email: '',
    phone: '',
    website: '',
    paymentTermsNetDays: undefined,
    skontoPercent: undefined,
    skontoDays: undefined,
    defaultCurrency: 'EUR',
    language: PartnerLanguage.De,
    defaultTaxCategory: null,
    isCustomer: true,
    isSupplier: false,
    customerNumber: '',
    supplierNumber: '',
    contacts: [],
  }
}

/** Map validated form values to the API write DTO (drops empty strings → null). */
export function toWriteRequest(v: PartnerFormValues): PartnerWriteRequest {
  const nn = (s?: string | null) => (s && s.trim() ? s.trim() : null)
  const addr = (a: PartnerFormValues['billingAddress']) => ({
    street: a.street ?? '',
    line2: nn(a.line2),
    postalCode: a.postalCode ?? '',
    city: a.city ?? '',
    countryCode: (a.countryCode ?? '').toUpperCase(),
    poBox: nn(a.poBox),
  })
  return {
    name: v.name.trim(),
    legalForm: nn(v.legalForm),
    billingAddress: addr(v.billingAddress),
    shippingAddress:
      v.hasShippingAddress && v.shippingAddress ? addr(v.shippingAddress) : null,
    vatId: nn(v.vatId),
    taxNumber: nn(v.taxNumber),
    email: nn(v.email),
    phone: nn(v.phone),
    website: nn(v.website),
    paymentTermsNetDays: v.paymentTermsNetDays ?? null,
    skontoPercent: v.skontoPercent ?? null,
    skontoDays: v.skontoDays ?? null,
    defaultCurrency: (v.defaultCurrency ?? 'EUR').toUpperCase(),
    language: v.language as PartnerLanguage,
    defaultTaxCategory:
      v.defaultTaxCategory == null ? null : (v.defaultTaxCategory as TaxCategory),
    isCustomer: v.isCustomer,
    isSupplier: v.isSupplier,
    customerNumber: nn(v.customerNumber),
    supplierNumber: nn(v.supplierNumber),
  }
}
