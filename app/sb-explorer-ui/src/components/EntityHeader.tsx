import { ActionIcon, Badge, Group, Paper, Title, Tooltip } from '@mantine/core'
import { IconPlus, IconSend, IconTrash } from '@tabler/icons-react'
import type { EntityStatus } from '../api/types'
import StatusPill from './StatusPill'

interface EntityHeaderProps {
  name: string
  type: 'queue' | 'topic' | 'subscription'
  status: EntityStatus
  activeCount?: number
  deadLetterCount?: number
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
              <Badge variant="outline" color="gray" radius="sm">
                Active: {activeCount}
              </Badge>
            )}
            {deadLetterCount !== undefined && (
              <Badge variant="outline" color="red" radius="sm">
                DLQ: {deadLetterCount}
              </Badge>
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
