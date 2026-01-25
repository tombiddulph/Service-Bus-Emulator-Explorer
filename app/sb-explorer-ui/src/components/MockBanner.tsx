import { Alert, Group, Text } from '@mantine/core'
import { apiBaseUrl, isMockEnabled, mockMatrix } from '../api/client'

const MockBanner = () => {
  if (!isMockEnabled) return null
  const mocked = Object.entries(mockMatrix)
    .filter(([, v]) => v)
    .map(([k]) => k)
  const live = Object.entries(mockMatrix)
    .filter(([, v]) => !v)
    .map(([k]) => k)
  return (
    <Alert color="blue" variant="light" radius="md" mb="sm" title="Mock mode">
      <Group gap={6} align="center">
        <Text fw={600}>Mock mode is ON.</Text>
        <Text size="sm" c="dimmed">
          Base URL: {apiBaseUrl}
        </Text>
      </Group>
      <Text size="sm" mt={4}>
        Mocked: {mocked.join(', ') || 'none'} | Live: {live.join(', ') || 'none'}
      </Text>
    </Alert>
  )
}

export default MockBanner
