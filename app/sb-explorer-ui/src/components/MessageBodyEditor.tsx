import Editor from '@monaco-editor/react'

interface MessageBodyEditorProps {
  value: string
  onChange?: (value: string) => void
  language?: string
  theme: 'light' | 'dark'
  readOnly?: boolean
  height?: number
}

// Shared body editor so Send Message and the message detail/replay views stay in sync.
const MessageBodyEditor = ({ value, onChange, language = 'plaintext', theme, readOnly = false, height = 260 }: MessageBodyEditorProps) => (
  <div style={{ height, border: '1px solid var(--portal-border)', overflow: 'hidden', background: 'var(--mantine-color-body)' }}>
    <Editor
      height="100%"
      language={language}
      value={value}
      onChange={(next) => onChange?.(next ?? '')}
      theme={theme === 'dark' ? 'vs-dark' : 'vs'}
      options={{ readOnly, minimap: { enabled: false }, lineNumbers: 'off', wordWrap: 'on' }}
    />
  </div>
)

export default MessageBodyEditor
