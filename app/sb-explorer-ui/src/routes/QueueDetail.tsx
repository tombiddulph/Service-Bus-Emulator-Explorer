import { useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Button, Group, Loader, Stack, Text } from '@mantine/core'
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
  useQueues,
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

  const messages = useMessages({
    scope: { type: 'queue', name: name ?? '' },
    state: messageState,
    skip,
    take,
    enabled: !!name,
  })

  const bulkDelete = useBulkDlqDelete()
  const sendMessage = useSendMessage()
  const deleteQueue = useDeleteQueue()

  const inspectingMessage = messages.data?.items?.find((m: any) => m.messageId === inspect)

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
            {messageState === 'deadletter' && (
              <Button
                color="red"
                disabled={selectedIds.length === 0 || bulkDelete.isPending}
                onClick={() =>
                  bulkDelete.mutate(
                    {
                      scope: { type: 'queue', name },
                      messageIds: selectedIds,
                    },
                    {
                      onSuccess: () => {
                        notifications.show({
                          title: 'DLQ cleared',
                          message: `Deleted ${selectedIds.length} message${selectedIds.length === 1 ? '' : 's'}.`,
                          color: 'green',
                        })
                        setSelectedIds([])
                      },
                      onError: (error) => {
                        notifications.show({
                          title: 'DLQ delete failed',
                          message: error instanceof Error ? error.message : 'Unable to delete DLQ messages.',
                          color: 'red',
                        })
                      },
                    },
                  )
                }
              >
                Delete selected DLQ
              </Button>
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

      <MessageDetailPanel message={inspectingMessage} open={!!inspect} onOpenChange={(open) => !open && setInspect(undefined)} />

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
    </Stack>
  )
}

export default QueueDetail
