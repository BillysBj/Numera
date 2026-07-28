# 07-05 Summary — Foreign-Currency VAT Presentation

## Delivered

- Extended the frozen invoice presentation model and snapshot reader with exchange rate,
  exchange-rate date, and EUR VAT total from 07-04.
- Added the foreign-currency PDF statement: `USt in EUR` / `VAT in EUR`, followed by the
  frozen rate and reference date. EUR invoices retain their existing presentation.
- Set BT-6 (`TaxCurrency`) to EUR for non-EUR documents with a frozen `TotalTaxEur`;
  BT-5 and BT-110 remain in the document currency.
- Added BT-111 from the frozen `TotalTaxEur` to both serializations produced from the
  same descriptor:
  - UBL receives a second invoice-level `cac:TaxTotal`, directly after the primary
    tax total and before `cac:LegalMonetaryTotal`. It contains only
    `cbc:TaxAmount currencyID="EUR"`.
  - CII receives a second `ram:TaxTotalAmount currencyID="EUR"` directly after the
    document-currency tax total in the header settlement monetary summation.
- Added pure UBL/CII assertions pinning BT-5, BT-6, BT-110, and BT-111, plus a live
  KoSIT USD scenario for both syntaxes.

## ZUGFeRD-csharp 18 Limitation

ZUGFeRD-csharp 18 supports BT-6 through `InvoiceDescriptor.TaxCurrency`, but exposes no
descriptor property for BT-111. `XRechnungGenerator` therefore loads the generated XML
with `System.Xml.Linq`, injects the namespace-aware accounting-currency tax-total node
in schema order, and reserializes it. The post-processing is conditional on a non-EUR
document currency and a frozen `TotalTaxEur`, so EUR invoices emit neither BT-6 nor
BT-111.

## Verification

- `dotnet build Numera.sln -c Debug`
  - Passed: 0 warnings, 0 errors.
- `dotnet test tests/Numera.Platform.Tests --no-build -c Debug`
  - Passed: 99, failed: 0, skipped: 0.
- `dotnet test tests/Numera.IntegrationTests --no-build -c Debug`
  - Passed: 115, failed: 0, skipped: 0.
- Focused live KoSIT foreign-currency golden:
  - `currency/ubl`: Accepted, 0 error-severity findings.
  - `currency/cii`: Accepted, 0 error-severity findings.
  - Validator sidecar: `easybill/kosit-validator-xrechnung_3.0.2:v0.2.7` on host port
    8081.

## Notes

- No migration or frontend change was made.
- No git write command was run; all work remains uncommitted.
