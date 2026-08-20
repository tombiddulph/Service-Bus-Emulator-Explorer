import { useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Button, Checkbox, Group, Loader, Stack, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import EntityHeader from '../components/EntityHeader'
import EntityOverviewCard from '../components/EntityOverviewCard'
import MessageDetailPanel from '../components/MessageDetailPanel'
import MessageGrid from '../components/MessageGrid'
import MessageTabs from '../components/MessageTabs'
import ConfirmActionDialog from '../components/dialogs/ConfirmActionDialog'
import SendMessageDialog from '../components/dialogs/SendMessageDialog'
import {
  useBulkDlqDelete,
  useDeleteQueue,
  useMessages,
  usePurgeMessages,
  useQueues,
  useReplayDlq,
  useSendMessage,
} from '../api/hooks'
import type { MessageState, QueueInfo } from '../api/types'
import { useAppContext } from '../App'

const QueueDetail = () => {
  const { name } = useParams()
  const navigate = useNavigate()
  const { theme } = useAppContext()
  const { data: queues, isLoading } = useQueues()
  const queueList = (Array.isArray(queues) ? queues : queues ? Object.values(queues as any) : []) as QueueInfo[]
  const queue = useMemo(() => queueList.find((q) => q.name === name), [queueList, name])

  const [messageState, setMessageState] = useState<MessageState>('active')
  const [skip, setSkip] = useState(0)
  const take = 25
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const [inspect, setInspect] = useState<string | undefined>()
  const [sendOpen, setSendOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [purgeOpen, setPurgeOpen] = useState(false)
  const [removeFromDlq, setRemoveFromDlq] = useState(true)

  const messages = useMessages({
    scope: { type: 'queue', name: name ?? '' },
    state: messageState,
    skip,
    take,
    enabled: !!name,
  })

  const bulkDelete = useBulkDlqDelete()
  const replayDlq = useReplayDlq()
  const sendMessage = useSendMessage()
  const deleteQueue = useDeleteQueue()
  const purgeMessages = usePurgeMessages()

  const inspectingMessage = messages.data?.items?.find((m: any) => m.messageId === inspect)

  const handleReplaySuccess = (result: { count: number; notFound?: string[] }) => {
    const notFoundCount = result.notFound?.length ?? 0
    notifications.show({
      title: 'DLQ replay complete',
      message: `Replayed ${result.count} message${result.count === 1 ? '' : 's'}.${notFoundCount ? ` ${notFoundCount} message${notFoundCount === 1 ? '' : 's'} not found.` : ''}`,
      color: notFoundCount ? 'yellow' : 'green',
    })
  }

  const handleDelete = async () => {
    if (!name) return
    await deleteQueue.mutateAsync(name)
    setDeleteOpen(false)
    navigate('/queues')
  }

  if (!name) return <Text>No queue selected.</Text>
  if (isLoading && !queue) return <Loader size="sm" />
  if (!queue) return <Text>Queue not found.</Text>

  return (
    <Stack gap="md">
      <EntityHeader
        name={queue.name}
        type="queue"
        status={queue.status}
        activeCount={queue.activeMessageCount}
        deadLetterCount={queue.deadLetterMessageCount}
        activeCountIsExact={queue.activeMessageCountIsExact}
        deadLetterCountIsExact={queue.deadLetterMessageCountIsExact}
        onSend={() => setSendOpen(true)}
        onDelete={() => setDeleteOpen(true)}
      />

      <EntityOverviewCard
        title="Properties"
        items={[
          { label: 'Status', value: queue.status },
          { label: 'Max delivery count', value: queue.maxDeliveryCount },
          { label: 'Lock duration', value: queue.lockDuration },
          { label: 'Default TTL', value: queue.defaultTtl },
          { label: 'Created', value: queue.createdAt ? new Date(queue.createdAt).toLocaleString() : '—' },
        ]}
      />

      <Stack gap="sm">
        <MessageTabs
          state={messageState}
          onChange={(state) => {
            setMessageState(state)
            setSelectedIds([])
            setSkip(0)
          }}
          activeCount={queue.activeMessageCount}
          deadLetterCount={queue.deadLetterMessageCount}
        />

        <Group justify="space-between" align="center">
          <Text fw={600}>Messages</Text>
            {messageState === 'active' && (
              <Button
                color="red"
                variant="light"
                disabled={purgeMessages.isPending || !queue.activeMessageCount}
                onClick={() => setPurgeOpen(true)}
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
                  color="teal"
                  disabled={replayDlq.isPending || (selectedIds.length === 0 && !messages.data?.items.length)}
                  onClick={() =>
                    replayDlq.mutate(
                      { scope: { type: 'queue', name }, messageIds: selectedIds.length ? selectedIds : undefined, removeFromDlq },
                      {
                        onSuccess: (result) => {
                          handleReplaySuccess(result)
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
                      { scope: { type: 'queue', name }, messageIds: selectedIds },
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
          messages={messages.data}
          loading={messages.isLoading}
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

      {inspectingMessage && (
        <MessageDetailPanel
          key={inspectingMessage.messageId}
          message={inspectingMessage}
          open={!!inspect}
          onOpenChange={(open) => !open && setInspect(undefined)}
          editable={messageState === 'deadletter'}
          onSaveAndRequeue={(body, removeFromDlq) => {
            if (!name) return
            const messageId = inspectingMessage.messageId
            replayDlq.mutate(
              { scope: { type: 'queue', name }, messageIds: [messageId], body, removeFromDlq },
              {
                onSuccess: (result) => {
                  handleReplaySuccess(result)
                  setSelectedIds((ids) => ids.filter((id) => id !== messageId))
                  setInspect(undefined)
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
            {
              scope: { type: 'queue', name },
              ...payload,
            },
            {
              onSuccess: () => {
                setSendOpen(false)
                notifications.show({ title: 'Message sent', message: `Queued to ${name}`, color: 'green' })
              },
            },
          )
        }
      />

      <ConfirmActionDialog
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        title={`Delete queue ${queue.name}?`}
        description="Deleting the queue removes all messages."
      >
        <Group gap="xs" justify="flex-end">
          <Button variant="default" onClick={() => setDeleteOpen(false)}>
            Cancel
          </Button>
          <Button color="red" onClick={handleDelete}>
            Delete
          </Button>
        </Group>
      </ConfirmActionDialog>

      <ConfirmActionDialog
        open={purgeOpen}
        onOpenChange={setPurgeOpen}
        title={`Purge active messages in ${queue.name}?`}
        description="This permanently deletes every active (non-dead-lettered) message in the queue."
      >
        <Group gap="xs" justify="flex-end">
          <Button variant="default" onClick={() => setPurgeOpen(false)}>
            Cancel
          </Button>
          <Button
            color="red"
            onClick={() =>
              purgeMessages.mutate(
                { type: 'queue', name },
                {
                  onSuccess: () => {
                    setPurgeOpen(false)
                    setSelectedIds([])
                    notifications.show({ title: 'Queue purged', message: `Removed all active messages from ${name}`, color: 'green' })
                  },
                  onError: (error) => {
                    notifications.show({ title: 'Purge failed', message: error instanceof Error ? error.message : 'Unable to purge messages.', color: 'red' })
                  },
                },
              )
            }
          >
            Purge
          </Button>
        </Group>
      </ConfirmActionDialog>
    </Stack>
  )
}

export default QueueDetail
