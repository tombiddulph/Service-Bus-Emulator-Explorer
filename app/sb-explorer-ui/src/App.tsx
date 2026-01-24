import { createContext, useContext, useEffect, useMemo, useState } from 'react'
import { MantineProvider } from '@mantine/core'
import { Notifications } from '@mantine/notifications'
import { Navigate, RouterProvider, createBrowserRouter } from 'react-router-dom'
import AppShell from './routes/AppShell'
import Queues from './routes/Queues'
import QueueDetail from './routes/QueueDetail'
import Topics from './routes/Topics'
import TopicDetail from './routes/TopicDetail'
import AppError from './components/AppError'
import { mantineLight, mantineDark } from './theme'

type ColorScheme = 'light' | 'dark'

interface AppContextValue {
  theme: ColorScheme
  toggleTheme: () => void
}

const AppContext = createContext<AppContextValue | undefined>(undefined)

export const useAppContext = () => {
  const value = useContext(AppContext)
  if (!value) throw new Error('useAppContext must be used within App')
  return value
}

function App() {
  const [theme, setTheme] = useState<ColorScheme>(() => (localStorage.getItem('sbx-theme') as ColorScheme) || 'light')
  const toggleTheme = () => setTheme((prev: ColorScheme) => (prev === 'light' ? 'dark' : 'light'))

  useEffect(() => {
    localStorage.setItem('sbx-theme', theme)
  }, [theme])

  const router = useMemo(
    () =>
      createBrowserRouter([
        {
          element: <AppShell />,
          errorElement: <AppError />,
          children: [
            { index: true, element: <Navigate to="/queues" replace /> },
            { path: '/queues', element: <Queues />, errorElement: <AppError /> },
            { path: '/queues/:name', element: <QueueDetail />, errorElement: <AppError /> },
            { path: '/topics', element: <Topics />, errorElement: <AppError /> },
            { path: '/topics/:name', element: <TopicDetail />, errorElement: <AppError /> },
            { path: '/topics/:name/:subscription', element: <TopicDetail />, errorElement: <AppError /> },
          ],
        },
      ]),
    [],
  )

  return (
    <AppContext.Provider value={{ theme, toggleTheme }}>
      <MantineProvider theme={theme === 'light' ? mantineLight : mantineDark} forceColorScheme={theme} defaultColorScheme="light">
        <Notifications position="top-right" />
        <RouterProvider router={router} />
      </MantineProvider>
    </AppContext.Provider>
  )
}

export default App
