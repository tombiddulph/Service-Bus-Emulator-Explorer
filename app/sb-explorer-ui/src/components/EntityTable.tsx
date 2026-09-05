import { useState, type ReactNode } from 'react'
import { Button, Group, Loader, ScrollArea, Table, Text, TextInput, Tooltip } from '@mantine/core'
import { IconMessageDots, IconPlus, IconRefresh, IconSearch, IconTopologyStar } from '@tabler/icons-react'
import type { EntityStatus } from '../api/types'
import { formatMessageCount, messageCountTooltip } from '../utils/formatCount'
import StatusPill from './StatusPill'

export interface EntityRow {
  name: string
  status: EntityStatus
  activeMessageCount: number
  deadLetterMessageCount: number
  activeMessageCountIsExact?: boolean
  deadLetterMessageCountIsExact?: boolean
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
    render: (item) => (
      <Tooltip label={messageCountTooltip(item.activeMessageCountIsExact)} disabled={item.activeMessageCountIsExact !== false}>
        <span>{formatMessageCount(item.activeMessageCount, item.activeMessageCountIsExact)}</span>
      </Tooltip>
    ),
  },
  {
    id: 'dlq',
    label: 'Dead-letter',
    render: (item) => (
      <Tooltip label={messageCountTooltip(item.deadLetterMessageCountIsExact)} disabled={item.deadLetterMessageCountIsExact !== false}>
        <span>{formatMessageCount(item.deadLetterMessageCount, item.deadLetterMessageCountIsExact)}</span>
      </Tooltip>
    ),
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
  const [filter, setFilter] = useState('')
  const list = (items ?? []).filter(item => item.name.toLowerCase().includes(filter.toLowerCase()))
  const Icon = title === 'Queues' ? IconMessageDots : IconTopologyStar

  return (
    <section aria-label={title}>
      <div className="portal-resource-heading">
        <Icon size={36} stroke={1.4} className="portal-resource-icon" />
        <div><h1>{title}</h1><Text size="xs" c="dimmed">Service Bus · Emulator workspace</Text></div>
      </div>
      <div className="portal-command-bar" role="toolbar" aria-label={`${title} commands`}>
        {onCreate && (
          <Button leftSection={<IconPlus size={16} />} onClick={onCreate} variant="subtle">
            Create
          </Button>
        )}
        {onRefresh && <Button variant="subtle" leftSection={<IconRefresh size={16} />} onClick={onRefresh} disabled={loading}>Refresh</Button>}
      </div>
      <Group my="md" gap="md">
        <TextInput aria-label={`Filter ${title.toLowerCase()}`} placeholder="Filter by name" size="xs" w={280} maw="100%" leftSection={<IconSearch size={14} />} value={filter} onChange={e => setFilter(e.currentTarget.value)} />
        <Text size="xs" c="dimmed">{list.length} of {items?.length ?? 0} items</Text>
        {loading && <Loader size="xs" aria-label="Loading resources" />}
      </Group>

      <ScrollArea>
        <Table
          className="portal-table"
          highlightOnHover
          verticalSpacing="xs"
          horizontalSpacing="md"
          striped={false}
          withRowBorders
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
                  <Table.Td key={column.id}>{column.id === 'name' && onRowClick
                    ? <button className="portal-link-button" onClick={event => { event.stopPropagation(); onRowClick(item) }}>{item.name}</button>
                    : column.render(item)}</Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </ScrollArea>

      {!loading && list.length === 0 && <div style={{ padding: '24px 0' }}>{filter ? <Text size="sm" c="dimmed">No resources match “{filter}”.</Text> : emptyState}</div>}
    </section>
  )
}

export default EntityTable
