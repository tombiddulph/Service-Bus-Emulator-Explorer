import { Tabs } from '@mantine/core'
import type { MessageState } from '../api/types'

interface MessageTabsProps {
  state: MessageState
  onChange: (next: MessageState) => void
  activeCount?: number
  deadLetterCount?: number
}

const MessageTabs = ({ state, onChange, activeCount, deadLetterCount }: MessageTabsProps) => {
  return (
    <Tabs value={state} onChange={(value) => onChange((value as MessageState) ?? 'active')} variant="pills" radius="md">
      <Tabs.List>
        <Tabs.Tab value="active">Active ({activeCount ?? 0})</Tabs.Tab>
        <Tabs.Tab value="deadletter">Dead-letter ({deadLetterCount ?? 0})</Tabs.Tab>
      </Tabs.List>
    </Tabs>
  )
}

export default MessageTabs
