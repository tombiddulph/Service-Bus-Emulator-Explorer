import { useMemo } from 'react'
import { Divider, Stack, Text } from '@mantine/core'
import Editor from '@monaco-editor/react'
import type { MessageInfo } from '../api/types'
import MessagePropertiesTable from './MessagePropertiesTable'
import { useAppContext } from '../App'
import SideDrawer from './SideDrawer'

interface MessageDetailPanelProps {
  message?: MessageInfo
  open: boolean
  onOpenChange: (open: boolean) => void
}

const MessageDetailPanel = ({ message, open, onOpenChange }: MessageDetailPanelProps) => {
  const { theme } = useAppContext()
  const bodyValue = useMemo(() => message?.body ?? message?.bodyPreview ?? '', [message])
  const monacoTheme = theme === 'dark' ? 'vs-dark' : 'vs'

  return (
    <SideDrawer open={open} onOpenChange={onOpenChange} title={message?.messageId ?? 'Message'} width={640}>
      <Stack gap="xs">
        <Text size="sm" c="dimmed">Content Type: {message?.contentType ?? '—'}</Text>
        <Text size="sm" c="dimmed">Enqueued: {message?.enqueuedTime ? new Date(message.enqueuedTime).toLocaleString() : '—'}</Text>
        <Text size="sm" c="dimmed">Delivery count: {message?.deliveryCount ?? 0}</Text>
      </Stack>
      <Divider my="sm" />
      <div style={{ height: 260, border: '1px solid var(--surface-border, #ddd)', borderRadius: 8, overflow: 'hidden' }}>
        <Editor
          height="100%"
          defaultLanguage="json"
          value={bodyValue}
          theme={monacoTheme}
          options={{ readOnly: true, minimap: { enabled: false }, lineNumbers: 'off', wordWrap: 'on' }}
        />
      </div>

      <MessagePropertiesTable title="User properties" data={message?.userProperties as Record<string, unknown> | undefined} />
      <MessagePropertiesTable title="System properties" data={message?.systemProperties as Record<string, unknown> | undefined} />
    </SideDrawer>
  )
}

export default MessageDetailPanel
