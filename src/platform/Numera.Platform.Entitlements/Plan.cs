// The tier/plan model is the SINGLE source of truth defined by the tenant's DB
// column. That enum (TenantPlan: S/M/L/XL) already lives in Numera.Platform.Db.Entities
// from plan 01-02, and this layer must NOT duplicate it. Instead of redefining the
// four values here (which would create a second, drift-prone source of truth), we
// re-export the existing type under this namespace as `Plan` via a namespace-scoped
// alias, so entitlement code can speak of `Plan.S` while the values physically live
// on Tenant.Plan in the database.

global using Plan = Numera.Platform.Db.Entities.TenantPlan;
