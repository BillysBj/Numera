# Plan 08-02 Summary

Implemented owner-only team management across the API and React frontend.

## Backend

- Added `InvitationService` with the existing Keycloak admin-token pattern.
- Invitations find or create a user by email, add the user directly to the current
  Keycloak organization, and insert or update the tenant-scoped membership role.
- Added audited role-change and removal operations.
- Prevented demotion or removal of the last Owner with a typed service outcome.
- Added `/api/team` list, invite, role-change, and remove endpoints.
- Applied the `RequireOwner` policy to the endpoint group and enforced
  `Capability.MultiUser` on every mutation.
- Registered the service and endpoint group in `Program.cs`.

## Frontend

- Added typed team query/mutation hooks and mirrored C# role ordinals.
- Added `/team` with a member table, invite form, role changes, removal actions,
  inline status/errors, and client-side last-owner controls.
- Added the shared `UpgradeHint` gate when `MultiUser` is unavailable.
- Added German-authoritative and English translations, namespace registration,
  route, and navigation.

## Tests and verification

- Added five PostgreSQL 18 integration tests covering invitation persistence,
  role updates, removal, the last-owner guard, and membership RLS.
- `dotnet build Numera.sln -c Debug`: passed, 0 warnings, 0 errors.
- `dotnet test tests/Numera.IntegrationTests --filter TeamManagement -c Debug`:
  passed, 5/5 tests.
- `dotnet test tests/Numera.IntegrationTests -c Debug`: passed, 129/129 tests.
- `npm --prefix web run lint`: passed.
- `npm --prefix web run build`: passed.
- `npm --prefix web run test`: passed, 56/56 tests across 7 files.
- `git diff --check`: passed.

No migration was added. Invitations use the locked synchronous direct-add flow;
there is no email acceptance or membership synchronization workflow.
