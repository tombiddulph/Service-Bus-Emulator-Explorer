import { useEffect, useMemo, useState } from 'react'
import { Button, Checkbox, Divider, Group, Stack, Text, Textarea } from '@mantine/core'
import Editor from '@monaco-editor/react'
import type { MessageInfo } from '../api/types'
import MessagePropertiesTable from './MessagePropertiesTable'
import { useAppContext } from '../App'
import SideDrawer from './SideDrawer'

interface MessageDetailPanelProps {
  message?: MessageInfo
  open: boolean
  onOpenChange: (open: boolean) => void
  editable?: boolean
  onSaveAndRequeue?: (body: string, removeFromDlq: boolean) => void
}

const MessageDetailPanel = ({ message, open, onOpenChange, editable = false, onSaveAndRequeue }: MessageDetailPanelProps) => {
  const { theme } = useAppContext()
  const bodyValue = useMemo(() => message?.body ?? message?.bodyPreview ?? '', [message])
  const [editBody, setEditBody] = useState(bodyValue)
  const [editing, setEditing] = useState(false)
  const [removeFromDlq, setRemoveFromDlq] = useState(true)
  const monacoTheme = theme === 'dark' ? 'vs-dark' : 'vs'

  useEffect(() => {
    setEditBody(bodyValue)
    setEditing(false)
    setRemoveFromDlq(true)
  }, [bodyValue])

  return (
    <SideDrawer open={open} onOpenChange={onOpenChange} title={message?.messageId ?? 'Message'} width={640}>
      <Stack gap="xs">
        <Text size="sm" c="dimmed">Content Type: {message?.contentType ?? '—'}</Text>
        <Text size="sm" c="dimmed">Enqueued: {message?.enqueuedTime ? new Date(message.enqueuedTime).toLocaleString() : '—'}</Text>
        <Text size="sm" c="dimmed">Delivery count: {message?.deliveryCount ?? 0}</Text>
      </Stack>
      <Divider my="sm" />
      {editable && !editing && <Button variant="light" onClick={() => setEditing(true)}>Edit</Button>}
      {editable && editing && <Textarea label="Message body" minRows={8} value={editBody} onChange={(event) => setEditBody(event.currentTarget.value)} />}
      {editable && editing && (
        <Checkbox
          mt="sm"
          label="Remove from DLQ after successful replay"
          checked={removeFromDlq}
          onChange={(event) => setRemoveFromDlq(event.currentTarget.checked)}
        />
      )}
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
      {editable && editing && (
        <Group justify="flex-end" mt="md">
          <Button onClick={() => onSaveAndRequeue?.(editBody, removeFromDlq)}>Save &amp; Requeue</Button>
        </Group>
      )}
    </SideDrawer>
  )
}

export default MessageDetailPanel
