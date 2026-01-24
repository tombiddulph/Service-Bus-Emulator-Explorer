import { useEffect, useState } from 'react'
import { Button, Group, Modal, Stack, TextInput, Title } from '@mantine/core'

interface CreateTopicDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (payload: { name: string }) => void
}

const CreateTopicDialog = ({ open, onOpenChange, onSubmit }: CreateTopicDialogProps) => {
  const [name, setName] = useState('')

  useEffect(() => {
    if (!open) setName('')
  }, [open])

  const handleSubmit = () => {
    if (!name) return
    onSubmit({ name })
  }

  return (
    <Modal opened={open} onClose={() => onOpenChange(false)} title={<Title order={4}>Create topic</Title>} centered>
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
    </Modal>
  )
}

export default CreateTopicDialog
