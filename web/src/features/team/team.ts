import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

/** Mirrors Numera.Platform.Db.Entities.MembershipRole. */
export const MembershipRole = {
  Owner: 1,
  Employee: 2,
  TaxAdvisor: 3,
} as const

export type MembershipRoleValue =
  (typeof MembershipRole)[keyof typeof MembershipRole]

export interface TeamMember {
  userId: string
  role: MembershipRoleValue
}

export class TeamApiError extends Error {
  readonly status: number
  readonly code?: string

  constructor(status: number, code?: string) {
    super(code ?? `Request failed: ${status}`)
    this.status = status
    this.code = code
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null) as
      | { title?: string }
      | null
    throw new TeamApiError(response.status, problem?.title)
  }

  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export function useTeam(enabled = true) {
  return useQuery({
    queryKey: ['team'],
    queryFn: () => request<TeamMember[]>('/api/team'),
    enabled,
  })
}

export function useInvite() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { email: string; role: MembershipRoleValue }) =>
      request<{ userId: string }>('/api/team/invite', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['team'] }),
  })
}

export function useChangeRole() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { userId: string; role: MembershipRoleValue }) =>
      request<void>(`/api/team/${input.userId}/role`, {
        method: 'PUT',
        body: JSON.stringify({ role: input.role }),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['team'] }),
  })
}

export function useRemoveMember() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (userId: string) =>
      request<void>(`/api/team/${userId}`, { method: 'DELETE' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['team'] }),
  })
}
