import { Drawer, Stack, Group, ActionIcon, Title } from '@mantine/core'
import { IconX } from '@tabler/icons-react'
import type { ReactNode } from 'react'

interface SideDrawerProps {
  open: boolean
  title: ReactNode
  width?: number
  onOpenChange: (open: boolean) => void
  children: ReactNode
}

const SideDrawer = ({ open, title, width = 540, onOpenChange, children }: SideDrawerProps) => {
  return (
    <Drawer
      opened={open}
      onClose={() => onOpenChange(false)}
      position="right"
      size={width}
      padding="lg"
      withOverlay
      overlayProps={{ opacity: 0.4, blur: 2 }}
      styles={{
        content: {
          borderTopLeftRadius: 16,
          borderBottomLeftRadius: 16,
        },
      }}
      classNames={{
        content: 'drawer-content',
      }}
    >
      <Stack gap="md">
        <Group justify="space-between" align="center">
          <Title order={5} c="inherit">
            {title}
          </Title>
          <ActionIcon variant="subtle" aria-label="Close" onClick={() => onOpenChange(false)}>
            <IconX size={18} />
          </ActionIcon>
        </Group>
        {children}
      </Stack>
    </Drawer>
  )
}

export default SideDrawer
