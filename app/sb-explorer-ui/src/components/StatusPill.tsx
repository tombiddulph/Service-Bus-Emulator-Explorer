import { Badge } from '@mantine/core'
import type { EntityStatus } from '../api/types'

const statusColor: Record<EntityStatus, string> = {
  Active: 'green',
  Disabled: 'red',
  SendDisabled: 'yellow',
  ReceiveDisabled: 'yellow',
}

const StatusPill = ({ status }: { status: EntityStatus }) => {
  return (
    <Badge color={statusColor[status]} variant="light" radius="sm" tt="capitalize" size="sm">
      {status}
    </Badge>
  )
}

export default StatusPill
