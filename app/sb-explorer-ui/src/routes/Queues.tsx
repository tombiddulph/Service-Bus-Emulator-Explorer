import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button, Group, Stack, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import axios from 'axios'
import EntityTable from '../components/EntityTable'
import ConfirmActionDialog from '../components/dialogs/ConfirmActionDialog'
import CreateQueueDialog from '../components/dialogs/CreateQueueDialog'
import { useCreateQueue, useDeleteQueue, useQueues } from '../api/hooks'
import type { QueueInfo } from '../api/types'

const Queues = () => {
  const navigate = useNavigate()
  const { data: queues, isLoading, refetch } = useQueues()
  const createQueue = useCreateQueue()
  const deleteQueue = useDeleteQueue()
  const [createOpen, setCreateOpen] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<QueueInfo | null>(null)

  const handleCreate = async (payload: { name: string; maxDeliveryCount?: number; lockDuration?: string; defaultTtl?: string }) => {
    try {
      await createQueue.mutateAsync(payload)
      setCreateOpen(false)
      refetch()
    } catch (error) {
      const responseData = axios.isAxiosError(error)
        ? (error.response?.data as { detail?: string; error?: string } | undefined)
        : undefined
      const message = responseData?.detail || responseData?.error || (error instanceof Error ? error.message : 'Unable to create queue.')
      notifications.show({ title: 'Create queue failed', message, color: 'red' })
    }
  }

  const handleDelete = async () => {
    if (!deleteTarget) return
    await deleteQueue.mutateAsync(deleteTarget.name)
    setDeleteTarget(null)
    refetch()
  }

  return (
    <Stack gap="md">
      <EntityTable
        title="Queues"
        items={queues}
        loading={isLoading}
        onRefresh={refetch}
        onCreate={() => setCreateOpen(true)}
        onRowClick={(q) => navigate(`/queues/${q.name}`)}
        emptyState={
          <Group gap="xs">
            <Text>No queues yet.</Text>
            <Button variant="subtle" onClick={() => setCreateOpen(true)}>
              Create one
            </Button>
          </Group>
        }
      />

      <CreateQueueDialog open={createOpen} onOpenChange={setCreateOpen} onSubmit={handleCreate} />

      <ConfirmActionDialog
        open={!!deleteTarget}
        onOpenChange={(open) => !open && setDeleteTarget(null)}
        title={`Delete queue ${deleteTarget?.name ?? ''}`}
        description="This will remove the queue and any messages."
      >
        <Group gap="xs" justify="flex-end">
          <Button variant="default" onClick={() => setDeleteTarget(null)}>
            Cancel
          </Button>
          <Button color="red" onClick={handleDelete} disabled={!deleteTarget}>
            Delete
          </Button>
        </Group>
      </ConfirmActionDialog>
    </Stack>
  )
}

export default Queues
