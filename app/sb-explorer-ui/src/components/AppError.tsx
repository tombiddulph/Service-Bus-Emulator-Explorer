import { Button, Card, Stack, Text, Title } from '@mantine/core'
import { useNavigate, useRouteError } from 'react-router-dom'

const AppError = () => {
  const error = useRouteError() as any
  const navigate = useNavigate()

  return (
    <Card withBorder shadow="sm" radius="lg" padding="lg" mt="md" mx="auto" maw={640}>
      <Stack gap="sm">
        <Title order={4}>Something went wrong</Title>
        <Text>
          {error?.statusText || error?.message || 'Unexpected application error.'}
        </Text>
        <Button onClick={() => navigate('/')}>Return home</Button>
      </Stack>
    </Card>
  )
}

export default AppError
