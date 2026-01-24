import { useEffect, useMemo, useState } from 'react'
import { Button, Group, Modal, Stack, TextInput, Title } from '@mantine/core'
import Editor from '@monaco-editor/react'
import MessagePropertiesTable from '../MessagePropertiesTable'

interface SendMessageDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (payload: { body: string; contentType?: string; userProperties?: Record<string, unknown> }) => void
  theme: 'light' | 'dark'
}

const SendMessageDialog = ({ open, onOpenChange, onSubmit, theme }: SendMessageDialogProps) => {
  const [body, setBody] = useState('{}')
  const [contentType, setContentType] = useState('application/json')
  const [userProps, setUserProps] = useState<Record<string, string>>({})
  const [kvKey, setKvKey] = useState('')
  const [kvValue, setKvValue] = useState('')

  useEffect(() => {
    if (!open) {
      setBody('{}')
      setUserProps({})
      setKvKey('')
      setKvValue('')
    }
  }, [open])

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
      } catch (_err) {
        alert('Body must be valid JSON')
        return
      }
    }
    onSubmit({ body, contentType, userProperties: parsedUserProps })
  }

  return (
    <Modal opened={open} onClose={() => onOpenChange(false)} title={<Title order={4}>Send message</Title>} size="lg" centered>
      <Stack gap="sm">
        <TextInput label="Content type" value={contentType} onChange={(e) => setContentType(e.currentTarget.value)} />
        <Stack gap={4}>
          <TextInput label="Body" value={body} onChange={(e) => setBody(e.currentTarget.value)} />
          <div style={{ height: 260, border: '1px solid var(--mantine-color-gray-4)', borderRadius: 8, overflow: 'hidden', background: 'var(--mantine-color-body)' }}>
            <Editor
              height="100%"
              defaultLanguage={contentType.includes('json') ? 'json' : 'plaintext'}
              value={body}
              onChange={(value) => setBody(value ?? '')}
              theme={theme === 'dark' ? 'vs-dark' : 'vs'}
              options={{ minimap: { enabled: false }, lineNumbers: 'off', wordWrap: 'on' }}
            />
          </div>
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
