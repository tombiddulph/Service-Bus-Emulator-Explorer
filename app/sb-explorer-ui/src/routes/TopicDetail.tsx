import { useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Button, Checkbox, Group, Loader, ScrollArea, Stack, Table, Text, Tooltip } from '@mantine/core'
import { IconPlayerPlay, IconRefresh, IconTrash } from '@tabler/icons-react'
import { notifications } from '@mantine/notifications'
import EntityHeader from '../components/EntityHeader'
import EntityOverviewCard from '../components/EntityOverviewCard'
import MessageDetailPanel from '../components/MessageDetailPanel'
import MessageGrid from '../components/MessageGrid'
import MessageTabs from '../components/MessageTabs'
import ConfirmActionDialog from '../components/dialogs/ConfirmActionDialog'
import CreateSubscriptionDialog from '../components/dialogs/CreateSubscriptionDialog'
import SendMessageDialog from '../components/dialogs/SendMessageDialog'
import {
  useBulkDlqDelete,
  useCreateSubscription,
  useDeleteSubscription,
  useDeleteTopic,
  useMessages,
  usePurgeMessages,
  useSendMessage,
  useSubscriptions,
  useTopics,
  useReplayDlq,
} from '../api/hooks'
import type { MessageScope, MessageState, ReplayDlqResult } from '../api/types'
import StatusPill from '../components/StatusPill'
import { formatMessageCount, messageCountTooltip } from '../utils/formatCount'
import { useAppContext } from '../App'
import { summarizeReplayResult } from '../utils/replayResult'

