import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiClient, dlqPath, messagePath } from './client'
import type {
  MessageInfo,
  MessageScope,
  MessageState,
  PagedResult,
  QueueInfo,
  SubscriptionInfo,
  TopicInfo,
} from './types'

const listRefetchMs = 8000

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
    placeholderData: keepPreviousData,
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
  scope: MessageScope | { type: 'topic'; name: string }
  body: string
  contentType?: string
  userProperties?: Record<string, unknown>
}

export const useSendMessage = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ scope, ...payload }: SendMessageInput) => {
      await apiClient.post(messagePath(scope), payload)
    },
    onSuccess: (_data, variables) => {
      if (variables.scope.type === 'queue' || variables.scope.type === 'subscription') {
        qc.invalidateQueries({ queryKey: ['messages', scopeKey(variables.scope)] })
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
      await apiClient.post(dlqPath(scope), { messageIds })
    },
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['messages', scopeKey(variables.scope)] })
      qc.invalidateQueries({ queryKey: ['subs'] })
      qc.invalidateQueries({ queryKey: ['queues'] })
    },
  })
}
