import { Badge } from '@mantine/core'
import type { EntityStatus } from '../api/types'

const statuses: Record<string, { color: string; label: string }> = {
  active: { color: 'green', label: 'Active' },
  disabled: { color: 'red', label: 'Disabled' },
  senddisabled: { color: 'yellow', label: 'Send disabled' },
  receivedisabled: { color: 'yellow', label: 'Receive disabled' },
}

const StatusPill = ({ status }: { status: EntityStatus }) => {
  const presentation = statuses[status.toLowerCase()] ?? { color: 'gray', label: status }
  return (
    <Badge color={presentation.color} variant="light" radius={2} tt="none" size="sm">
      {presentation.label}
    </Badge>
  )
}

export default StatusPill
