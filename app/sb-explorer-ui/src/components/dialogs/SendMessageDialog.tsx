import { useMemo, useState } from 'react'
import { Button, Group, Modal, Stack, Text, TextInput, Title } from '@mantine/core'
import MessageBodyEditor from '../MessageBodyEditor'
import MessagePropertiesTable from '../MessagePropertiesTable'

interface SendMessageDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (payload: { body: string; contentType?: string; userProperties?: Record<string, unknown>; sessionId?: string }) => void
  theme: 'light' | 'dark'
}

const SendMessageDialog = ({ open, onOpenChange, onSubmit, theme }: SendMessageDialogProps) => {
  const [body, setBody] = useState('{}')
  const [contentType, setContentType] = useState('application/json')
  const [sessionId, setSessionId] = useState('')
  const [userProps, setUserProps] = useState<Record<string, string>>({})
  const [kvKey, setKvKey] = useState('')
  const [kvValue, setKvValue] = useState('')

  // Reset fields the moment `open` flips to false, however it happened (Cancel, backdrop,
  // Escape, or the parent closing after a successful send). Adjusting state during render
  // (rather than in an effect) avoids the extra setState-in-effect render cascade.
  const [wasOpen, setWasOpen] = useState(open)
  if (open !== wasOpen) {
    setWasOpen(open)
    if (!open) {
      setBody('{}')
      setSessionId('')
      setUserProps({})
      setKvKey('')
      setKvValue('')
    }
  }

  const parsedUserProps = useMemo(() => {
    const entries = Object.entries(userProps)
    const result: Record<string, string> = {}
    entries.forEach(([k, v]) => {
      if (k) result[k] = v
    })
    return result
  }, [userProps])

  const handleAddKv = () => {
    if (!kvKey) return
    setUserProps((prev) => ({ ...prev, [kvKey]: kvValue }))
    setKvKey('')
    setKvValue('')
  }

  const handleDeleteKv = (key: string) => {
    setUserProps((prev) => {
      const next = { ...prev }
      delete next[key]
      return next
    })
  }

  const handleSubmit = () => {
    if (contentType.includes('json')) {
      try {
        JSON.parse(body || '{}')
      } catch (err) {
        // JSON.parse always throws a SyntaxError on invalid input.
        alert(`Body must be valid JSON: ${(err as SyntaxError).message}`)
        return
      }
    }
    onSubmit({ body, contentType, userProperties: parsedUserProps, sessionId: sessionId || undefined })
  }

  return (
    <Modal opened={open} onClose={() => onOpenChange(false)} title={<Title order={4}>Send message</Title>} size="lg" centered>
      <Stack gap="sm">
        <TextInput label="Content type" value={contentType} onChange={(e) => setContentType(e.currentTarget.value)} />
        <TextInput
          label="Session ID"
          description="Required for session-enabled queues/subscriptions"
          value={sessionId}
          onChange={(e) => setSessionId(e.currentTarget.value)}
        />
        <Stack gap={4}>
          <Text size="sm" fw={500}>Body</Text>
          <MessageBodyEditor
            value={body}
            onChange={setBody}
            language={contentType.includes('json') ? 'json' : 'plaintext'}
            theme={theme}
          />
        </Stack>

        <Stack gap="xs">
          <Group gap="xs">
            <TextInput placeholder="Key" value={kvKey} onChange={(e) => setKvKey(e.currentTarget.value)} flex={1} />
            <TextInput placeholder="Value" value={kvValue} onChange={(e) => setKvValue(e.currentTarget.value)} flex={1} />
            <Button onClick={handleAddKv} disabled={!kvKey}>
              Add
            </Button>
          </Group>
          <MessagePropertiesTable title="Application properties" data={parsedUserProps} />
          {Object.keys(parsedUserProps).length > 0 && (
            <Group gap="xs" wrap="wrap">
              {Object.keys(parsedUserProps).map((k) => (
                <Button key={k} variant="subtle" size="xs" onClick={() => handleDeleteKv(k)}>
                  Remove {k}
                </Button>
              ))}
            </Group>
          )}
        </Stack>

        <Group justify="flex-end" mt="sm">
          <Button variant="default" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button onClick={handleSubmit} disabled={!body}>
            Send
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}

export default SendMessageDialog
