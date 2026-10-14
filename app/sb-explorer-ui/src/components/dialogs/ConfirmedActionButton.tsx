import { useState } from 'react'
import { Button, Group, TextInput } from '@mantine/core'
import type { ButtonProps } from '@mantine/core'
import ConfirmActionDialog from './ConfirmActionDialog'
import { useReadOnly } from '../../api/hooks'

export default function ConfirmedActionButton({ entity, description, onClick, children, ...props }: ButtonProps & {
  entity: string; description: string; onClick: () => void
}) {
  const [open, setOpen] = useState(false)
  const [typedName, setTypedName] = useState('')
  const readOnly = useReadOnly()
  return <>
    <Button {...props} disabled={props.disabled || readOnly} onClick={() => { setTypedName(''); setOpen(true) }}>{children}</Button>
    <ConfirmActionDialog open={open} onOpenChange={setOpen} title={`Confirm action on ${entity}`} description={description}>
      <TextInput label="Type entity name to confirm" value={typedName} onChange={event => setTypedName(event.currentTarget.value)} />
      <Group justify="flex-end">
        <Button variant="default" onClick={() => setOpen(false)}>Cancel</Button>
        <Button color="red" disabled={typedName !== entity || readOnly || props.disabled} onClick={() => { setOpen(false); onClick() }}>Confirm</Button>
      </Group>
    </ConfirmActionDialog>
  </>
}
