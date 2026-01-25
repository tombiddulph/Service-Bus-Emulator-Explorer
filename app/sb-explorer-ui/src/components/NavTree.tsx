import { useMemo, useState, type MouseEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { Badge, Box, Loader, NavLink, Stack } from '@mantine/core'
import { IconChevronDown, IconChevronRight, IconMessageDots, IconTopologyStar, IconTopologyStar3 } from '@tabler/icons-react'
import { useQueues, useSubscriptions, useTopics } from '../api/hooks'
import type { TopicInfo } from '../api/types'

const TopicBranch = ({
  topic,
  compact,
  isOpen,
  onToggle,
}: {
  topic: TopicInfo
  compact: boolean
  isOpen: boolean
  onToggle: () => void
}) => {
  const navigate = useNavigate()
  const { data: subs, isLoading } = useSubscriptions(topic.name, isOpen)
  const handleToggleClick = (event: MouseEvent) => {
    event.preventDefault()
    event.stopPropagation()
    onToggle()
  }

  return (
    <NavLink
      className="nav-tree-link"
      label={compact ? topic.name.slice(0, 10) : topic.name}
      leftSection={<IconTopologyStar size={16} />}
      rightSection={
        <Box onClick={handleToggleClick} style={{ display: 'flex', alignItems: 'center', cursor: 'pointer' }}>
          {isOpen ? <IconChevronDown size={14} /> : <IconChevronRight size={14} />}
        </Box>
      }
      opened={isOpen}
      onClick={() => navigate(`/topics/${topic.name}`)}
      childrenOffset={compact ? 12 : 16}
    >



      {isOpen && isLoading && <Loader size="xs" color="blue" />}
      {isOpen &&
        subs?.map((sub) => (
          <NavLink
            className="nav-tree-link"
            key={`sub-${topic.name}-${sub.name}`}
            label={compact ? sub.name.slice(0, 12) : sub.name}
            leftSection={<IconTopologyStar3 size={14} />}
            rightSection={sub.deadLetterMessageCount > 0 ? (
              <Badge color="red" variant="light" size="xs">
                {sub.deadLetterMessageCount}
              </Badge>
            ) : undefined}
            onClick={(e) => {
              e.stopPropagation()
              navigate(`/topics/${topic.name}/${sub.name}`)
            }}
          />
        ))}
    </NavLink>
  )
}

const NavTree = ({ compact = false }: { compact?: boolean }) => {
  const navigate = useNavigate()
  const { data: queues, isLoading: queuesLoading } = useQueues()
  const { data: topics, isLoading: topicsLoading } = useTopics()

  const queueList = useMemo(() => (Array.isArray(queues) ? queues : queues ? Object.values(queues as any) : []), [queues])
  const topicList = useMemo(() => (Array.isArray(topics) ? topics : topics ? Object.values(topics as any) : []), [topics])

  const [openItems, setOpenItems] = useState<Set<string>>(new Set(['queues', 'topics']))

  const toggleBranch = (key: string) => {
    setOpenItems((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  const label = (text: string) => (compact ? text.slice(0, 1).toUpperCase() : text)

  const openAndNavigateQueues = () => {
    setOpenItems((prev) => new Set(prev).add('queues'))
    navigate('/queues')
  }

  const openAndNavigateTopics = () => {
    setOpenItems((prev) => new Set(prev).add('topics'))
    navigate('/topics')
  }

  const toggleQueues = () => {
    setOpenItems((prev) => {
      const next = new Set(prev)
      if (next.has('queues')) next.delete('queues')
      else next.add('queues')
      return next
    })
  }

  const handleQueuesToggleClick = (event: MouseEvent) => {
    event.preventDefault()
    event.stopPropagation()
    toggleQueues()
  }

  const handleTopicsToggleClick = (event: MouseEvent) => {
    event.preventDefault()
    event.stopPropagation()
    toggleBranch('topics')
  }


  return (
    <Stack gap="xs">
      <NavLink
        className="nav-tree-link"
        label={label('Queues')}
        leftSection={<IconMessageDots size={16} />}
        rightSection={
          <Box onClick={handleQueuesToggleClick} style={{ display: 'flex', alignItems: 'center', cursor: 'pointer' }}>
            {openItems.has('queues') ? <IconChevronDown size={14} /> : <IconChevronRight size={14} />}
          </Box>
        }
        opened={openItems.has('queues')}
        onClick={openAndNavigateQueues}
      >
        {queuesLoading && <Loader size="xs" color="blue" />}
        {queueList.map((queue: any) => (
          <NavLink
            className="nav-tree-link"
            key={queue.name}
            label={compact ? queue.name.slice(0, 12) : queue.name}
            onClick={(e) => {
              e.stopPropagation()
              navigate(`/queues/${queue.name}`)
            }}
            rightSection={queue.deadLetterMessageCount > 0 ? (
              <Badge color="red" variant="light" size="xs">
                {queue.deadLetterMessageCount}
              </Badge>
            ) : undefined}
            childrenOffset={compact ? 12 : 16}
          />
        ))}
      </NavLink>

      <NavLink
        className="nav-tree-link"
        label={label('Topics')}
        leftSection={<IconTopologyStar size={16} />}
        rightSection={
          <Box onClick={handleTopicsToggleClick} style={{ display: 'flex', alignItems: 'center', cursor: 'pointer' }}>
            {openItems.has('topics') ? <IconChevronDown size={14} /> : <IconChevronRight size={14} />}
          </Box>
        }
        opened={openItems.has('topics')}
        onClick={openAndNavigateTopics}
      >
        {topicsLoading && <Loader size="xs" color="blue" />}
        {topicList.map((topic: any) => (
          <TopicBranch
            key={topic.name}
            topic={topic as TopicInfo}
            compact={compact}
            isOpen={openItems.has(`topic-${topic.name}`)}
            onToggle={() => toggleBranch(`topic-${topic.name}`)}
          />
        ))}
      </NavLink>
    </Stack>
  )
}


export default NavTree
