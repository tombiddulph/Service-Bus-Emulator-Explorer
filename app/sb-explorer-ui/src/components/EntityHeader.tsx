import { Button, Group, Text, Tooltip } from '@mantine/core'
import { useQueryClient } from '@tanstack/react-query'
import { IconMessageDots, IconPlus, IconRefresh, IconSend, IconTopologyStar, IconTrash } from '@tabler/icons-react'
import type { EntityStatus } from '../api/types'
import { formatMessageCount, messageCountTooltip } from '../utils/formatCount'
import StatusPill from './StatusPill'

interface EntityHeaderProps {
  name: string
  type: 'queue' | 'topic' | 'subscription'
  status: EntityStatus
  activeCount?: number
  deadLetterCount?: number
  activeCountIsExact?: boolean
  deadLetterCountIsExact?: boolean
  onSend?: () => void
  onDelete?: () => void
  onCreateSubscription?: () => void
}

const EntityHeader = ({ name, type, status, activeCount, deadLetterCount, activeCountIsExact, deadLetterCountIsExact, onSend, onDelete, onCreateSubscription }: EntityHeaderProps) => {
  const queryClient = useQueryClient()
  const Icon = type === 'queue' ? IconMessageDots : IconTopologyStar
  return <section aria-label={`${type} overview`}>
    <div className="portal-resource-heading">
      <Icon size={36} stroke={1.4} className="portal-resource-icon" />
      <div><h1>{name}</h1><Text size="xs" c="dimmed">Service Bus {type}</Text></div>
    </div>
    <div className="portal-command-bar" role="toolbar" aria-label="Resource commands">
      {onSend && <Button variant="subtle" leftSection={<IconSend size={16} />} onClick={onSend}>Send message</Button>}
      {onCreateSubscription && <Button variant="subtle" leftSection={<IconPlus size={16} />} onClick={onCreateSubscription}>Subscription</Button>}
      <Button variant="subtle" leftSection={<IconRefresh size={16} />} onClick={() => queryClient.invalidateQueries()}>Refresh</Button>
      {onDelete && <Button variant="subtle" color="red" leftSection={<IconTrash size={16} />} onClick={onDelete}>Delete</Button>}
    </div>
    <Group gap="xl" pt="sm">
      <StatusPill status={status} />
      {activeCount !== undefined && <Tooltip label={messageCountTooltip(activeCountIsExact)}><Text size="sm">Active messages: <strong>{formatMessageCount(activeCount, activeCountIsExact)}</strong></Text></Tooltip>}
      {deadLetterCount !== undefined && <Tooltip label={messageCountTooltip(deadLetterCountIsExact)}><Text size="sm">Dead-letter messages: <strong>{formatMessageCount(deadLetterCount, deadLetterCountIsExact)}</strong></Text></Tooltip>}
    </Group>
  </section>
}

export default EntityHeader
