import { Modal, Stack, Text, Title } from '@mantine/core'
import type { ReactNode } from 'react'

interface ConfirmActionDialogProps {
  open: boolean
  title: string
  description?: string
  children: ReactNode
  onOpenChange: (open: boolean) => void
}

const ConfirmActionDialog = ({ open, title, description, children, onOpenChange }: ConfirmActionDialogProps) => {
  return (
    <Modal opened={open} onClose={() => onOpenChange(false)} title={<Title order={4}>{title}</Title>} centered>
      <Stack gap="sm">
        {description && (
          <Text size="sm" c="dimmed">
            {description}
          </Text>
        )}
        {children}
      </Stack>
    </Modal>
  )
}

export default ConfirmActionDialog
