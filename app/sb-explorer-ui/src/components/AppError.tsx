import { Button, Card, Stack, Text, Title } from '@mantine/core'
import { isRouteErrorResponse, useNavigate, useRouteError } from 'react-router-dom'

const AppError = () => {
  const error = useRouteError()
  const navigate = useNavigate()
  const message = (isRouteErrorResponse(error) ? error.statusText : error instanceof Error ? error.message : '')
    || 'Unexpected application error.'

  return (
    <Card withBorder shadow="sm" radius="lg" padding="lg" mt="md" mx="auto" maw={640}>
      <Stack gap="sm">
        <Title order={4}>Something went wrong</Title>
        <Text>
          {message}
        </Text>
        <Button onClick={() => navigate('/')}>Return home</Button>
      </Stack>
    </Card>
  )
}

export default AppError
