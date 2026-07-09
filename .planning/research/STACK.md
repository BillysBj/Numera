# Stack Research

**Domain:** Multi-tenant SaaS accounting & e-invoicing platform for the German market (Lexware Office / sevDesk competitor), delivered as a responsive PWA
**Researched:** 2026-07-09
**Confidence:** HIGH on backend/e-invoicing ecosystem and hosting; MEDIUM on frontend specifics and bank-API pricing (pricing not public)

---

## TL;DR — The Prescriptive Stack

| Layer | Choice | Confidence |
|-------|--------|------------|
| Backend | **.NET 10 (LTS) + ASP.NET Core** | HIGH |
| E-invoicing generation | **ZUGFeRD-csharp** (v18.x) | HIGH |
| E-invoicing validation | **KoSIT Validator v1.6.0** (Java sidecar) + Schematron; Mustang as reference | HIGH |
| PDF / PDF-A-3 | **QuestPDF** (native ZUGFeRD/PDF-A-3 support) | HIGH |
| Money | **.NET `decimal`** everywhere; consider **NodaMoney** for the money value-object | HIGH |
| Database | **PostgreSQL 18** with **Row-Level Security** (shared DB, `tenant_id` discriminator) | HIGH |
| ORM | **EF Core 10** + Npgsql, global query filters + RLS defence-in-depth | HIGH |
| Frontend | **React 19 + Vite + vite-plugin-pwa** | MEDIUM-HIGH |
| UI | **shadcn/ui (Radix + Tailwind v4)** + **TanStack Table/Query** | MEDIUM |
| i18n | **i18next / react-i18next** | HIGH |
| Auth | **Keycloak** (self-hosted, realms/organizations for tenancy) | HIGH |
| Bank API | **finAPI** (BaFin-licensed, FinTS+XS2A, German-native) | MEDIUM (pricing not public) |
| Hosting | **Hetzner Cloud (Nuremberg/Falkenstein)**, self-managed Postgres or UbiCloud | HIGH |

The single most important architectural driver is e-invoicing library maturity. The Java and .NET ecosystems have mature, actively-maintained EN 16931 / XRechnung / ZUGFeRD libraries; Node.js does not. This pushes the backend to **.NET** (or Java). .NET is recommended over Java for a solo/small-team greenfield: better DX, first-class Postgres/EF Core, QuestPDF for PDF/A-3, and the actively-maintained `ZUGFeRD-csharp` covering both creation AND parsing. The KoSIT validator (the only officially-sanctioned XRechnung validator) is Java and runs as a language-agnostic sidecar regardless of backend choice.

---

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| **.NET / ASP.NET Core** | **10.0 (LTS)** | Backend runtime + web API | LTS released Nov 2025, supported until **Nov 2028** (odd releases like .NET 9 are STS, EOL Nov 2026 — do NOT start greenfield on 9). Best-in-class e-invoicing library ecosystem, strong Postgres support, `decimal` primitive for money, high performance. |
| **C#** | **14** | Language | Ships with .NET 10; records, pattern matching, and nullable reference types suit a money/domain-heavy codebase. |
| **PostgreSQL** | **18** (18.3+) | Primary database | Released Sept 2025, supported ~5 yrs. Native **Row-Level Security** is the linchpin of multi-tenant isolation. `numeric`/`decimal` for exact money. Free, EU-hostable, no vendor lock-in. |
| **EF Core** | **10** | ORM / migrations | Ships with .NET 10 (LTS). Global query filters for tenant scoping, migrations for GoBD-relevant schema evolution. Pair with **Npgsql** provider. |
| **React** | **19** | Frontend framework | Largest ecosystem for data-heavy dashboards; first-class PWA support; huge hiring pool. Stable as of Dec 2024. |
| **Vite** | **6/7** | Frontend build tool | Fast iteration, framework-agnostic, ideal for an authenticated app-shell PWA (no SEO requirement → Next.js SSR is unnecessary overhead). |
| **Keycloak** | **26.x** | Identity & access (IAM) | Self-hosted → full DSGVO data sovereignty (no US CLOUD Act exposure). Realms/Organizations model multi-tenancy natively; OIDC integrates cleanly with ASP.NET Core. Free, no per-MAU cost. Future **Steuerberater** cross-tenant role fits the realm/role model. |

