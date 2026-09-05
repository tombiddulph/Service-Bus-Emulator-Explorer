import { useState } from 'react'
import { Link, Outlet, useLocation } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { ActionIcon, Anchor, AppShell as MantineAppShell, Badge, Breadcrumbs, Group, Stack, Text, Tooltip } from '@mantine/core'
import { IconLayoutSidebarLeftCollapse, IconLayoutSidebarLeftExpand, IconMenu2, IconMoon, IconRefresh, IconSun, IconTopologyStar } from '@tabler/icons-react'
import NavTree from '../components/NavTree'
import MockBanner from '../components/MockBanner'
import { useAppContext } from '../App'
import { useEnvironment } from '../api/hooks'

function AppShell() {
  const { theme, toggleTheme } = useAppContext()
  const queryClient = useQueryClient()
  const { data: environment } = useEnvironment()
  const [collapsed, setCollapsed] = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)
  const { pathname } = useLocation()
  const segments = pathname.split('/').filter(Boolean)

  return (
    <MantineAppShell
      padding={0}
      header={{ height: 48 }}
      navbar={{ width: collapsed ? 64 : 240, breakpoint: 'md', collapsed: { mobile: !mobileOpen } }}
      className="portal-shell"
    >
      <MantineAppShell.Header className="portal-topbar">
        <Group justify="space-between" h="100%" px="sm" wrap="nowrap">
          <Group gap="sm" wrap="nowrap">
            <ActionIcon hiddenFrom="md" variant="transparent" color="white" aria-label="Toggle navigation" onClick={() => setMobileOpen(!mobileOpen)}>
              <IconMenu2 size={20} />
            </ActionIcon>
            <IconTopologyStar size={24} aria-hidden />
            <Anchor component={Link} to="/queues" c="white" fw={600} className="portal-brand">Service Bus Explorer</Anchor>
            <Text size="xs" className="portal-topbar-label" visibleFrom="sm">Emulator</Text>
          </Group>
          <Group gap={8} wrap="nowrap">
            <Badge variant="outline" color="gray.0" radius={2} visibleFrom="sm">{environment ?? 'Connecting…'}</Badge>
            <Tooltip label="Refresh all resources">
              <ActionIcon variant="transparent" color="white" aria-label="Refresh all resources" onClick={() => queryClient.invalidateQueries()}><IconRefresh size={18} /></ActionIcon>
            </Tooltip>
            <Tooltip label={`Switch to ${theme === 'light' ? 'dark' : 'light'} mode`}>
              <ActionIcon variant="transparent" color="white" aria-label={`Switch to ${theme === 'light' ? 'dark' : 'light'} mode`} onClick={toggleTheme}>
                {theme === 'light' ? <IconMoon size={18} /> : <IconSun size={18} />}
              </ActionIcon>
            </Tooltip>
          </Group>
        </Group>
      </MantineAppShell.Header>

      <MantineAppShell.Navbar className="portal-sidebar" p={0}>
        <Group justify={collapsed ? 'center' : 'space-between'} px="sm" h={44} wrap="nowrap">
          {!collapsed && <Text size="xs" fw={600} c="dimmed">SERVICE BUS</Text>}
          <ActionIcon visibleFrom="md" variant="subtle" color="gray" aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'} onClick={() => setCollapsed(!collapsed)}>
            {collapsed ? <IconLayoutSidebarLeftExpand size={18} /> : <IconLayoutSidebarLeftCollapse size={18} />}
          </ActionIcon>
        </Group>
        <div className="portal-nav-scroll" onClick={(event) => { if ((event.target as HTMLElement).closest('a')) setMobileOpen(false) }}>
          <NavTree compact={collapsed} />
        </div>
        {!collapsed && <Text size="xs" c="dimmed" p="md" className="portal-sidebar-footer">Local emulator workspace</Text>}
      </MantineAppShell.Navbar>

      <MantineAppShell.Main>
        <nav aria-label="Breadcrumb" className="portal-breadcrumbs">
          <Breadcrumbs separator="/">
            <Anchor component={Link} to="/queues" size="sm">Service Bus</Anchor>
            {segments.map((segment, index) => {
              const label = index === 0 ? segment.charAt(0).toUpperCase() + segment.slice(1) : decodeURIComponent(segment)
              return index === segments.length - 1
                ? <Text key={index} size="sm" truncate>{label}</Text>
                : <Anchor key={index} component={Link} to={`/${segments.slice(0, index + 1).join('/')}`} size="sm">{label}</Anchor>
            })}
          </Breadcrumbs>
        </nav>
        <div className="portal-workspace">
          <Stack gap="md"><MockBanner /><Outlet /></Stack>
        </div>
      </MantineAppShell.Main>
    </MantineAppShell>
  )
}

export default AppShell
