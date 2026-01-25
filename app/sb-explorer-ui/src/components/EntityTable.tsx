import type { ReactNode } from 'react'
import { ActionIcon, Button, Group, Paper, ScrollArea, Table, Text, Tooltip } from '@mantine/core'
import { IconPlus, IconRefresh } from '@tabler/icons-react'
import type { EntityStatus } from '../api/types'
import StatusPill from './StatusPill'

export interface EntityRow {
  name: string
  status: EntityStatus
  activeMessageCount: number
  deadLetterMessageCount: number
  scheduledMessageCount?: number
  createdAt?: string
}

interface EntityTableProps<T extends EntityRow> {
  title: string
  items?: T[]
  loading?: boolean
  onRefresh?: () => void
  onCreate?: () => void
  onRowClick?: (item: T) => void
  emptyState?: ReactNode
}

type Column<T> = {
  id: string
  label: string
  render: (item: T) => ReactNode
}

const columns: Column<EntityRow>[] = [
  {
    id: 'name',
    label: 'Name',
    render: (item) => <Text fw={600}>{item.name}</Text>,
  },
  {
    id: 'status',
    label: 'Status',
    render: (item) => <StatusPill status={item.status} />, 
  },
  {
    id: 'active',
    label: 'Active',
    render: (item) => item.activeMessageCount ?? 0,
  },
  {
    id: 'dlq',
    label: 'Dead-letter',
    render: (item) => item.deadLetterMessageCount ?? 0,
  },
  {
    id: 'scheduled',
    label: 'Scheduled',
    render: (item) => item.scheduledMessageCount ?? 0,
  },
  {
    id: 'created',
    label: 'Created',
    render: (item) => (item.createdAt ? new Date(item.createdAt).toLocaleString() : '—'),
  },
]

const EntityTable = <T extends EntityRow>({
  title,
  items,
  loading,
  onCreate,
  onRefresh,
  onRowClick,
  emptyState,
}: EntityTableProps<T>) => {
  const list: T[] = Array.isArray(items) ? items : items ? Object.values(items as any) : []

  return (
    <Paper
      withBorder
      shadow="sm"
      radius="lg"
      p="md"
      bg="var(--mantine-color-body)"
      style={{ color: 'var(--mantine-color-text)' }}
    >
      <Group justify="space-between" align="center" mb="sm">
        <Group gap={6} align="center">
          <Tooltip label="Refresh">
            <ActionIcon variant="light" color="gray" aria-label="Refresh" onClick={onRefresh} disabled={loading}>
              <IconRefresh size={18} />
            </ActionIcon>
          </Tooltip>
          <Text fw={700}>{title}</Text>
        </Group>
        {onCreate && (
          <Button leftSection={<IconPlus size={16} />} onClick={onCreate} variant="light">
            Create
          </Button>
        )}
      </Group>

      <ScrollArea>
        <Table
          highlightOnHover
          verticalSpacing="sm"
          horizontalSpacing="md"
          striped={false}
          withRowBorders={false}
          miw={600}
          styles={{ th: { color: 'inherit' }, td: { color: 'inherit' } }}
        >
          <Table.Thead>
            <Table.Tr>
              {columns.map((column) => (
                <Table.Th key={column.id}>{column.label}</Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {list.map((item) => (
              <Table.Tr
                key={item.name}
                onClick={() => onRowClick?.(item)}
                style={{ cursor: onRowClick ? 'pointer' : 'default' }}
              >
                {columns.map((column) => (
                  <Table.Td key={column.id}>{column.render(item)}</Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </ScrollArea>

      {!loading && list.length === 0 && emptyState && <div style={{ marginTop: 12 }}>{emptyState}</div>}
    </Paper>
  )
}

export default EntityTable
