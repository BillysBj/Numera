---
phase: 13-banking-zahlungsabgleich
plan: 05
subsystem: banking
tags: [finapi, oauth2, httpclient, data-protection, hangfire, psd2]

requires:
  - phase: 13-banking-zahlungsabgleich
    provides: Banking provider port, connection credential columns, account and transaction drafts
provides:
  - Config-gated finAPI live-sandbox provider behind IBankConnectionProvider
  - Thin mocked-transport-tested AIS client for auth, users, Web Form 2.0, accounts, transactions, and consent
  - Data Protection encryption seam for finAPI sub-user credentials and access tokens
  - Tenant-scoped PSD2 consent health job with configurable expiry warning lead time
affects: [13-06-banking-api, bank-sync, consent-reauth]

tech-stack:
  added: []
  patterns:
    - Typed HttpClient behind a provider-neutral port
    - Stub-safe configuration gate requiring ClientId, ClientSecret, and BaseUrl
    - ASP.NET Core Data Protection with purpose finapi-credentials
    - SetTenant before resolving NumeraDbContext in background jobs

completed: 2026-08-05
---

# Phase 13 Plan 05: finAPI live-sandbox provider summary

The API now opts into a thin finAPI Access adapter only when all three required application settings are present. With the placeholder/empty configuration, `IBankConnectionProvider` remains the no-network `StubBankConnectionProvider`.

## Implemented

- Added `FinApiOptions` for client credentials, Access/Web Form base URLs, and the 14-day consent warning default.
- Added `FinApiClient` for OAuth client/user tokens, sub-user provisioning, Web Form 2.0 import/update, account listing, paged transaction download clamped to 89 days, and consent status.
- Normalized JSON money to `decimal` at the client boundary, retained provider transaction ids, and converted every non-success response to a sanitized `FinApiException` without response bodies or credentials.
- Added `IBankCredentialProtector` and the Data Protection implementation using the exact `finapi-credentials` purpose. User id, generated sub-user secret, and refreshed user access token are persisted only as protected strings. End-user online-banking credentials are never accepted or stored.
- Added `FinApiBankConnectionProvider` behind the existing port. It provisions/reuses a tenant sub-user, starts import/re-auth Web Forms, maps accounts and transactions, and maps provider consent state.
- Added the RLS-safe worker-queue `CheckBankConsentJob`. It sets the tenant before DbContext resolution, refreshes one connection, marks elapsed consent as `Expired`, and uses `Pending` plus `ConsentExpiresAt` as the existing-model warning state inside `ConsentWarnDays`.
- Replaced the API's unconditional stub registration with the config gate and registered the consent job transiently. Worker scheduling was deliberately not changed.
- Added offline tests whose only transport is a canned `HttpMessageHandler`, covering token/user flow, import redirect, accounts, two transaction pages and signed decimals/provider ids, consent/expiry, and credential-protector round-trip.

## Configuration gate

`Program.cs` always binds `FinApiOptions`. It registers `FinApiBankConnectionProvider`, typed `FinApiClient`, Data Protection, and `IBankCredentialProtector` only when `FinApi:ClientId`, `FinApi:ClientSecret`, and `FinApi:BaseUrl` are all non-empty. Otherwise it registers only `StubBankConnectionProvider` for `IBankConnectionProvider`. `CheckBankConsentJob` depends only on always-available services at construction time, so the empty-config API graph has no finAPI DI dependency.

## Files created

- `src/Numera.Api/Services/FinApi/FinApiOptions.cs`
- `src/Numera.Api/Services/FinApi/FinApiClient.cs`
- `src/Numera.Api/Services/FinApi/FinApiBankConnectionProvider.cs`
- `src/Numera.Api/Services/FinApi/IBankCredentialProtector.cs`
- `src/Numera.Api/Jobs/CheckBankConsentJob.cs`
- `tests/Numera.IntegrationTests/FinApiClientTests.cs`
- `.planning/phases/13-banking-zahlungsabgleich/13-05-SUMMARY.md`

## Files modified

- `src/Numera.Api/Program.cs`
- `src/Numera.Api/appsettings.json`

## Dependencies

- No NuGet package or project reference was added.
- ASP.NET Core Data Protection comes from the existing Web framework reference.
- The finAPI adapter is a hand-written `HttpClient`; no vendor SDK is used.

## Verification

- SDK: `10.0.301` at `C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe`.
- `dotnet build Numera.sln --configuration Release --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet build src/modules/Numera.Modules.Banking/Numera.Modules.Banking.csproj --configuration Release --no-restore`: passed with 0 warnings and 0 errors (explicit Release verification because the solution log referenced that project's cached Debug output path).
- The new integration-test source compiled as part of the solution build; integration tests were not run, per reviewer instruction.
- Empty `FinApi` values select the parameterless scoped `StubBankConnectionProvider`; the live client/protector are absent from that branch, so resolution has no missing finAPI dependency.
- `src/Numera.Worker/Program.cs` and all 13-04-owned ingest/sync files were left unchanged.
- No file was staged or committed. The pre-existing untracked `.claude/` directory was left untouched.

## Deviations and uncertainties

- The existing 13-01 `BankConnection` model has no finAPI bank-connection-id column, although update/consent calls require that vendor id. Within the strict ownership boundary, the provider resolves it from the account payload, preferring finAPI accounts already linked to the Numera connection and requiring an unambiguous result. No entity or migration was changed.
- The existing consent enum has no `Expiring` value or separate warning flag. The health job therefore uses `Pending` plus the exact future `ConsentExpiresAt` as the warning signal; expired/revoked states remain distinct.
- Exact sandbox endpoint/body variants and consent field names still need confirmation against the provisioned finAPI contract/tier. The client accepts the common response aliases used by the documented narrow surface, and all CI coverage remains network-free.
- A direct API health startup probe was inconclusive because host infrastructure startup did not become ready within the bounded probe; the compiled empty-config registration branch itself is deterministic and contains only the stub binding.

## User setup required

- Provision a finAPI sandbox application with Web Form 2.0 enabled.
- Supply `FinApi__ClientId`, `FinApi__ClientSecret`, `FinApi__BaseUrl`, and normally `FinApi__WebFormBaseUrl` outside committed configuration.
- Persist/protect the ASP.NET Core Data Protection key ring in deployment so existing encrypted bank credentials remain decryptable across restarts/instances.

---
*Phase: 13-banking-zahlungsabgleich*
*Completed: 2026-08-05*
