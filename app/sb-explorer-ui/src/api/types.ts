import type { components } from './generated'

type Schemas = components['schemas']
export type EntityStatus = Schemas['EntityStatus']
export type QueueInfo = Schemas['QueueInfo']
export type TopicInfo = Schemas['TopicInfo']
export type SubscriptionInfo = Schemas['SubscriptionInfo']
export type MessageInfo = Schemas['MessageInfo']
export type MessageState = Schemas['MessageState']
export type DeleteDlqResult = Schemas['DeleteDlqResult']
export type ReplayDlqResult = Schemas['ReplayDlqResult']
export type PurgeResult = Schemas['PurgeResult']
export type CreateQueueRequest = Schemas['CreateQueueRequest']
export type CreateTopicRequest = Schemas['CreateTopicRequest']
export type CreateSubscriptionRequest = Schemas['CreateSubscriptionRequest']
export type SendMessageRequest = Schemas['SendMessageRequest']
export type EnvironmentInfo = Schemas['EnvironmentInfo']
export type ConnectionInfo = Schemas['ConnectionInfo']
export type PagedResult<T> = Omit<Schemas['PagedMessages'], 'items'> & { items: T[] }

// UI navigation scopes are not wire contracts.
export type MessageScope = { type: 'queue'; name: string } | { type: 'subscription'; topic: string; subscription: string }
export type SendScope = { type: 'queue'; name: string } | { type: 'topic'; name: string }
