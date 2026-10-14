import { useState } from 'react'
import { Button, Group, Modal, NumberInput, Stack, TextInput, Title } from '@mantine/core'
import ConfirmSubmitButton from './ConfirmSubmitButton'

interface CreateSubscriptionDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (payload: { name: string; maxDeliveryCount?: number; lockDuration?: string; defaultTtl?: string }) => void
}

const CreateSubscriptionForm = ({ onOpenChange, onSubmit }: Omit<CreateSubscriptionDialogProps, 'open'>) => {
  const [name, setName] = useState('')
  const [maxDeliveryCount, setMaxDeliveryCount] = useState('10')
  const [lockDuration, setLockDuration] = useState('00:01:00')
  const [defaultTtl, setDefaultTtl] = useState('01:00:00')

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
    <Stack gap="sm">
      <TextInput label="Name" required value={name} onChange={(e) => setName(e.currentTarget.value)} data-autofocus />
      <NumberInput
        label="Max delivery count"
        value={maxDeliveryCount}
        onChange={(value) => setMaxDeliveryCount(String(value ?? ''))}
        min={1}
      />
      <TextInput label="Lock duration (hh:mm:ss)" value={lockDuration} onChange={(e) => setLockDuration(e.currentTarget.value)} />
      <TextInput label="Default TTL (hh:mm:ss)" description="Maximum 01:00:00 in the emulator" value={defaultTtl} onChange={(e) => setDefaultTtl(e.currentTarget.value)} />
      <Group justify="flex-end" mt="sm">
        <Button variant="default" onClick={() => onOpenChange(false)}>
          Cancel
        </Button>
        <ConfirmSubmitButton onClick={handleSubmit} disabled={!name}>
          Create
        </ConfirmSubmitButton>
      </Group>
    </Stack>
  )
}

const CreateSubscriptionDialog = ({ open, onOpenChange, onSubmit }: CreateSubscriptionDialogProps) => (
  <Modal opened={open} onClose={() => onOpenChange(false)} title={<Title order={4}>Create subscription</Title>} centered>
    <CreateSubscriptionForm key={String(open)} onOpenChange={onOpenChange} onSubmit={onSubmit} />
  </Modal>
)

export default CreateSubscriptionDialog
