import { Card, Group, SimpleGrid, Text } from '@mantine/core'

interface OverviewCardProps {
  title: string
  items: { label: string; value?: string | number | null }[]
}

const EntityOverviewCard = ({ title, items }: OverviewCardProps) => {
  return (
    <Card withBorder shadow="sm" radius="lg" padding="md" style={{ color: 'var(--mantine-color-text)' }}>
      <Group mb="xs" justify="space-between">
        <Text fw={700}>{title}</Text>
      </Group>
      <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }} spacing="sm">
        {items.map((item) => (
          <Card key={item.label} radius="md" padding="sm" withBorder>
            <Text size="xs" c="dimmed" mb={4}>
              {item.label}
            </Text>
            <Text>{item.value ?? '—'}</Text>
          </Card>
        ))}
      </SimpleGrid>
    </Card>
  )
}

export default EntityOverviewCard
