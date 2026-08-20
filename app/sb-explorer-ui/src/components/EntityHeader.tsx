import { ActionIcon, Badge, Group, Paper, Title, Tooltip } from '@mantine/core'
import { IconPlus, IconSend, IconTrash } from '@tabler/icons-react'
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

const EntityHeader = ({
  name,
  type,
  status,
  activeCount,
  deadLetterCount,
  activeCountIsExact,
  deadLetterCountIsExact,
  onSend,
  onDelete,
  onCreateSubscription,
}: EntityHeaderProps) => {
  return (
    <Paper withBorder shadow="sm" radius="lg" p="md" mb="md" style={{ color: 'var(--mantine-color-text)' }}>
      <Group justify="space-between" align="center">
        <Group gap={8} align="center">
          <Title order={3}>{name}</Title>
          <StatusPill status={status} />
          <Badge variant="light" color="blue" radius="sm">
            {type}
          </Badge>
          <Group gap={6} align="center">
            {activeCount !== undefined && (
              <Tooltip label={messageCountTooltip(activeCountIsExact)} disabled={activeCountIsExact !== false}>
                <Badge variant="outline" color="gray" radius="sm">
                  Active: {formatMessageCount(activeCount, activeCountIsExact)}
                </Badge>
              </Tooltip>
            )}
            {deadLetterCount !== undefined && (
              <Tooltip label={messageCountTooltip(deadLetterCountIsExact)} disabled={deadLetterCountIsExact !== false}>
                <Badge variant="outline" color="red" radius="sm">
                  DLQ: {formatMessageCount(deadLetterCount, deadLetterCountIsExact)}
                </Badge>
              </Tooltip>
            )}
          </Group>
        </Group>

        <Group gap={8} align="center">
          {type !== 'subscription' && onCreateSubscription && (
            <Tooltip label="Create subscription">
              <ActionIcon variant="light" color="blue" aria-label="Create subscription" onClick={onCreateSubscription}>
                <IconPlus size={18} />
              </ActionIcon>
            </Tooltip>
          )}
          {onSend && (
            <Tooltip label="Send message">
              <ActionIcon variant="light" color="green" aria-label="Send message" onClick={onSend}>
                <IconSend size={18} />
              </ActionIcon>
            </Tooltip>
          )}
          {onDelete && (
            <Tooltip label="Delete">
              <ActionIcon variant="light" color="red" aria-label="Delete" onClick={onDelete}>
                <IconTrash size={18} />
              </ActionIcon>
            </Tooltip>
          )}
        </Group>
      </Group>
    </Paper>
  )
}

export default EntityHeader
