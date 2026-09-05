import { useMemo, useState } from 'react'
import { Button, Checkbox, Divider, Group, Stack, Text } from '@mantine/core'
import type { MessageInfo } from '../api/types'
import MessageBodyEditor from './MessageBodyEditor'
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

// Callers must remount this component (e.g. key={message?.messageId}) when the inspected message
// changes, so this initial state isn't stale for a different message with the same body.
const MessageDetailPanel = ({ message, open, onOpenChange, editable = false, onSaveAndRequeue }: MessageDetailPanelProps) => {
  const { theme } = useAppContext()
  const bodyValue = useMemo(() => message?.body ?? message?.bodyPreview ?? '', [message])
  const [editBody, setEditBody] = useState(bodyValue)
  const [editing, setEditing] = useState(false)
  const [removeFromDlq, setRemoveFromDlq] = useState(true)

  return (
    <SideDrawer open={open} onOpenChange={onOpenChange} title="Message details" width={640}>
      <Text size="sm" style={{ overflowWrap: 'anywhere' }}>Message ID: {message?.messageId ?? '—'}</Text>
      <Stack gap="xs">
        <Text size="sm" c="dimmed">Content Type: {message?.contentType ?? '—'}</Text>
        <Text size="sm" c="dimmed">Enqueued: {message?.enqueuedTime ? new Date(message.enqueuedTime).toLocaleString() : '—'}</Text>
        <Text size="sm" c="dimmed">Delivery count: {message?.deliveryCount ?? 0}</Text>
      </Stack>
      <Divider my="sm" />
      {editable && !editing && <Button variant="light" onClick={() => setEditing(true)}>Edit</Button>}
      {editable && editing && (
        <Checkbox
          mt="sm"
          label="Remove from DLQ after successful replay"
          checked={removeFromDlq}
          onChange={(event) => setRemoveFromDlq(event.currentTarget.checked)}
        />
      )}
      <Text fw={600} size="sm">Message body</Text>
      <div>
        <MessageBodyEditor
          value={editing ? editBody : bodyValue}
          onChange={setEditBody}
          language="json"
          theme={theme}
          readOnly={!editing}
          height={300}
        />
      </div>

      <MessagePropertiesTable title="User properties" data={message?.userProperties as Record<string, unknown> | undefined} />
      <MessagePropertiesTable title="System properties" data={message?.systemProperties as Record<string, unknown> | undefined} />
      {editable && editing && (
        <Group justify="flex-end" mt="md">
          <Button
            variant="default"
            onClick={() => {
              setEditBody(bodyValue)
              setEditing(false)
            }}
          >
            Cancel
          </Button>
          <Button onClick={() => onSaveAndRequeue?.(editBody, removeFromDlq)}>Save &amp; Requeue</Button>
        </Group>
      )}
    </SideDrawer>
  )
}

export default MessageDetailPanel

