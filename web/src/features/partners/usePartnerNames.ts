import { useCallback, useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { listPartners } from '@/lib/api/partners'

/**
 * Resolves partner ids to display names for list tables (Offene Posten,
 * Serienrechnungen, …) whose rows carry only a `partnerId`. The partner list is
 * fetched once and cached by react-query; for the intended small business the
 * partner set fits a single page, so one paged read is sufficient.
 */
export function usePartnerNames() {
  const query = useQuery({
    queryKey: ['partner-names'],
    queryFn: () => listPartners({ page: 1, pageSize: 100 }),
    staleTime: 5 * 60 * 1000,
  })

  const byId = useMemo(() => {
    const map = new Map<string, string>()
    for (const p of query.data?.items ?? []) {
      map.set(p.id, p.name)
    }
    return map
  }, [query.data])

  /** The partner's display name, or null when the id is unknown/absent. */
  const nameFor = useCallback(
    (id?: string | null): string | null => (id ? byId.get(id) : undefined) ?? null,
    [byId],
  )

  return { nameFor, isLoading: query.isLoading }
}
