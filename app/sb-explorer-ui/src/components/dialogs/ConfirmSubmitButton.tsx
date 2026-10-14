import { useContext } from 'react'
import { Button } from '@mantine/core'
import type { ButtonProps } from '@mantine/core'
import { ConfirmationContext } from './ConfirmationContext'
import { useReadOnly } from '../../api/hooks'

export default function ConfirmSubmitButton(props: ButtonProps & { onClick?: () => void }) {
  const confirmed = useContext(ConfirmationContext)
  const readOnly = useReadOnly()
  return <Button {...props} disabled={props.disabled || !confirmed || readOnly} />
}
