import { useMemo, useState, type ReactNode } from 'react'
import { Outlet } from 'react-router-dom'
import {
  ActionIcon,
  AppShell as MantineAppShell,
  Badge,
  Box,
  Container,
  Divider,
  Group,
  Stack,
  Text,
  Tooltip,
} from '@mantine/core'
import {
  IconLayoutSidebarLeftCollapse,
  IconLayoutSidebarLeftExpand,
  IconMoon,
  IconRefresh,
  IconSun,
} from '@tabler/icons-react'

import NavTree from '../components/NavTree'
import MockBanner from '../components/MockBanner'
import { useAppContext } from '../App'

const ShellHeader = ({ actions, chrome }: { actions?: ReactNode; chrome: { headerBg: string; headerText: string } }) => {
  const { theme, toggleTheme } = useAppContext()
  const isLight = theme === 'light'

  return (
    <Box
      component="header"
      style={{
        height: 64,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '0 18px',
        background: chrome.headerBg,
        color: chrome.headerText,
        boxShadow: '0 8px 28px rgba(0,0,0,0.18)',
      }}
    >
      <Group gap="sm">
        <Box
          style={{
            width: 34,
            height: 34,
            borderRadius: 10,
            background: 'linear-gradient(135deg, rgba(255,255,255,0.22), rgba(255,255,255,0.08))',
            border: '1px solid rgba(255,255,255,0.25)',
            display: 'grid',
            placeItems: 'center',
            color: chrome.headerText,
            fontWeight: 700,
          }}
        >
          SB
        </Box>
        <Group gap={8} align="center">
          <Text fw={700} c={chrome.headerText} style={{ letterSpacing: -0.1 }}>
            Service Bus Explorer
          </Text>
          <Badge color="blue" variant="filled" radius="sm">
            Emulator
          </Badge>
        </Group>
      </Group>

      <Group gap={6} align="center">
        {actions}
        <Tooltip label="Refresh">
          <ActionIcon
            variant="subtle"
            color="gray.0"
            aria-label="Refresh"
            onClick={() => window.location.reload()}
            style={{ color: chrome.headerText }}
          >
            <IconRefresh size={18} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label={isLight ? 'Switch to dark mode' : 'Switch to light mode'}>
          <ActionIcon
            variant="subtle"
            color="gray.0"
            aria-label={isLight ? 'Switch to dark mode' : 'Switch to light mode'}
            onClick={toggleTheme}
            style={{ color: chrome.headerText }}
          >
            {isLight ? <IconMoon size={18} /> : <IconSun size={18} />}
          </ActionIcon>
        </Tooltip>
      </Group>
    </Box>
  )
}

function AppShell() {
  const { theme } = useAppContext()
  const [collapsed, setCollapsed] = useState(false)

  const chrome = useMemo(
    () =>
      theme === 'light'
        ? {
            headerBg: 'linear-gradient(120deg, #0f65c9 0%, #0b78e3 60%, #0a8cff 100%)',
            headerText: '#f8fbff',
            subBg: '#f3f6fb',
            subText: '#2c3547',
            divider: '#d6dce7',
            sidebarBg: '#0c1626',
            sidebarText: '#f5f7fb',
            mainBg: '#eef2f8',
            mainText: '#0f172a',
          }
        : {
            headerBg: 'linear-gradient(120deg, #0a2d4f 0%, #0b3a66 50%, #0f4c85 100%)',
            headerText: '#e7f1ff',
            subBg: '#0f1a28',
            subText: '#dbe6f7',
            divider: '#1f2836',
            sidebarBg: '#0b101b',
            sidebarText: '#e3e8f5',
            mainBg: '#0b1220',
            mainText: '#e4e9f5',
          },
    [theme],
  )

  return (
    <MantineAppShell
      padding={0}
      header={{ height: 64 }}
      navbar={{ width: collapsed ? 88 : 280, breakpoint: 'md', collapsed: { mobile: collapsed, desktop: false } }}
      styles={{
        header: { backgroundColor: 'transparent' },
        navbar: {
          backgroundColor: chrome.sidebarBg,
          color: chrome.sidebarText,
          borderRight: '1px solid rgba(255,255,255,0.08)',
          paddingLeft: 8,
          paddingRight: 8,
        },
        main: {
          backgroundColor: chrome.mainBg,
          color: chrome.mainText,
          paddingBottom: 0,
          minHeight: '100vh',
        },
      }}
    >
      <MantineAppShell.Header>
        <ShellHeader
          chrome={{ headerBg: chrome.headerBg, headerText: chrome.headerText }}
          actions={
            <Group gap={8} align="center" c={chrome.headerText}>
              <Divider orientation="vertical" color="rgba(255,255,255,0.35)" h={26} visibleFrom="md" />
              <Text fw={600} visibleFrom="md">Azure Service Bus Emulator</Text>
              <ActionIcon
                variant="subtle"
                aria-label={collapsed ? 'Open navigation' : 'Close navigation'}
                onClick={() => setCollapsed((c) => !c)}
                color="gray.0"
                hiddenFrom="md"
              >
                {collapsed ? <IconLayoutSidebarLeftExpand size={18} /> : <IconLayoutSidebarLeftCollapse size={18} />}
              </ActionIcon>
            </Group>
          }
        />
      </MantineAppShell.Header>

      <MantineAppShell.Navbar p="sm" withBorder={false}>
        <Group justify="space-between" align="center" mb="sm" px={4}>
          {!collapsed && (
            <Text fw={600} size="sm" c={chrome.sidebarText}>
              Navigation
            </Text>
          )}
          <Tooltip label={collapsed ? 'Expand rail' : 'Collapse rail'}>
            <ActionIcon
              variant="subtle"
              color={theme === 'light' ? 'gray.2' : 'gray.6'}
              aria-label="Toggle navigation"
              onClick={() => setCollapsed((c) => !c)}
              style={{ color: chrome.sidebarText }}
            >
              {collapsed ? <IconLayoutSidebarLeftExpand size={18} /> : <IconLayoutSidebarLeftCollapse size={18} />}
            </ActionIcon>
          </Tooltip>
        </Group>
        <NavTree compact={collapsed} />
      </MantineAppShell.Navbar>

      <MantineAppShell.Main>
        <Box
          style={{
            backgroundColor: chrome.subBg,
            color: chrome.subText,
            borderBottom: `1px solid ${chrome.divider}`,
            padding: '10px 18px',
          }}
        >
          <Group gap={6} align="center">
            <Text size="sm">Home</Text>
            <Divider orientation="vertical" h={14} color={chrome.divider} />
            <Text size="sm" fw={600}>
              Service Bus (Emulator)
            </Text>
          </Group>
        </Box>
        <Box py="md" px="md">
          <Container size="xl" px="md">
            <Stack gap="md">
              <MockBanner />
              <Outlet />
            </Stack>
          </Container>
        </Box>
      </MantineAppShell.Main>
    </MantineAppShell>
  )
}

export default AppShell