### E-Invoicing Libraries (the critical, domain-specific layer)

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| **ZUGFeRD-csharp** (`ZUGFeRD-csharp` NuGet) | **18.0.0** (Mar 2026, final major) | Create AND parse/read ZUGFeRD 2.3 (BASIC/COMFORT/EXTENDED/XRECHNUNG profiles), Factur-X, XRechnung CII. `InvoiceDescriptor.Load()` auto-detects version. | Primary library for generating outbound invoices and parsing inbound structured invoices. Covers both UBL/CII data models needed for EN 16931. |
| **KoSIT Validator** (`itplr-kosit/validator`) | **v1.6.0** + `validator-configuration-xrechnung` (config 2026-01-31, XRechnung 3.0.x) | Official XRechnung/EN 16931 validation (Schematron + XSD). The ONLY validator whose results are authoritative for German compliance. | Run as a **Java sidecar service** (Docker container) that your .NET backend calls over HTTP/stdin for every inbound AND outbound invoice. Language-agnostic — this is why backend language doesn't dictate validation. |
| **Mustang** (`org.mustangproject`) | 2.24.x (Java) | Reference implementation, alternate validator, arithmetic/syntactic checks. | Optional cross-check / fallback validator; useful as a second opinion and for ZUGFeRD 2.5 forward-compat. Not required if KoSIT validator sidecar is in place. |
| **QuestPDF** | latest (10.x) | PDF invoice rendering + **PDF/A-3 with embedded ZUGFeRD XML** | Generate the human-readable invoice PDF and embed the CII XML per ZUGFeRD spec. Has documented, first-class ZUGFeRD example. Free under Community License (<$1M revenue), then $999 perpetual. |

**Format targets (verify at build time — specs move):**
- **XRechnung 3.0.2** (mandatory since 2025-01-01; **4.0 expected end of 2026** — plan for it)
- **ZUGFeRD 2.3.x / Factur-X 1.0.7** (harmonized DE/FR, EN 16931-conformant)
- Both **UBL** and **CII** syntaxes must be supported for XRechnung; ZUGFeRD is CII-only.

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| **Npgsql** | 9.x (matches EF Core 10) | PostgreSQL ADO.NET + EF Core provider | Always (DB access). Supports `SET LOCAL app.current_tenant` for RLS session context. |
| **NodaMoney** | 2.7.0 | Money value-object (currency + rounding) | Optional but recommended: wraps `decimal` with currency awareness + automatic minor-unit rounding. Use for the domain money type; keep raw `decimal` for intermediate calc precision. |
| **FluentValidation** | 11.x/12.x | Request/domain validation | Invoice/customer/tax-rule validation with readable rules. |
| **MediatR** (or built-in) | 12.x | CQRS/application layer | Optional; helps keep invoicing/booking use-cases isolated. Note: MediatR moved to a commercial model — evaluate license or use a lightweight hand-rolled dispatcher. |
| **Hangfire** or **Quartz.NET** | latest | Background jobs | Dunning runs (Mahnwesen), scheduled bank sync, USt deadline reminders, email dispatch. Hangfire persists to Postgres. |
| **Serilog** | latest | Structured logging | GoBD-relevant audit trails, tenant-scoped logs. |
| **react-i18next / i18next** | 15.x / 25.x | Frontend i18n (DE + EN from day 1) | Namespaced translations, ICU pluralization, lazy-loaded locale bundles. Industry standard. |
| **TanStack Query** | 5.x | Server-state / data fetching | Caching, background refetch, offline-friendly — pairs well with PWA. |
| **TanStack Table** | 8.x | Headless data tables | Invoices, open items, transactions grids. Pairs with shadcn/ui table primitives. |
| **vite-plugin-pwa** | 0.21.x+ | Service worker, manifest, offline, installability | Workbox-powered; handles install prompt + offline app-shell + update flow. |
| **react-hook-form** + **zod** | 7.x / 3.x | Forms + schema validation | Invoice/quote editors, complex line-item forms. |
| **Playwright** (or getUserMedia + a JS decoder) | — | Camera / receipt scanner | PWA camera access via `getUserMedia`; server-side OCR later (Azure Document Intelligence EU region, or self-hosted). |

### Bank API Provider (multibanking)

| Provider | Status | Verdict |
|----------|--------|---------|
| **finAPI** | BaFin-licensed AISP/PISP, Munich-based, ~3,000 banks in 9 EU countries, **FinTS + XS2A**, 30-day free trial (no public free tier) | **RECOMMENDED.** German-native, deepest German bank coverage, FinTS support matters because many German banks are better reached via FinTS than PSD2 XS2A. BaFin license means you don't need your own PSD2 license. Acquired by Fabrick/Sella Group 2025 (stable ownership). |
| **Klarna Kosma** | 15,000+ banks / 24 countries, single XS2A API. Absorbed Nordigen/GoCardless BAD business. | Viable alternative; broader EU coverage but less German-FinTS depth. Consider if you expand beyond DACH early. |
| **GoCardless Bank Account Data (Nordigen)** | **STOPPED accepting new accounts July 2025** | **DO NOT choose** — closed to new signups. This was the popular free-tier option; it's gone. |
| **Tink** (Visa) | 6,000+ banks; sandbox free, production pricing via sales | Alternative; strong EU coverage, but German bank/FinTS depth weaker than finAPI. |

