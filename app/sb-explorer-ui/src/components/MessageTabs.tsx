import { Tabs } from '@mantine/core'
import type { MessageState } from '../api/types'
import { formatMessageCount, messageCountTooltip } from '../utils/formatCount'

interface MessageTabsProps {
  state: MessageState
  onChange: (next: MessageState) => void
  activeCount?: number
  deadLetterCount?: number
  activeCountIsExact?: boolean
  deadLetterCountIsExact?: boolean
}

const MessageTabs = ({ state, onChange, activeCount, deadLetterCount, activeCountIsExact, deadLetterCountIsExact }: MessageTabsProps) => {
  return (
    <Tabs value={state} onChange={(value) => onChange((value as MessageState) ?? 'active')}>
      <Tabs.List>
        <Tabs.Tab value="active" title={messageCountTooltip(activeCountIsExact)}>Active ({formatMessageCount(activeCount, activeCountIsExact)})</Tabs.Tab>
        <Tabs.Tab value="deadletter" title={messageCountTooltip(deadLetterCountIsExact)}>Dead-letter ({formatMessageCount(deadLetterCount, deadLetterCountIsExact)})</Tabs.Tab>
      </Tabs.List>
    </Tabs>
  )
}

export default MessageTabs
