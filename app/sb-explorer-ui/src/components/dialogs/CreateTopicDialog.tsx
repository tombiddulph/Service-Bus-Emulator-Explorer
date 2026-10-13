import { useState } from 'react'
import { Button, Group, Modal, Stack, TextInput, Title } from '@mantine/core'

interface CreateTopicDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (payload: { name: string }) => void
}

const CreateTopicForm = ({ onOpenChange, onSubmit }: Omit<CreateTopicDialogProps, 'open'>) => {
  const [name, setName] = useState('')

  const handleSubmit = () => {
    if (!name) return
    onSubmit({ name })
  }

  return (
    <Stack gap="sm">
      <TextInput label="Name" required value={name} onChange={(e) => setName(e.currentTarget.value)} data-autofocus />
      <Group justify="flex-end" mt="sm">
        <Button variant="default" onClick={() => onOpenChange(false)}>
          Cancel
        </Button>
        <Button onClick={handleSubmit} disabled={!name}>
          Create
        </Button>
      </Group>
    </Stack>
  )
}

const CreateTopicDialog = ({ open, onOpenChange, onSubmit }: CreateTopicDialogProps) => (
  <Modal opened={open} onClose={() => onOpenChange(false)} title={<Title order={4}>Create topic</Title>} centered>
    <CreateTopicForm key={String(open)} onOpenChange={onOpenChange} onSubmit={onSubmit} />
  </Modal>
)

export default CreateTopicDialog
