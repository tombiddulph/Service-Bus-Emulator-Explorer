import { createContext, useContext } from 'react'

export type ColorScheme = 'light' | 'dark'

interface AppContextValue {
  theme: ColorScheme
  toggleTheme: () => void
}

export const AppContext = createContext<AppContextValue | undefined>(undefined)

export const useAppContext = () => {
  const value = useContext(AppContext)
  if (!value) throw new Error('useAppContext must be used within App')
  return value
}
