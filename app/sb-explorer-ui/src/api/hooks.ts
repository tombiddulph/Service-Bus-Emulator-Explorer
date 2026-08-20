import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiClient, dlqPath, messagePath, purgePath, replayDlqPath } from './client'
import type {
  CountResult,
  MessageInfo,
  MessageScope,
  MessageState,
  PagedResult,
  QueueInfo,
  SendScope,
  SubscriptionInfo,
  TopicInfo,
} from './types'

const listRefetchMs = 8000

export const useEnvironment = () =>
  useQuery({
    queryKey: ['environment'],
    queryFn: async () => (await apiClient.get<{ name: string }>('/environment')).data.name,
    staleTime: Infinity,
    // The API can still be warming up on the very first page load, so keep retrying instead of failing permanently.
    retry: 10,
    retryDelay: (attempt) => Math.min(1000 * 2 ** attempt, 10000),
    refetchOnMount: 'always',
    refetchOnWindowFocus: true,
    refetchOnReconnect: true,
  })

const scopeKey = (scope: MessageScope) =>
  scope.type === 'queue' ? `queue:${scope.name}` : `subscription:${scope.topic}:${scope.subscription}`

export const useQueues = () =>
  useQuery({
    queryKey: ['queues'],
    queryFn: async () => (await apiClient.get<QueueInfo[]>('/queues')).data,
    refetchInterval: listRefetchMs,
  })

export const useTopics = () =>
  useQuery({
    queryKey: ['topics'],
    queryFn: async () => (await apiClient.get<TopicInfo[]>('/topics')).data,
    refetchInterval: listRefetchMs,
  })

export const useSubscriptions = (topic: string, enabled = true) =>
  useQuery({
    queryKey: ['subs', topic],
    queryFn: async () => (await apiClient.get<SubscriptionInfo[]>(`/topics/${topic}/subscriptions`)).data,
    enabled: !!topic && enabled,
    refetchInterval: listRefetchMs,
  })

interface MessagesQuery {
  scope: MessageScope
  state: MessageState
  skip: number
  take: number
  enabled?: boolean
}

const isScopeValid = (scope: MessageScope) => {
  if (scope.type === 'queue') return !!scope.name
  return !!scope.topic && !!scope.subscription
}

export const useMessages = ({ scope, state, skip, take, enabled = true }: MessagesQuery) =>
  useQuery({
    queryKey: ['messages', scopeKey(scope), state, skip, take],
    queryFn: async () =>
      (
        await apiClient.get<PagedResult<MessageInfo>>(messagePath(scope), {
          params: { mode: 'peek', state, skip, take },
        })
      ).data,
    enabled: enabled && isScopeValid(scope),
    refetchInterval: listRefetchMs,
    refetchIntervalInBackground: true,
    refetchOnMount: 'always',
  })

export const useCreateQueue = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: Partial<QueueInfo> & { name: string }) => {
      await apiClient.post('/queues', payload)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['queues'] }),
  })
}

export const useCreateTopic = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: Partial<TopicInfo> & { name: string }) => {
      await apiClient.post('/topics', payload)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['topics'] }),
  })
}

export const useCreateSubscription = (topic: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: Partial<SubscriptionInfo> & { name: string }) => {
      await apiClient.post(`/topics/${topic}/subscriptions`, payload)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['subs', topic] }),
  })
}

export const useDeleteQueue = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (name: string) => {
      await apiClient.delete(`/queues/${name}`)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['queues'] }),
  })
}

export const useDeleteTopic = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (name: string) => {
      await apiClient.delete(`/topics/${name}`)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['topics'] }),
  })
}

export const useDeleteSubscription = (topic: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (subName: string) => {
      await apiClient.delete(`/topics/${topic}/subscriptions/${subName}`)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['subs', topic] }),
  })
}

interface SendMessageInput {
  scope: SendScope
  body: string
  contentType?: string
  userProperties?: Record<string, unknown>
  sessionId?: string
}

export const useSendMessage = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ scope, ...payload }: SendMessageInput) => {
      await apiClient.post(messagePath(scope), payload)
    },
    onSuccess: (_data, variables) => {
      if (variables.scope.type === 'queue') {
        qc.invalidateQueries({ queryKey: ['messages', `queue:${variables.scope.name}`] })
      } else {
        // A topic send fans out to every subscription under it, so refresh them all.
        qc.invalidateQueries({
          predicate: (query) =>
            query.queryKey[0] === 'messages' &&
            typeof query.queryKey[1] === 'string' &&
            query.queryKey[1].startsWith(`subscription:${variables.scope.name}:`),
        })
      }
      // Refresh lists to reflect new counts
      qc.invalidateQueries({ queryKey: ['queues'] })
      qc.invalidateQueries({ queryKey: ['topics'] })
      qc.invalidateQueries({ queryKey: ['subs'] })
    },
  })
}

interface BulkDlqDeleteInput {
  scope: MessageScope
  messageIds?: string[]
}

export const useBulkDlqDelete = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ scope, messageIds }: BulkDlqDeleteInput) => {
      const res = await apiClient.post<CountResult>(dlqPath(scope), { messageIds })
      return res.data
    },
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['messages', scopeKey(variables.scope)] })
      qc.invalidateQueries({ queryKey: ['subs'] })
      qc.invalidateQueries({ queryKey: ['queues'] })
    },
  })
}

interface ReplayDlqInput {
  scope: MessageScope
  messageIds?: string[]
  body?: string
  contentType?: string
  userProperties?: Record<string, unknown>
  removeFromDlq?: boolean
}

export const useReplayDlq = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ scope, messageIds, body, contentType, userProperties, removeFromDlq }: ReplayDlqInput) => {
      const res = await apiClient.post<CountResult>(replayDlqPath(scope), { messageIds, body, contentType, userProperties, removeFromDlq })
      return res.data
    },
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['messages', scopeKey(variables.scope)] })
      qc.invalidateQueries({ queryKey: ['subs'] })
      qc.invalidateQueries({ queryKey: ['topics'] })
      qc.invalidateQueries({ queryKey: ['queues'] })
    },
  })
}

export const usePurgeMessages = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (scope: MessageScope) => {
      await apiClient.post(purgePath(scope))
    },
    onSuccess: (_data, scope) => {
      qc.invalidateQueries({ queryKey: ['messages', scopeKey(scope)] })
      qc.invalidateQueries({ queryKey: ['subs'] })
      qc.invalidateQueries({ queryKey: ['topics'] })
      qc.invalidateQueries({ queryKey: ['queues'] })
    },
  })
}
