import { useEffect, useState } from 'react'
import { Button, Group, Modal, NumberInput, Stack, TextInput, Title } from '@mantine/core'

interface CreateQueueDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (payload: { name: string; maxDeliveryCount?: number; lockDuration?: string; defaultTtl?: string }) => void
}

const maxDefaultTtlSeconds = 60 * 60

const parseTimeSpan = (value: string) => {
  if (!value) return null
  const [dayPart, restPart] = value.includes('.') ? value.split('.', 2) : [null, value]
  const [hours, minutes, seconds] = restPart.split(':')
  if ([hours, minutes, seconds].some((part) => part === undefined)) return null
  const days = dayPart ? Number(dayPart) : 0
  const hrs = Number(hours)
  const mins = Number(minutes)
  const secs = Number(seconds)
  if ([days, hrs, mins, secs].some((part) => Number.isNaN(part) || part < 0)) return null
  if (mins > 59 || secs > 59) return null
  return days * 86400 + hrs * 3600 + mins * 60 + secs
}

const CreateQueueDialog = ({ open, onOpenChange, onSubmit }: CreateQueueDialogProps) => {
  const [name, setName] = useState('')
  const [maxDeliveryCount, setMaxDeliveryCount] = useState('10')
  const [lockDuration, setLockDuration] = useState('00:01:00')
  const [defaultTtl, setDefaultTtl] = useState('01:00:00')
  const [defaultTtlError, setDefaultTtlError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) {
      setName('')
      setMaxDeliveryCount('10')
      setLockDuration('00:01:00')
      setDefaultTtl('01:00:00')
      setDefaultTtlError(null)
    }
  }, [open])

  const handleSubmit = () => {
    if (!name) return
    const ttlSeconds = parseTimeSpan(defaultTtl)
    if (ttlSeconds === null) {
      setDefaultTtlError('Use hh:mm:ss format (max 01:00:00).')
      return
    }
    if (ttlSeconds > maxDefaultTtlSeconds) {
      setDefaultTtlError('Maximum is 01:00:00.')
      return
    }
    setDefaultTtlError(null)
    onSubmit({
      name,
      maxDeliveryCount: maxDeliveryCount ? Number(maxDeliveryCount) : undefined,
      lockDuration,
      defaultTtl,
    })
  }

  return (
    <Modal opened={open} onClose={() => onOpenChange(false)} title={<Title order={4}>Create queue</Title>} centered>
      <Stack gap="sm">
        <TextInput label="Name" required value={name} onChange={(e) => setName(e.currentTarget.value)} data-autofocus />
        <NumberInput
          label="Max delivery count"
          description="Number of deliveries before DLQ"
          value={maxDeliveryCount}
          onChange={(value) => setMaxDeliveryCount(String(value ?? ''))}
          min={1}
        />
        <TextInput label="Lock duration (hh:mm:ss)" value={lockDuration} onChange={(e) => setLockDuration(e.currentTarget.value)} />
        <TextInput
          label="Default TTL (hh:mm:ss)"
          description="Max 01:00:00"
          value={defaultTtl}
          onChange={(e) => {
            setDefaultTtl(e.currentTarget.value)
            if (defaultTtlError) setDefaultTtlError(null)
          }}
          error={defaultTtlError}
        />
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

export default CreateQueueDialog
