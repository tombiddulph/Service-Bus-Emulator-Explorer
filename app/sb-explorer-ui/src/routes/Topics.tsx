import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button, Group, Stack, Text } from '@mantine/core'
import EntityTable from '../components/EntityTable'
import ConfirmActionDialog from '../components/dialogs/ConfirmActionDialog'
import CreateTopicDialog from '../components/dialogs/CreateTopicDialog'
import { useCreateTopic, useDeleteTopic, useTopics } from '../api/hooks'
import type { TopicInfo } from '../api/types'

const Topics = () => {
  const navigate = useNavigate()
  const { data: topics, isLoading, refetch } = useTopics()
  const createTopic = useCreateTopic()
  const deleteTopic = useDeleteTopic()
  const [createOpen, setCreateOpen] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<TopicInfo | null>(null)

  const handleCreate = async (payload: { name: string }) => {
    await createTopic.mutateAsync(payload)
    setCreateOpen(false)
    refetch()
  }

  const handleDelete = async () => {
    if (!deleteTarget) return
    await deleteTopic.mutateAsync(deleteTarget.name)
    setDeleteTarget(null)
    refetch()
  }

  return (
    <Stack gap="md">
      <EntityTable
        title="Topics"
        items={topics}
        loading={isLoading}
        onRefresh={refetch}
        onCreate={() => setCreateOpen(true)}
        onRowClick={(t) => navigate(`/topics/${t.name}`)}
        emptyState={
          <Group gap="xs">
            <Text>No topics yet.</Text>
            <Button variant="subtle" onClick={() => setCreateOpen(true)}>
              Create one
            </Button>
          </Group>
        }
      />

      <CreateTopicDialog open={createOpen} onOpenChange={setCreateOpen} onSubmit={handleCreate} />

      <ConfirmActionDialog
        open={!!deleteTarget}
        onOpenChange={(open) => !open && setDeleteTarget(null)}
        title={`Delete topic ${deleteTarget?.name ?? ''}`}
        description="This will remove the topic and its messages."
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

export default Topics
