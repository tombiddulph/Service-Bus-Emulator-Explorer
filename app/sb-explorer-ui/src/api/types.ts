export type EntityStatus = 'Active' | 'Disabled' | 'SendDisabled' | 'ReceiveDisabled'

export interface QueueInfo {
  name: string
  status: EntityStatus
  activeMessageCount: number
  deadLetterMessageCount: number
  activeMessageCountIsExact?: boolean
  deadLetterMessageCountIsExact?: boolean
  scheduledMessageCount?: number
  createdAt?: string
  maxDeliveryCount?: number
  lockDuration?: string
  defaultTtl?: string
}

export interface TopicInfo {
  name: string
  status: EntityStatus
  activeMessageCount: number
  deadLetterMessageCount: number
  activeMessageCountIsExact?: boolean
  deadLetterMessageCountIsExact?: boolean
  scheduledMessageCount?: number
  createdAt?: string
}

export interface SubscriptionInfo {
  name: string
  status: EntityStatus
  activeMessageCount: number
  deadLetterMessageCount: number
  activeMessageCountIsExact?: boolean
  deadLetterMessageCountIsExact?: boolean
  scheduledMessageCount?: number
  maxDeliveryCount?: number
  lockDuration?: string
  defaultTtl?: string
  createdAt?: string
}

export interface MessageInfo {
  messageId: string
  bodyPreview: string
  body?: string
  enqueuedTime?: string
  expiresAt?: string
  deliveryCount?: number
  contentType?: string
  sessionId?: string
  userProperties?: Record<string, unknown>
  systemProperties?: Record<string, unknown>
}

export interface PagedResult<T> {
  items: T[]
  total?: number
  hasMore?: boolean
}

export type MessageState = 'active' | 'deadletter'

export type MessageScope =
  | { type: 'queue'; name: string }
  | { type: 'subscription'; topic: string; subscription: string }

// Messages can only be sent to a queue or a topic (the broker fans a topic send out to its
// subscriptions), never directly to a subscription.
export type SendScope = { type: 'queue'; name: string } | { type: 'topic'; name: string }

export interface CountResult {
  count: number
  notFound?: string[]
}

export interface ReplayMessageOutcome {
  messageId: string
  sent: boolean
  removedFromDlq: boolean
  error?: string
}

export interface ReplayDlqResult {
  count: number
  isPartial: boolean
  outcomes: ReplayMessageOutcome[]
  notFound?: string[]
  error?: string
}
