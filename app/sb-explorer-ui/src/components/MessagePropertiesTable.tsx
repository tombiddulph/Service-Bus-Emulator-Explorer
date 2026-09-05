import { Group, ScrollArea, Table, Text } from '@mantine/core'

interface PropsTableProps {
  title: string
  data?: Record<string, unknown>
}

const MessagePropertiesTable = ({ title, data }: PropsTableProps) => {
  const entries = data ? Object.entries(data) : []

  return (
    <section aria-label={title}>
      <Group justify="space-between" align="center" mb="xs">
        <Text fw={600}>{title}</Text>
      </Group>
      <ScrollArea.Autosize mah={240} type="auto">
        <Table className="portal-table" striped={false} highlightOnHover withRowBorders verticalSpacing="xs" horizontalSpacing="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Key</Table.Th>
              <Table.Th>Value</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {entries.length === 0 ? (
              <Table.Tr>
                <Table.Td colSpan={2}>
                  <Text c="dimmed" size="sm" fs="italic">
                    None
                  </Text>
                </Table.Td>
              </Table.Tr>
            ) : (
              entries.map(([k, v]) => (
                <Table.Tr key={k}>
                  <Table.Td>
                    <Text size="sm" style={{ overflowWrap: 'anywhere' }}>{k}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" style={{ overflowWrap: 'anywhere' }}>{typeof v === 'object' ? JSON.stringify(v) : String(v)}</Text>
                  </Table.Td>
                </Table.Tr>
              ))
            )}
          </Table.Tbody>
        </Table>
      </ScrollArea.Autosize>
    </section>
  )
}

export default MessagePropertiesTable
