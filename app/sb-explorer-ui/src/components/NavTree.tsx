import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { ActionIcon, Badge, Group, Loader, Stack, Text, TextInput, Tooltip } from '@mantine/core'
import { IconChevronDown, IconChevronRight, IconMessageDots, IconSearch, IconTopologyStar, IconTopologyStar3 } from '@tabler/icons-react'
import { useQueues, useSubscriptions, useTopics } from '../api/hooks'
import { formatMessageCount } from '../utils/formatCount'

const ResourceLink = ({ to, name, kind = 'queue', deadLetterCount, deadLetterCountIsExact }: { to: string; name: string; kind?: 'queue' | 'topic' | 'subscription'; deadLetterCount?: number; deadLetterCountIsExact?: boolean }) => {
  const { pathname } = useLocation()
  const Icon = kind === 'queue' ? IconMessageDots : kind === 'topic' ? IconTopologyStar : IconTopologyStar3
  const showCount = (deadLetterCount ?? 0) > 0 || deadLetterCountIsExact === false
  const count = formatMessageCount(deadLetterCount, deadLetterCountIsExact)
  return <Link className="portal-resource-link" to={to} aria-current={pathname === to ? 'page' : undefined} title={name}>
    <Icon size={16} /><span>{name}</span>
    {showCount && <Badge className="portal-nav-dlq" color="red" variant="light" size="xs" radius={2} title={`${count} dead-letter messages`} aria-label={`${count} dead-letter messages`}>{count}</Badge>}
  </Link>
}

const TopicBranch = ({ name }: { name: string }) => {
  const { pathname } = useLocation()
  const [open, setOpen] = useState(pathname.startsWith(`/topics/${encodeURIComponent(name)}/`))
  const { data: subscriptions, isLoading } = useSubscriptions(name, open)
  return (
    <div>
      <Group gap={0} wrap="nowrap">
        <ActionIcon size="sm" variant="subtle" color="gray" aria-label={`${open ? 'Collapse' : 'Expand'} ${name}`} aria-expanded={open} onClick={() => setOpen(!open)}>
          {open ? <IconChevronDown size={14} /> : <IconChevronRight size={14} />}
        </ActionIcon>
        <ResourceLink name={name} kind="topic" to={`/topics/${encodeURIComponent(name)}`} />
      </Group>
      {open && <div className="portal-nav-children">
        {isLoading && <Loader size="xs" />}
        {subscriptions?.map(sub => <ResourceLink key={sub.name} name={sub.name} kind="subscription" deadLetterCount={sub.deadLetterMessageCount} deadLetterCountIsExact={sub.deadLetterMessageCountIsExact} to={`/topics/${encodeURIComponent(name)}/${encodeURIComponent(sub.name)}`} />)}
      </div>}
    </div>
  )
}

const NavTree = ({ compact = false }: { compact?: boolean }) => {
  const { pathname } = useLocation()
  const { data: queues, isLoading: queuesLoading } = useQueues()
  const { data: topics, isLoading: topicsLoading } = useTopics()
  const [filter, setFilter] = useState('')
  const [queuesOpen, setQueuesOpen] = useState(true)
  const [topicsOpen, setTopicsOpen] = useState(true)
  const matches = (name: string) => name.toLowerCase().includes(filter.toLowerCase())

  return (
    <Stack gap={4}>
      {!compact && <TextInput mx="sm" mb="sm" size="xs" aria-label="Filter navigation" placeholder="Filter resources" leftSection={<IconSearch size={14} />} value={filter} onChange={e => setFilter(e.currentTarget.value)} />}
      {(['queues', 'topics'] as const).map(kind => {
        const isQueue = kind === 'queues'
        const open = isQueue ? queuesOpen : topicsOpen
        const Icon = isQueue ? IconMessageDots : IconTopologyStar
        const label = isQueue ? 'Queues' : 'Topics'
        return <div key={kind}>
          <Group gap={0} wrap="nowrap" className="portal-nav-section">
            <Tooltip label={label} disabled={!compact} position="right">
              <Link to={`/${kind}`} className={`portal-resource-link ${compact ? 'portal-resource-link-compact' : ''}`} aria-current={pathname === `/${kind}` ? 'page' : undefined} aria-label={label}>
                <Icon size={18} />{!compact && <Text size="sm" fw={600}>{label}</Text>}
              </Link>
            </Tooltip>
            {!compact && <ActionIcon mr={8} size="sm" variant="subtle" color="gray" aria-label={`${open ? 'Collapse' : 'Expand'} ${label}`} aria-expanded={open} onClick={() => isQueue ? setQueuesOpen(!open) : setTopicsOpen(!open)}>
              {open ? <IconChevronDown size={14} /> : <IconChevronRight size={14} />}
            </ActionIcon>}
          </Group>
          {!compact && open && <div className="portal-nav-children">
            {(isQueue ? queuesLoading : topicsLoading) && <Loader size="xs" />}
            {isQueue
              ? queues?.filter(q => matches(q.name)).map(q => <ResourceLink key={q.name} name={q.name} deadLetterCount={q.deadLetterMessageCount} deadLetterCountIsExact={q.deadLetterMessageCountIsExact} to={`/queues/${encodeURIComponent(q.name)}`} />)
              : topics?.filter(t => matches(t.name)).map(t => <TopicBranch key={t.name} name={t.name} />)}
          </div>}
        </div>
      })}
    </Stack>
  )
}

export default NavTree
