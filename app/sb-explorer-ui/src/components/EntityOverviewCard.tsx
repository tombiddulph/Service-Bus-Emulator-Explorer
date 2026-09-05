import { SimpleGrid, Text } from '@mantine/core'

interface OverviewCardProps {
  title: string
  items: { label: string; value?: string | number | null }[]
}

const EntityOverviewCard = ({ title, items }: OverviewCardProps) => (
  <section className="portal-essentials" aria-label={title}>
    <Text fw={600} mb="sm">{title === 'Properties' ? 'Essentials' : title}</Text>
    <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xl" verticalSpacing={8}>
      {items.map(item => <dl className="portal-property" key={item.label} style={{ margin: 0 }}><dt>{item.label}</dt><dd>{item.value ?? '—'}</dd></dl>)}
    </SimpleGrid>
  </section>
)

export default EntityOverviewCard
