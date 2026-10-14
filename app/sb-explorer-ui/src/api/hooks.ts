import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import axios from 'axios'
import { useRefreshSettings } from './refreshSettings'
import { apiClient, dlqPath, messagePath, purgePath, replayDlqPath } from './client'
import type {
  DeleteDlqResult,
  MessageInfo,
  MessageScope,
  MessageState,
  PagedResult,
  PurgeResult,
  QueueInfo,
  ReplayDlqResult,
  SendScope,
  SubscriptionInfo,
  TopicInfo,
} from './types'

const dlqOperationTimeoutMs = 45000

export const useEnvironment = () =>
  useQuery({
    queryKey: ['environment'],
    queryFn: async ({ signal }) => (await apiClient.get<{ name: string; readOnly: boolean; refreshIntervalMs: number }>('/environment', { signal })).data,
    staleTime: Infinity,
    // The API can still be warming up on the very first page load, so keep retrying instead of failing permanently.
    retry: 10,
    retryDelay: (attempt) => Math.min(1000 * 2 ** attempt, 10000),
    refetchOnMount: 'always',
    refetchOnWindowFocus: true,
    refetchOnReconnect: true,
  })

export const useReadOnly = () => useEnvironment().data?.readOnly ?? true

function usePollingInterval(): number | false {
  const settings = useRefreshSettings()
  const environment = useEnvironment()
  return settings.paused ? false : settings.intervalMs ?? environment.data?.refreshIntervalMs ?? 8000
}

const scopeKey = (scope: MessageScope) =>
  scope.type === 'queue' ? `queue:${scope.name}` : `subscription:${scope.topic}:${scope.subscription}`

export const useQueues = () =>
  useQuery({
    queryKey: ['queues'],
    queryFn: async ({ signal }) => (await apiClient.get<QueueInfo[]>('/queues', { signal })).data,
    refetchInterval: usePollingInterval(),
  })

export const useTopics = () =>
  useQuery({
    queryKey: ['topics'],
    queryFn: async ({ signal }) => (await apiClient.get<TopicInfo[]>('/topics', { signal })).data,
    refetchInterval: usePollingInterval(),
  })

export const useSubscriptions = (topic: string, enabled = true) =>
  useQuery({
    queryKey: ['subs', topic],
    queryFn: async ({ signal }) => (await apiClient.get<SubscriptionInfo[]>(`/topics/${encodeURIComponent(topic)}/subscriptions`, { signal })).data,
    enabled: !!topic && enabled,
    refetchInterval: usePollingInterval(),
  })

interface MessagesQuery {
  scope: MessageScope
  state: MessageState
  skip: number
  take: number
  cursor?: string
  enabled?: boolean
}

const isScopeValid = (scope: MessageScope) => {
  if (scope.type === 'queue') return !!scope.name
  return !!scope.topic && !!scope.subscription
}

export const useMessages = ({ scope, state, skip, take, cursor, enabled = true }: MessagesQuery) =>
  useQuery({
    queryKey: ['messages', scopeKey(scope), state, skip, take, cursor],
    queryFn: async ({ signal }) =>
      (
        await apiClient.get<PagedResult<MessageInfo>>(messagePath(scope), {
          params: { mode: 'peek', state, skip: cursor ? 0 : skip, take, fromSequenceNumber: cursor },
          signal,
        })
      ).data,
    enabled: enabled && isScopeValid(scope),
    refetchInterval: usePollingInterval(),
    refetchIntervalInBackground: false,
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
       const res = await apiClient.post<DeleteDlqResult>(dlqPath(scope), { messageIds }, { timeout: dlqOperationTimeoutMs })
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
       const res = await apiClient.post<ReplayDlqResult>(replayDlqPath(scope), { messageIds, body, contentType, userProperties, removeFromDlq }, { timeout: dlqOperationTimeoutMs })
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
      try {
        const response = await apiClient.post<PurgeResult>(purgePath(scope), undefined, { timeout: 35000 })
        return response.data
      } catch (error) {
        if (axios.isAxiosError<PurgeResult>(error) && error.response?.data?.status) {
          return error.response.data
        }
        throw error
      }
    },
    onSuccess: (_data, scope) => {
      qc.invalidateQueries({ queryKey: ['messages', scopeKey(scope)] })
      qc.invalidateQueries({ queryKey: ['subs'] })
      qc.invalidateQueries({ queryKey: ['topics'] })
      qc.invalidateQueries({ queryKey: ['queues'] })
    },
  })
}
