import { z } from 'zod'
import {
  TaxCategory,
  type UpdateCompanyProfileRequest,
} from '@/lib/api/companyProfile'

// Client-side zod schema MIRRORING the 03-01 server FluentValidation rules
// (CompanyProfileValidators.cs). The server stays authoritative — this is UX only: it
// blocks obviously-invalid input before the round-trip and localises messages.
//
// The §14 core: a legal name, a billing address (street/postalCode/city), and EXACTLY ONE
// of USt-IdNr (vatId) / Steuernummer (taxNumber) — a compliant invoice cannot be finalized
// without a tax identity, and having both is ambiguous.

/** Minimal translator signature (i18next `t` is assignable to this). */
export type Translate = (key: string) => string

// Optional numeric field: an empty input becomes `undefined`; otherwise a number.
const optionalNumber = (extra: (s: z.ZodNumber) => z.ZodNumber = (s) => s) =>
  z.preprocess(
    (v) => (v === '' || v === null || v === undefined ? undefined : Number(v)),
    extra(z.number()).optional(),
  )

// A trimmed optional string: blank → undefined (so it PUTs as null, not "").
const optionalString = z
  .string()
  .trim()
  .optional()
  .or(z.literal(''))
  .transform((v) => (v ? v : undefined))

export function makeCompanyProfileSchema(t: Translate) {
  return z
    .object({
      legalName: z
        .string()
        .trim()
        .min(1, t('errors.legalNameRequired'))
        .max(200, t('errors.legalNameMax')),
      address: z.object({
        street: z.string().trim().min(1, t('errors.streetRequired')),
        line2: optionalString,
        postalCode: z
          .string()
          .trim()
          .min(1, t('errors.postalCodeRequired')),
        city: z.string().trim().min(1, t('errors.cityRequired')),
        countryCode: z
          .string()
          .trim()
          .length(2, t('errors.countryCode'))
          .default('DE'),
        poBox: optionalString,
      }),
      vatId: optionalString,
      taxNumber: optionalString,
      isKleinunternehmer: z.boolean().default(false),
      defaultPaymentTermsNetDays: optionalNumber((s) =>
        s
          .int()
          .min(0, t('errors.paymentTermsRange'))
          .max(365, t('errors.paymentTermsRange')),
      ),
      defaultTaxCategory: z.number().int().optional(),
      iban: optionalString,
      bic: optionalString,
      bankName: optionalString,
      registerCourt: optionalString,
      registerNumber: optionalString,
      managingDirector: optionalString,
      contactEmail: optionalString,
      contactPhone: optionalString,
    })
    // §14 requires EXACTLY ONE tax identity — mirrors HaveExactlyOneTaxId (hasVat ^ hasTax).
    .refine((v) => !!v.vatId !== !!v.taxNumber, {
      message: t('errors.exactlyOneTaxId'),
      path: ['vatId'],
    })
}

// A default instance (identity translator) purely for static type inference.
export const companyProfileSchema = makeCompanyProfileSchema((k) => k)
export type CompanyProfileFormValues = z.infer<typeof companyProfileSchema>

/** Empty defaults for the create/first-use form (DE country, §19 off). */
export function emptyCompanyProfileForm(): CompanyProfileFormValues {
  return {
    legalName: '',
    address: {
      street: '',
      line2: undefined,
      postalCode: '',
      city: '',
      countryCode: 'DE',
      poBox: undefined,
    },
    vatId: undefined,
    taxNumber: undefined,
    isKleinunternehmer: false,
    defaultPaymentTermsNetDays: undefined,
    defaultTaxCategory: TaxCategory.S,
    iban: undefined,
    bic: undefined,
    bankName: undefined,
    registerCourt: undefined,
    registerNumber: undefined,
    managingDirector: undefined,
    contactEmail: undefined,
    contactPhone: undefined,
  }
}

/** Map validated form values to the API upsert DTO (drops empty strings → null). */
export function toUpdateCompanyProfileRequest(
  v: CompanyProfileFormValues,
): UpdateCompanyProfileRequest {
  const nn = (s?: string | null) => (s && s.trim() ? s.trim() : null)
  return {
    legalName: v.legalName.trim(),
    address: {
      street: v.address.street.trim(),
      line2: nn(v.address.line2),
      postalCode: v.address.postalCode.trim(),
      city: v.address.city.trim(),
      countryCode: (v.address.countryCode ?? 'DE').toUpperCase(),
      poBox: nn(v.address.poBox),
    },
    vatId: nn(v.vatId),
    taxNumber: nn(v.taxNumber),
    isKleinunternehmer: v.isKleinunternehmer,
    defaultPaymentTermsNetDays: v.defaultPaymentTermsNetDays ?? null,
    defaultTaxCategory: (v.defaultTaxCategory ?? null) as TaxCategory | null,
    iban: nn(v.iban),
    bic: nn(v.bic),
    bankName: nn(v.bankName),
    registerCourt: nn(v.registerCourt),
    registerNumber: nn(v.registerNumber),
    managingDirector: nn(v.managingDirector),
    contactEmail: nn(v.contactEmail),
    contactPhone: nn(v.contactPhone),
  }
}