**Recommendation:** Start with **finAPI** for the German market. Abstract the bank layer behind your own interface so a later switch to Kosma/Tink is contained. Get concrete pricing quotes early — none publish production pricing, and cost is the deciding factor between them.

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| **Docker / Docker Compose** | Local dev + KoSIT validator sidecar packaging | The KoSIT validator ships as a Java bundle — containerize it once and call over HTTP. |
| **xUnit / NUnit + Testcontainers** | Backend testing | Testcontainers spins up real Postgres for RLS/tenant-isolation tests (critical — RLS bugs leak tenant data). |
| **Vitest + Playwright** | Frontend unit + E2E | E2E for install/offline PWA flows and camera capture. |
| **pgAdmin / psql** | DB admin | RLS policy verification. |
| **GitHub Actions / Gitea Actions** | CI/CD | Run KoSIT validation of sample invoices in CI as a regression gate. |

---

## Multi-Tenancy Pattern (prescriptive)

**Recommendation: Shared database, shared schema, `tenant_id` discriminator column, enforced by PostgreSQL Row-Level Security.**

Why not schema-per-tenant or DB-per-tenant:
- Freelancers + small businesses = potentially thousands of small tenants. Schema/DB-per-tenant explodes migration and connection-pool complexity at that count.
- RLS gives strong DB-enforced isolation without the operational cost.
- Easy cross-tenant admin/reporting and future Steuerberater multi-mandant access.

Implementation rules (from 2025 RLS best-practice consensus, MEDIUM-HIGH confidence):
1. Every tenant-scoped table gets `tenant_id uuid NOT NULL` + an RLS policy comparing to a session variable.
2. Set context per request with **`SET LOCAL app.current_tenant = '<uuid>'`** — **never plain `SET`** (leaks across pooled connections).
3. Make `tenant_id` the **leading column** of every primary access index (missing composite index = ~100× slower RLS; with it, ~0.3ms at 50M rows).
4. **Do NOT grant `BYPASSRLS`** to app/admin roles — use explicit admin policies; reserve `BYPASSRLS` for migration roles only.
5. Use EF Core **global query filters** as defence-in-depth (app-layer) on top of RLS (DB-layer) — belt and braces.
6. Views: `security_invoker = true`.
7. Test isolation with Testcontainers — a leaked-tenant bug here is a DSGVO breach.

---

## Money Handling (non-negotiable: no float)