const TopicDetailContent = () => {
  const { name, subscription } = useParams()
  const navigate = useNavigate()
  const { theme } = useAppContext()
  const { data: topics, isLoading } = useTopics()
  const topic = topics?.find(t => t.name === name)

  const { data: subs, refetch: refetchSubs } = useSubscriptions(name ?? '', true)
  const sub = subs?.find(s => s.name === subscription)

  const [messageState, setMessageState] = useState<MessageState>('active')
  const [skip, setSkip] = useState(0)
  const take = 25
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const [inspect, setInspect] = useState<string | undefined>()
  const [sendOpen, setSendOpen] = useState(false)
  const [createSubOpen, setCreateSubOpen] = useState(false)
  const [deleteTopicOpen, setDeleteTopicOpen] = useState(false)
  const [deleteSubOpen, setDeleteSubOpen] = useState(false)
  const [purgeTarget, setPurgeTarget] = useState<MessageScope>()
  const [removeFromDlq, setRemoveFromDlq] = useState(true)

  const messageScope = useMemo(
    () => ({ type: 'subscription', topic: name ?? '', subscription: subscription ?? '' } as const),
    [name, subscription],
  )

  const messages = useMessages({
    scope: messageScope,
    state: messageState,
    skip,
    take,
    enabled: !!name && !!subscription,
  })

  const bulkDelete = useBulkDlqDelete()
  const replayDlq = useReplayDlq()
  const sendMessage = useSendMessage()
  const createSubscription = useCreateSubscription(name ?? '')
  const deleteSubscription = useDeleteSubscription(name ?? '')
  const deleteTopic = useDeleteTopic()
  const purgeMessages = usePurgeMessages()

  const inspectingMessage = messages?.data?.items?.find((m) => m.messageId === inspect)

  const handleReplaySuccess = (result: ReplayDlqResult) => {
    const summary = summarizeReplayResult(result)
    notifications.show(summary)
    return summary.retryIds
  }

  const handleCreateSub = async (payload: { name: string; maxDeliveryCount?: number; lockDuration?: string; defaultTtl?: string }) => {
    await createSubscription.mutateAsync(payload)
    setCreateSubOpen(false)
    refetchSubs()
  }

  const handleDeleteSub = async () => {
    if (!subscription) return
    await deleteSubscription.mutateAsync(subscription)
    setDeleteSubOpen(false)
    navigate(`/topics/${name}`)
    refetchSubs()
  }

  const handleDeleteTopic = async () => {
    if (!name) return
    await deleteTopic.mutateAsync(name)
    setDeleteTopicOpen(false)
    navigate('/topics')
  }

  if (!name) return <Text>No topic selected.</Text>
  if (isLoading && !topic) return <Loader size="sm" />
  if (!topic) return <Text>Topic not found.</Text>

  const renderSubscriptionList = () => (
    <section aria-label="Subscriptions">
      <Group justify="space-between" align="center" mb="sm">
        <Text fw={600}>Subscriptions</Text>
        <Button variant="subtle" onClick={() => setCreateSubOpen(true)}>Create subscription</Button>
      </Group>
      <ScrollArea>
      <Table
        className="portal-table"
        miw={500}
        verticalSpacing="xs"
        horizontalSpacing="md"
        highlightOnHover
        withRowBorders
        styles={{ th: { color: 'inherit' }, td: { color: 'inherit' } }}
      >
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Name</Table.Th>
            <Table.Th>Status</Table.Th>
            <Table.Th>Active</Table.Th>
            <Table.Th>DLQ</Table.Th>
            <Table.Th></Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {subs?.map((s) => (
            <Table.Tr key={s.name} style={{ cursor: 'pointer' }} onClick={() => navigate(`/topics/${name}/${s.name}`)}>
              <Table.Td><button className="portal-link-button" onClick={event => { event.stopPropagation(); navigate(`/topics/${encodeURIComponent(name!)}/${encodeURIComponent(s.name)}`) }}>{s.name}</button></Table.Td>
              <Table.Td>
                <StatusPill status={s.status} />
              </Table.Td>
              <Table.Td>
                <Tooltip label={messageCountTooltip(s.activeMessageCountIsExact)} disabled={s.activeMessageCountIsExact !== false}>
                  <span>{formatMessageCount(s.activeMessageCount, s.activeMessageCountIsExact)}</span>
                </Tooltip>
              </Table.Td>
              <Table.Td>
                <Tooltip label={messageCountTooltip(s.deadLetterMessageCountIsExact)} disabled={s.deadLetterMessageCountIsExact !== false}>
                  <span>{formatMessageCount(s.deadLetterMessageCount, s.deadLetterMessageCountIsExact)}</span>
                </Tooltip>
              </Table.Td>
              <Table.Td>
                <Button variant="subtle" onClick={(e) => { e.stopPropagation(); navigate(`/topics/${name}/${s.name}`) }}>
                  Open
                </Button>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
      </ScrollArea>
      {subs?.length === 0 && <Text size="sm" c="dimmed" py="lg">No subscriptions yet.</Text>}
    </section>
  )

  const showSubscriptionDetail = !!subscription && sub

  return (
    <Stack gap="md">
      <EntityHeader
        name={showSubscriptionDetail ? `${topic.name} / ${sub?.name}` : topic.name}
        type={showSubscriptionDetail ? 'subscription' : 'topic'}
        status={showSubscriptionDetail ? sub!.status : topic.status}
        activeCount={showSubscriptionDetail ? sub!.activeMessageCount : topic.activeMessageCount}
        deadLetterCount={showSubscriptionDetail ? sub!.deadLetterMessageCount : topic.deadLetterMessageCount}
        activeCountIsExact={showSubscriptionDetail ? sub!.activeMessageCountIsExact : topic.activeMessageCountIsExact}
        deadLetterCountIsExact={showSubscriptionDetail ? sub!.deadLetterMessageCountIsExact : topic.deadLetterMessageCountIsExact}
        onSend={() => setSendOpen(true)}
        onDelete={() => (showSubscriptionDetail ? setDeleteSubOpen(true) : setDeleteTopicOpen(true))}
        onCreateSubscription={showSubscriptionDetail ? undefined : () => setCreateSubOpen(true)}
      />

      <EntityOverviewCard
        title="Properties"
        items={[
          { label: 'Status', value: showSubscriptionDetail ? sub?.status : topic.status },
          { label: 'Max delivery count', value: showSubscriptionDetail ? sub?.maxDeliveryCount : undefined },
          { label: 'Lock duration', value: showSubscriptionDetail ? sub?.lockDuration : undefined },
          { label: 'Default TTL', value: showSubscriptionDetail ? sub?.defaultTtl : undefined },
          { label: 'Created', value: showSubscriptionDetail ? (sub?.createdAt ? new Date(sub.createdAt).toLocaleString() : '—') : (topic.createdAt ? new Date(topic.createdAt).toLocaleString() : '—') },
        ]}
      />

      {showSubscriptionDetail ? (
        <Stack gap="sm">
          <MessageTabs
            state={messageState}
            onChange={(state) => {
              setMessageState(state)
              setSelectedIds([])
              setSkip(0)
            }}
            activeCount={sub?.activeMessageCount}
            deadLetterCount={sub?.deadLetterMessageCount}
            activeCountIsExact={sub?.activeMessageCountIsExact}
            deadLetterCountIsExact={sub?.deadLetterMessageCountIsExact}
          />

          <Group gap={4} className="portal-command-bar" role="toolbar" aria-label="Message commands">
            <Button variant="subtle" leftSection={<IconRefresh size={16} />} loading={messages.isFetching} onClick={() => messages.refetch()}>Refresh messages</Button>
            {messageState === 'active' && (
              <Button
                color="red"
                variant="subtle"
                leftSection={<IconTrash size={16} />}
                disabled={purgeMessages.isPending || !sub?.activeMessageCount}
                onClick={() => setPurgeTarget(messageScope)}
              >
                Purge active messages
              </Button>
            )}
            {messageState === 'deadletter' && (
              <Group gap="xs">
                <Checkbox
                  label="Remove from DLQ"
                  checked={removeFromDlq}
                  onChange={(event) => setRemoveFromDlq(event.currentTarget.checked)}
                />
                <Button
                  variant="subtle"
                  leftSection={<IconPlayerPlay size={16} />}
                  disabled={replayDlq.isPending || (selectedIds.length === 0 && !messages.data?.items.length)}
                  onClick={() =>
                    replayDlq.mutate(
                      { scope: messageScope, messageIds: selectedIds.length ? selectedIds : undefined, removeFromDlq },
                      {
                        onSuccess: (result) => {
                          setSelectedIds(handleReplaySuccess(result))
                        },
                        onError: (error) => {
                          notifications.show({ title: 'DLQ replay failed', message: error instanceof Error ? error.message : 'Unable to replay DLQ messages.', color: 'red' })
                        },
                      },
                    )
                  }
                >
                  {selectedIds.length ? 'Replay selected' : 'Replay all'}
                </Button>
                <Button
                  color="red"
                  variant="subtle"
                  leftSection={<IconTrash size={16} />}
                  disabled={selectedIds.length === 0 || bulkDelete.isPending}
                  onClick={() =>
                    bulkDelete.mutate(
                      { scope: { type: 'subscription', topic: name, subscription }, messageIds: selectedIds },
                      {
                        onSuccess: (result) => {
                          const notFoundCount = result.notFound?.length ?? 0
                          notifications.show({
                            title: 'DLQ cleared',
                            message: `Deleted ${result.count} message${result.count === 1 ? '' : 's'}.${notFoundCount ? ` ${notFoundCount} message${notFoundCount === 1 ? '' : 's'} not found.` : ''}`,
                            color: notFoundCount ? 'yellow' : 'green',
                          })
                          setSelectedIds([])
                        },
                        onError: (error) => {
                          notifications.show({ title: 'DLQ delete failed', message: error instanceof Error ? error.message : 'Unable to delete DLQ messages.', color: 'red' })
                        },
                      },
                    )
                  }
                >Delete selected DLQ</Button>
              </Group>
            )}
          </Group>

          <MessageGrid
            messages={messages?.data}
            loading={messages?.isLoading}
            state={messageState}
            skip={skip}
            take={take}
            selectedIds={selectedIds}
            onSelectionChange={setSelectedIds}
            onPageChange={(next) => {
              setSkip(Math.max(0, next))
              setSelectedIds([])
            }}
            onInspect={(msg) => setInspect(msg.messageId)}
          />
        </Stack>
      ) : (
        renderSubscriptionList()
      )}

      {inspectingMessage && (
        <MessageDetailPanel
          key={inspectingMessage.messageId}
          message={inspectingMessage}
          open={!!inspect}
          onOpenChange={(open) => !open && setInspect(undefined)}
          editable={messageState === 'deadletter'}
          onSaveAndRequeue={(body, removeFromDlq) => {
            const messageId = inspectingMessage.messageId
            replayDlq.mutate(
              { scope: messageScope, messageIds: [messageId], body, removeFromDlq },
              {
                onSuccess: (result) => {
                  const retryIds = handleReplaySuccess(result)
                  setSelectedIds(retryIds)
                  if (!retryIds.includes(messageId)) setInspect(undefined)
                },
                onError: (error) => {
                  notifications.show({ title: 'Save & requeue failed', message: error instanceof Error ? error.message : 'Unable to requeue message.', color: 'red' })
                },
              },
            )
          }}
        />
      )}

      <SendMessageDialog
        open={sendOpen}
        onOpenChange={setSendOpen}
        theme={theme}
        onSubmit={(payload) =>
          sendMessage.mutate(
            // Sending always targets the topic - the broker fans it out to every subscription,
            // including the one currently being viewed.
            { scope: { type: 'topic', name: topic.name }, ...payload },
            {
              onSuccess: () => {
                setSendOpen(false)
                notifications.show({ title: 'Message sent', message: `Sent to ${showSubscriptionDetail ? 'subscription' : 'topic'}`, color: 'green' })
              },
            },
          )
        }
      />

      <CreateSubscriptionDialog open={createSubOpen} onOpenChange={setCreateSubOpen} onSubmit={handleCreateSub} />

      <ConfirmActionDialog
        open={deleteTopicOpen}
        onOpenChange={setDeleteTopicOpen}
        title={`Delete topic ${topic.name}?`}
        description="Deleting the topic removes all subscriptions and messages."
      >
        <Group gap="xs" justify="flex-end">
          <Button variant="default" onClick={() => setDeleteTopicOpen(false)}>
            Cancel
          </Button>
          <Button color="red" onClick={handleDeleteTopic}>
            Delete
          </Button>
        </Group>
      </ConfirmActionDialog>

      <ConfirmActionDialog
        open={deleteSubOpen}
        onOpenChange={setDeleteSubOpen}
        title={`Delete subscription ${subscription}?`}
        description="Deleting the subscription removes its messages."
      >
        <Group gap="xs" justify="flex-end">
          <Button variant="default" onClick={() => setDeleteSubOpen(false)}>
            Cancel
          </Button>
          <Button color="red" onClick={handleDeleteSub}>
            Delete
          </Button>
        </Group>
      </ConfirmActionDialog>

      <ConfirmActionDialog
        open={!!purgeTarget}
        onOpenChange={(open) => !open && !purgeMessages.isPending && setPurgeTarget(undefined)}
        title={`Purge active messages in ${purgeTarget?.type === 'subscription' ? purgeTarget.subscription : subscription}?`}
        description="This permanently deletes every active (non-dead-lettered) message in the subscription."
      >
        <Group gap="xs" justify="flex-end">
          <Button variant="default" disabled={purgeMessages.isPending} onClick={() => setPurgeTarget(undefined)}>
            Cancel
          </Button>
          <Button
            color="red"
            disabled={!purgeTarget || purgeMessages.isPending}
            loading={purgeMessages.isPending}
            onClick={() =>
              purgeTarget && purgeMessages.mutate(purgeTarget, {
                onSuccess: (result) => {
                  setPurgeTarget(undefined)
                  setSelectedIds([])
                  const complete = result.status === 'Completed'
                  notifications.show({
                    title: complete ? 'Subscription purged' : 'Subscription purge incomplete',
                    message: complete
                      ? `Removed ${result.removedCount} active message${result.removedCount === 1 ? '' : 's'}.`
                      : `${result.message ?? 'The purge did not complete.'} Removed ${result.removedCount} message${result.removedCount === 1 ? '' : 's'} before stopping.`,
                    color: complete ? 'green' : result.removedCount ? 'yellow' : 'red',
                  })
                },
                onError: (error) => {
                  notifications.show({ title: 'Purge failed', message: error instanceof Error ? error.message : 'Unable to purge messages.', color: 'red' })
                },
              })
            }
          >
            Purge
          </Button>
        </Group>
      </ConfirmActionDialog>
    </Stack>
  )
}

const TopicDetail = () => {
  const { name, subscription } = useParams()
  return <TopicDetailContent key={`${name}/${subscription ?? ''}`} />
}

export default TopicDetail
