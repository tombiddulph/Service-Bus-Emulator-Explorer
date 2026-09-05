import { useMemo } from 'react'
import type { ReactNode } from 'react'
import { ActionIcon, Checkbox, Group, Paper, ScrollArea, Table, Text, Tooltip } from '@mantine/core'
import { IconArrowLeft, IconArrowRight } from '@tabler/icons-react'
import type { MessageInfo, MessageState, PagedResult } from '../api/types'

interface MessageGridProps {
  messages?: PagedResult<MessageInfo>
  loading?: boolean
  state: MessageState
  skip: number
  take: number
  onPageChange: (nextSkip: number) => void
  selectedIds: string[]
  onSelectionChange: (ids: string[]) => void
  onInspect?: (message: MessageInfo) => void
}

type Column<T> = {
  id: string
  label: string
  render: (item: T) => ReactNode
}

const columns: Column<MessageInfo>[] = [
  {
    id: 'subject',
    label: 'Body',
    render: (item) => item.bodyPreview,
  },
  {
    id: 'messageId',
    label: 'Message ID',
    render: (item) => item.messageId,
  },
  {
    id: 'enqueued',
    label: 'Enqueued',
    render: (item) => (item.enqueuedTime ? new Date(item.enqueuedTime).toLocaleString() : '—'),
  },
  {
    id: 'delivery',
    label: 'Delivery count',
    render: (item) => item.deliveryCount ?? 0,
  },
  {
    id: 'session',
    label: 'Session',
    render: (item) => item.sessionId ?? '—',
  },
]

const MessageGrid = ({
  messages,
  loading,
  state,
  skip,
  take,
  onPageChange,
  selectedIds,
  onSelectionChange,
  onInspect,
}: MessageGridProps) => {
  const items = useMemo(() => messages?.items ?? [], [messages?.items])
  const hasNext = messages?.hasMore || (messages?.total ? skip + take < messages.total : items.length === take)
  const canPrev = skip > 0

  const toggleSelection = (id: string) => {
    if (selectedIds.includes(id)) onSelectionChange(selectedIds.filter((x) => x !== id))
    else onSelectionChange([...selectedIds, id])
  }

  const allSelected = useMemo(() => items.length > 0 && items.every((m) => selectedIds.includes(m.messageId)), [items, selectedIds])

  const toggleSelectAll = () => {
    if (allSelected) onSelectionChange([])
    else onSelectionChange(items.map((m) => m.messageId))
  }

  const start = items.length ? skip + 1 : 0
  const end = skip + items.length

  return (
    <Paper radius={0} p={0} style={{ color: 'var(--mantine-color-text)' }}>
      <Group align="center" gap="xs" mb="sm" className="portal-command-bar">
        <Tooltip label="Previous">
          <ActionIcon
            variant="light"
            color="gray"
            aria-label="Previous"
            disabled={!canPrev || loading}
            onClick={() => onPageChange(Math.max(0, skip - take))}
          >
            <IconArrowLeft size={18} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label="Next">
          <ActionIcon
            variant="light"
            color="gray"
            aria-label="Next"
            disabled={!hasNext || loading}
            onClick={() => onPageChange(skip + take)}
          >
            <IconArrowRight size={18} />
          </ActionIcon>
        </Tooltip>
        <Text c="dimmed" size="sm">
          Showing {start} - {end}
        </Text>
      </Group>

      <ScrollArea>
        <Table
          className="portal-table"
          highlightOnHover
          verticalSpacing="xs"
          horizontalSpacing="md"
          striped={false}
          withRowBorders
          miw={700}
          styles={{ th: { color: 'inherit' }, td: { color: 'inherit' } }}
        >
          <Table.Thead>
            <Table.Tr>
              <Table.Th>
                <Checkbox aria-label="Select all messages on this page" checked={allSelected} indeterminate={!allSelected && items.some(item => selectedIds.includes(item.messageId))} onChange={() => toggleSelectAll()} />
              </Table.Th>
              {columns.map((column) => (
                <Table.Th key={column.id}>{column.label}</Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {items.length === 0 && <Table.Tr><Table.Td colSpan={columns.length + 1}>
              <Text size="sm" c="dimmed" ta="center" py="xl" role="status">
                {loading ? 'Loading messages…' : skip > 0 ? 'No messages on this page. Go back to the previous page.' : state === 'deadletter' ? 'No dead-letter messages.' : 'No active messages.'}
              </Text>
            </Table.Td></Table.Tr>}
            {items.map((item) => (
              <Table.Tr key={item.messageId} style={{ cursor: onInspect ? 'pointer' : 'default' }} onClick={() => onInspect?.(item)}>
                <Table.Td>
                  <Checkbox
                    aria-label={`Select message ${item.messageId}`}
                    checked={selectedIds.includes(item.messageId)}
                    onClick={(e) => e.stopPropagation()}
                    onChange={() => toggleSelection(item.messageId)}
                  />
                </Table.Td>
                {columns.map((column) => (
                  <Table.Td key={column.id}>{column.id === 'messageId' && onInspect
                    ? <button className="portal-link-button" onClick={event => { event.stopPropagation(); onInspect(item) }}>{item.messageId}</button>
                    : column.render(item)}</Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </ScrollArea>
    </Paper>
  )
}

export default MessageGrid
