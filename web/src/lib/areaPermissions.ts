import type { Me } from './api'

export const AREA_KEYS = [
  'Documents', 'OpenItems', 'Dunning', 'Recurring', 'Inbound',
  'Banking', 'Partners', 'Catalog', 'Reports',
] as const
export type Area = typeof AREA_KEYS[number]

const areaRoutes: [string, Area][] = [
  ['/documents', 'Documents'], ['/open-items', 'OpenItems'],
  ['/settings/dunning', 'Dunning'], ['/recurring', 'Recurring'],
  ['/inbound', 'Inbound'], ['/belege', 'Inbound'], ['/banking', 'Banking'],
  ['/partners', 'Partners'], ['/catalog', 'Catalog'], ['/reports', 'Reports'],
]
const ownerRoutes = ['/settings/email', '/settings/ledger', '/team', '/reports/datev']
const matches = (path: string, prefix: string) => path === prefix || path.startsWith(prefix + '/')

/** Cosmetic navigation/deep-link guard; the API remains authoritative. */
export function canAccessRoute(me: Pick<Me, 'role' | 'allowedAreas'> | undefined, pathname: string): boolean {
  const path = pathname.toLowerCase().replace(/\/+$/, '') || '/'
  if (path === '/settings' || ownerRoutes.some(prefix => matches(path, prefix))) {
    return me?.role === 'Owner'
  }
  const area = areaRoutes.find(([prefix]) => matches(path, prefix))?.[1]
  if (!area) return true
  if (!me) return false
  return me.role !== 'Employee' || me.allowedAreas == null || me.allowedAreas.includes(area)
}
