import { useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ActionIcon, Button, Checkbox, Group, Loader, Paper, Stack, Table, Text, Tooltip } from '@mantine/core'
import { IconRefresh } from '@tabler/icons-react'
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
  useSendMessage,
  useSubscriptions,
  useTopics,
  useReplayDlq,
} from '../api/hooks'
import type { MessageState, TopicInfo } from '../api/types'
import StatusPill from '../components/StatusPill'
import { formatMessageCount, messageCountTooltip } from '../utils/formatCount'
import { useAppContext } from '../App'

const TopicDetail = () => {
  const { name, subscription } = useParams()
  const navigate = useNavigate()
  const { theme } = useAppContext()
  const { data: topics, isLoading } = useTopics()
  const topicList = (Array.isArray(topics) ? topics : topics ? Object.values(topics as any) : []) as TopicInfo[]
  const topic = useMemo(() => topicList.find((t) => t.name === name), [topicList, name])

  const { data: subs, refetch: refetchSubs } = useSubscriptions(name ?? '', true)
  const subsList = (Array.isArray(subs) ? subs : subs ? Object.values(subs as any) : []) as typeof subs
  const sub = useMemo(() => subsList?.find((s: any) => s.name === subscription), [subsList, subscription])

  const [messageState, setMessageState] = useState<MessageState>('active')
  const [skip, setSkip] = useState(0)
  const take = 25
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const [inspect, setInspect] = useState<string | undefined>()
  const [sendOpen, setSendOpen] = useState(false)
  const [createSubOpen, setCreateSubOpen] = useState(false)
  const [deleteTopicOpen, setDeleteTopicOpen] = useState(false)
  const [deleteSubOpen, setDeleteSubOpen] = useState(false)
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

  const inspectingMessage = messages?.data?.items?.find((m) => m.messageId === inspect)

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
    <Paper withBorder radius="lg" p="md" shadow="sm" mt="sm" style={{ color: 'var(--mantine-color-text)' }}>
      <Group justify="space-between" align="center" mb="sm">
        <Text fw={600}>Subscriptions</Text>
        <Button onClick={() => setCreateSubOpen(true)}>Create subscription</Button>
      </Group>
      <Table
        verticalSpacing="sm"
        horizontalSpacing="md"
        highlightOnHover
        withRowBorders={false}
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
              <Table.Td>{s.name}</Table.Td>
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
    </Paper>
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
          />

          <Group justify="space-between" align="center">
            <Group gap="xs">
              <Text fw={600}>Messages</Text>
              <Tooltip label="Refresh subscription messages">
                <ActionIcon variant="light" aria-label="Refresh subscription messages" onClick={() => messages.refetch()} loading={messages.isFetching}>
                  <IconRefresh size={16} />
                </ActionIcon>
              </Tooltip>
            </Group>
            {messageState === 'deadletter' && (
              <Group gap="xs">
                <Checkbox
                  label="Remove from DLQ"
                  checked={removeFromDlq}
                  onChange={(event) => setRemoveFromDlq(event.currentTarget.checked)}
                />
                <Button
                  color="teal"
                  disabled={replayDlq.isPending || (selectedIds.length === 0 && !messages.data?.items.length)}
                  onClick={() =>
                    replayDlq.mutate(
                      { scope: messageScope, messageIds: selectedIds.length ? selectedIds : undefined, removeFromDlq },
                      {
                        onSuccess: (result) => {
                          const notFoundCount = result.notFound?.length ?? 0
                          notifications.show({
                            title: 'DLQ replay complete',
                            message: `Replayed ${result.count} message${result.count === 1 ? '' : 's'}.${notFoundCount ? ` ${notFoundCount} message${notFoundCount === 1 ? '' : 's'} not found.` : ''}`,
                            color: notFoundCount ? 'yellow' : 'green',
                          })
                          setSelectedIds([])
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

      <MessageDetailPanel
        message={inspectingMessage}
        open={!!inspect}
        onOpenChange={(open) => !open && setInspect(undefined)}
        editable={messageState === 'deadletter'}
        onSaveAndRequeue={(body, removeFromDlq) => {
          if (!inspectingMessage) return
          replayDlq.mutate(
            { scope: messageScope, messageIds: [inspectingMessage.messageId], body, removeFromDlq },
            {
              onSuccess: () => setInspect(undefined),
              onError: (error) => {
                notifications.show({ title: 'Save & requeue failed', message: error instanceof Error ? error.message : 'Unable to requeue message.', color: 'red' })
              },
            },
          )
        }}
      />

      <SendMessageDialog
        open={sendOpen}
        onOpenChange={setSendOpen}
        theme={theme}
        onSubmit={(payload) =>
          sendMessage.mutate(
            {
              scope: showSubscriptionDetail ? { type: 'topic', name: topic.name } : { type: 'topic', name: topic.name },
              ...payload,
            },
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
    </Stack>
  )
}

export default TopicDetail