- **Storage:** PostgreSQL `numeric(19,4)` (or `numeric(19,6)` for unit prices/tax intermediate). Never `float`/`double`/`real`.
- **In .NET:** `System.Decimal` (28-digit precision) for all monetary values and intermediate calculations. Never `double`/`float`.
- **Domain type:** Wrap in **NodaMoney `Money`** (currency + automatic minor-unit rounding) for the public domain API; keep raw `decimal` for multi-step intermediate calculations, round only at the boundary (invoice line/total per EN 16931 rounding rules).
- **EN 16931 rounding:** VAT is calculated and rounded per VAT category (BR-CO-* rules); implement rounding explicitly and validate with the KoSIT validator — arithmetic mismatches are the #1 validation failure.

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| **.NET 10** | **Java (Spring Boot) + Mustang** | If the team is Java-native. Mustang is the most mature single e-invoicing library (create + validate + KoSIT bundled). Equally valid; chosen against only for solo/small-team DX. |
| **.NET 10** | **Node.js/TypeScript** | **Not recommended for core invoicing.** `node-zugferd` and `@e-invoice-eu/*` exist but are young, single-maintainer, and lack the profile coverage + official validation lineage of .NET/Java. Fine for the frontend, not the invoicing engine. |
| **Vite + React** | **Next.js** | Choose Next.js only if you later need SEO'd marketing/portal pages server-rendered. The core app is authenticated (no SEO) → Vite's simpler PWA path wins. You can run a separate Next.js/Astro marketing site. |
| **React** | **Angular** | Angular's batteries-included structure suits large enterprise teams; heavier for a small team. Vue is also fine. React chosen for ecosystem depth (TanStack, shadcn) and hiring. |
| **shadcn/ui** | **MUI (Material UI) + MUI X DataGrid** | Choose MUI if you want batteries-included enterprise DataGrid (sorting/filtering/virtualization/Excel export out of the box) and accept the Pro/Premium license cost + Material look. shadcn = full code ownership, no runtime dep, but you assemble the grid from TanStack Table. |
| **Keycloak** | **ASP.NET Core Identity (in-app)** | Simpler for v1 if you don't want to run Keycloak infra. But you'll rebuild SSO, MFA, org/realm tenancy, and Steuerberater cross-tenant access yourself. Keycloak pays off as tenancy/roles grow. |
| **Keycloak** | **Auth0 / Clerk** | Faster setup, but per-MAU pricing escalates fast and data sits on US-owned infra (DSGVO/CLOUD Act concern). Avoid for a German-market data-sovereignty product. |
| **finAPI** | **Klarna Kosma / Tink** | Broader pan-EU coverage; choose if expanding beyond DACH early or if their quote is materially cheaper. |
| **QuestPDF** | **iText** | iText has the most mature PDF/A conformance but is **AGPL or paid commercial** — expensive and license-viral. QuestPDF's Community License is free under $1M revenue and has native ZUGFeRD support. Use iText only if QuestPDF's PDF/A-3 conformance proves insufficient in KoSIT testing. |
| **Hetzner** | **IONOS / OVH / Scaleway** | Other EU-sovereign options. IONOS if you want German enterprise SLAs; Hetzner wins on price/performance. |

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| **Node.js as the invoicing backend** | E-invoicing libs (`node-zugferd`, `@e-invoice-eu`) are immature, single-maintainer, incomplete profile coverage, no official validation lineage. Getting EN 16931 arithmetic + Schematron right is where products fail. | .NET (ZUGFeRD-csharp) or Java (Mustang) |
| **.NET 9** (or any STS) for greenfield | STS release, **EOL Nov 2026** — you'd be on borrowed time before v1 ships. | **.NET 10 LTS** (EOL Nov 2028) |
| **GoCardless Bank Account Data / Nordigen** | **Closed to new accounts since July 2025.** | finAPI / Klarna Kosma |
| **`float`/`double`/`real` for money** anywhere | Binary floating point cannot represent cents exactly → rounding errors → failed EN 16931 arithmetic validation + wrong tax. | `decimal` (.NET) / `numeric` (Postgres) |
| **AWS/Azure/GCP (US hyperscalers) as primary data home** | US CLOUD Act exposure conflicts with DSGVO data-sovereignty positioning vs Lexware/sevDesk. | Hetzner / IONOS / OVH (EU-owned) |
| **iText for PDF (unless licensed)** | AGPL license is viral (forces open-sourcing your SaaS) or requires expensive commercial license. | QuestPDF (Community/Pro) |
| **Rolling your own XRechnung Schematron validation** | The KoSIT rules are complex, versioned, and legally authoritative; hand-rolling guarantees drift and rejected invoices. | KoSIT Validator sidecar |
| **`SET` (not `SET LOCAL`) for RLS tenant context** | Persists across pooled connections → cross-tenant data leak (DSGVO breach). | `SET LOCAL app.current_tenant` per transaction |
| **Schema-per-tenant / DB-per-tenant** for this scale | Thousands of small tenants → unmanageable migrations + connection sprawl. | Shared schema + RLS |

---

## Stack Patterns by Variant

**If the team is Java-native rather than .NET-native:**
- Use **Spring Boot 3.x + Mustang** instead of .NET + ZUGFeRD-csharp.
- Mustang bundles creation, validation, and the KoSIT validator in one library — slightly less integration glue.
- Everything else (Postgres RLS, React PWA, Keycloak, finAPI, Hetzner) is unchanged.

**If you need a public marketing site / SEO landing pages:**
- Keep the app as Vite+React PWA; add a **separate Astro or Next.js** static/SSR site for marketing. Don't SSR the whole app for a few public pages.

**If enterprise DataGrid features (Excel export, tree/grouping) are a v1 requirement:**
- Prefer **MUI X DataGrid (Pro)** over shadcn+TanStack Table to avoid building grid features by hand.

**If you want managed Postgres despite Hetzner not offering it:**
- Use **UbiCloud** (hosts on Hetzner Nuremberg, Dutch B.V. → non-US jurisdiction, ~€50/mo, one-click provisioning, DPA available) — keeps EU sovereignty while removing DB-ops burden.

---

## Version Compatibility

