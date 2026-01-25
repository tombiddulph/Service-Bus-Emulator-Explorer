import { useEffect, useState } from 'react'
import { Button, Group, Modal, NumberInput, Stack, TextInput, Title } from '@mantine/core'

interface CreateSubscriptionDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (payload: { name: string; maxDeliveryCount?: number; lockDuration?: string; defaultTtl?: string }) => void
}

const CreateSubscriptionDialog = ({ open, onOpenChange, onSubmit }: CreateSubscriptionDialogProps) => {
  const [name, setName] = useState('')
  const [maxDeliveryCount, setMaxDeliveryCount] = useState('10')
  const [lockDuration, setLockDuration] = useState('00:01:00')
  const [defaultTtl, setDefaultTtl] = useState('1.00:00:00')

  useEffect(() => {
    if (!open) {
      setName('')
      setMaxDeliveryCount('10')
      setLockDuration('00:01:00')
      setDefaultTtl('1.00:00:00')
    }
  }, [open])

  const handleSubmit = () => {
    if (!name) return
    onSubmit({
      name,
      maxDeliveryCount: maxDeliveryCount ? Number(maxDeliveryCount) : undefined,
      lockDuration,
      defaultTtl,
    })
  }

  return (
    <Modal opened={open} onClose={() => onOpenChange(false)} title={<Title order={4}>Create subscription</Title>} centered>
      <Stack gap="sm">
        <TextInput label="Name" required value={name} onChange={(e) => setName(e.currentTarget.value)} data-autofocus />
        <NumberInput
          label="Max delivery count"
          value={maxDeliveryCount}
          onChange={(value) => setMaxDeliveryCount(String(value ?? ''))}
          min={1}
        />
        <TextInput label="Lock duration (hh:mm:ss)" value={lockDuration} onChange={(e) => setLockDuration(e.currentTarget.value)} />
        <TextInput label="Default TTL (d.hh:mm:ss)" value={defaultTtl} onChange={(e) => setDefaultTtl(e.currentTarget.value)} />
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

export default CreateSubscriptionDialog
