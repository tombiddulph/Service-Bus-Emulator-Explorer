import { Drawer, Stack } from '@mantine/core'
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
      title={title}
      withOverlay
      overlayProps={{ opacity: 0.12, style: { top: 48 } }}
      styles={{ inner: { top: 48, height: 'calc(100% - 48px)' }, content: { height: '100%' } }}
      closeButtonProps={{ 'aria-label': 'Close message details' }}
      classNames={{
        content: 'portal-blade',
        header: 'portal-blade-header',
      }}
    >
      <Stack gap="md">
        {children}
      </Stack>
    </Drawer>
  )
}

export default SideDrawer