| Package A | Compatible With | Notes |
|-----------|-----------------|-------|
| .NET 10 | EF Core 10, Npgsql 9.x | All ship/align with the .NET 10 wave. |
| EF Core 10 | PostgreSQL 18 (via Npgsql) | Npgsql supports RLS session vars; use interceptors to `SET LOCAL` per request. |
| ZUGFeRD-csharp 18.x | ZUGFeRD 2.3, Factur-X 1.0.7, XRechnung 3.0.x (CII) | v18 is the **final major** — pin it, monitor for XRechnung 4.0 support path. |
| KoSIT Validator 1.6.0 | `validator-configuration-xrechnung` 2026-01-31 (XRechnung 3.0.x) | Config and validator versioned separately — update config when XRechnung 4.0 lands (end 2026). |
| QuestPDF 10.x | .NET 10, PDF/A-3 | Verify PDF/A-3b conformance against KoSIT before relying on it in prod. |
| vite-plugin-pwa | Vite 3.1+ (use 6/7), Node 16+ (use 20/22 LTS) | Workbox under the hood. |
| React 19 | TanStack Query 5, TanStack Table 8, react-i18next 15 | All support React 19. |

---

## Open Questions for Phase-Level Research

1. **XRechnung 4.0** (expected end of 2026) — confirm ZUGFeRD-csharp / KoSIT config upgrade path before it becomes mandatory.
2. **finAPI production pricing** — get a concrete quote; it's the deciding factor vs Kosma/Tink and none publish it.
3. **QuestPDF PDF/A-3b conformance** — validate generated PDFs against KoSIT in a spike; if it fails EXTENDED-profile embedding, fall back to iText (licensed) or a Java PDF step.
4. **DATEV export format** — later phase; DATEV has its own CSV/EXTF formats requiring dedicated research.
5. **ERiC (USt-Voranmeldung submission)** — ELSTER's ERiC is a native C/C++ library with a .NET wrapper; native-interop complexity — research when that phase arrives.
6. **GoBD-compliant archive** — immutability/audit requirements may push toward WORM storage or append-only + hash-chaining; research at the archive phase.

---

## Sources

- Microsoft .NET support policy (dotnet.microsoft.com/platform/support/policy) — .NET 10 LTS to Nov 2028, .NET 9 STS EOL Nov 2026 — HIGH
- NuGet: ZUGFeRD-csharp v18.0.0 (nuget.org/packages/ZUGFeRD-csharp) — profiles, Load()/create, final major — HIGH
- itplr-kosit/validator-configuration-xrechnung (github.com) — KoSIT Validator v1.6.0, config 2026-01-31, XRechnung 3.0.x — HIGH
- ZUGFeRD/mustangproject (github.com) — Java create+validate lib, 2.24.x — HIGH
- QuestPDF ZUGFeRD example + license/pricing (questpdf.com/examples/zugferd.html, /pricing.html) — native PDF/A-3 ZUGFeRD, Community <$1M / $999 Pro — HIGH
- PostgreSQL 18 release (postgresql.org/about/news/postgresql-18-released) — Sept 2025, RLS, ~5yr support — HIGH
- RLS multi-tenancy best practices (ricofritzsche.me, AWS Prescriptive Guidance) — SET LOCAL, composite index, no BYPASSRLS — MEDIUM-HIGH
- NodaMoney 2.7.0 (nuget.org/packages/NodaMoney, github) — decimal-backed money + rounding — HIGH
- finAPI (finapi.io) — BaFin AISP/PISP, ~3000 banks, FinTS+XS2A, Fabrick/Sella acquisition 2025 — MEDIUM (pricing not public)
- GoCardless / Nordigen closure July 2025 (openbankingtracker.com, gocardless docs) — closed to new accounts — MEDIUM-HIGH
- Klarna Kosma (openbankingtracker.com/embedded-finance/klarna-kosma) — 15k banks / 24 countries — MEDIUM
- Hetzner vs IONOS vs AWS Frankfurt GDPR hosting (hetzner.com, danubedata.ro, medium/UbiCloud review) — EU sovereignty, no managed DB on Hetzner, UbiCloud option — HIGH
- Keycloak multi-tenancy (skycloak.io, dzone, keycloak docs) — realms/organizations, self-hosted, OIDC + ASP.NET Core — HIGH
- shadcn/ui vs MUI (adminlte.io, shadcndeck.com) — TanStack Table pairing, MUI X DataGrid licensing — MEDIUM
- vite-plugin-pwa (vite-pwa-org.netlify.app, github) — Workbox, zero-config PWA — HIGH
- XRechnung 3.0.2 / 4.0 timeline (theinvoicinghub.com, e-rechnung-bund.de) — 3.0.2 since 2025-01-01, 4.0 end 2026 — MEDIUM-HIGH

---
*Stack research for: German multi-tenant SaaS accounting & e-invoicing platform (Numera)*
*Researched: 2026-07-09*
