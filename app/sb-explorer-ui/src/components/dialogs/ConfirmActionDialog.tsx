import { Modal, Stack, Text, TextInput, Title } from '@mantine/core'
import { useState, type ReactNode } from 'react'
import { ConfirmationContext } from './ConfirmationContext'

interface ConfirmActionDialogProps {
  open: boolean
  title: string
  description?: string
  confirmationText?: string
  children: ReactNode
  onOpenChange: (open: boolean) => void
}

const ConfirmActionDialog = ({ open, title, description, confirmationText, children, onOpenChange }: ConfirmActionDialogProps) => {
  const [typed, setTyped] = useState('')
  const [wasOpen, setWasOpen] = useState(open)
  if (wasOpen !== open) { setWasOpen(open); setTyped('') }
  return (
    <Modal opened={open} onClose={() => onOpenChange(false)} title={<Title order={4}>{title}</Title>} centered>
      <Stack gap="sm">
        {description && (
          <Text size="sm" c="dimmed">
            {description}
          </Text>
        )}
        {confirmationText && <TextInput label="Type entity name to confirm" description={confirmationText} value={typed} onChange={event => setTyped(event.currentTarget.value)} />}
        <ConfirmationContext.Provider value={!confirmationText || typed === confirmationText}>{children}</ConfirmationContext.Provider>
      </Stack>
    </Modal>
  )
}

export default ConfirmActionDialog
