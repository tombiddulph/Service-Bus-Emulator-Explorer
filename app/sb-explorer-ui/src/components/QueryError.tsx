import { Alert, Button, Text } from '@mantine/core'
import { apiErrorMessage } from '../utils/apiError'

export default function QueryError({ error, stale = false, retrying = false, onRetry }: {
  error?: Error | null; stale?: boolean; retrying?: boolean; onRetry: () => void
}) {
  if (!error) return null
  return <Alert color="red" title={stale ? 'Refresh failed — showing stale data' : 'Unable to load resources'}>
    <Text size="sm">{apiErrorMessage(error)}</Text>
    <Button mt="xs" size="xs" color="red" variant="light" loading={retrying} onClick={onRetry}>Retry</Button>
  </Alert>
}
