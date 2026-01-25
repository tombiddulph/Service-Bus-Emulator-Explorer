import { createTheme } from '@mantine/core'
import type { BrandVariants, Theme } from '@fluentui/react-components'
import { createDarkTheme, createLightTheme } from '@fluentui/react-components'

// Mantine themes
export const mantineLight = createTheme({
  colors: {
    brand: [
      '#e5f2ff',
      '#c9e0ff',
      '#9fc7ff',
      '#73abff',
      '#4c8fff',
      '#2f79ff',
      '#1d6cff',
      '#0b60ff',
      '#0047cc',
      '#003399',
    ],
  },
  primaryColor: 'brand',
  primaryShade: 5,
  fontFamily: "'Segoe UI', 'Segoe UI Web (West European)', -apple-system, BlinkMacSystemFont, 'Helvetica Neue', sans-serif",
  defaultRadius: 'md',
  shadows: {
    sm: '0 4px 14px rgba(0,0,0,0.07)',
    md: '0 8px 24px rgba(0,0,0,0.08)',
  },
  headings: { fontFamily: "'Segoe UI', 'Segoe UI Web (West European)', sans-serif" },
})

export const mantineDark = createTheme({
  colors: {
    brand: [
      '#e5f2ff',
      '#c9e0ff',
      '#9fc7ff',
      '#73abff',
      '#4c8fff',
      '#2f79ff',
      '#1d6cff',
      '#0b60ff',
      '#0047cc',
      '#003399',
    ],
  },
  primaryColor: 'brand',
  primaryShade: 5,
  fontFamily: "'Segoe UI', 'Segoe UI Web (West European)', -apple-system, BlinkMacSystemFont, 'Helvetica Neue', sans-serif",
  defaultRadius: 'md',
  shadows: {
    sm: '0 4px 14px rgba(0,0,0,0.35)',
    md: '0 8px 24px rgba(0,0,0,0.5)',
  },
  headings: { fontFamily: "'Segoe UI', 'Segoe UI Web (West European)', sans-serif" },
})

// Fluent themes (for existing Fluent components)
const brand: BrandVariants = {
  10: '#061724',
  20: '#082338',
  30: '#0a2e4a',
  40: '#0c3b5f',
  50: '#0e4775',
  60: '#0f548c',
  70: '#115ea3',
  80: '#0f6ebe',
  90: '#0d7fcf',
  100: '#0b88d0',
  110: '#0a96dd',
  120: '#0ba8e0',
  130: '#0db5e4',
  140: '#1bc3ee',
  150: '#35d0f8',
  160: '#4fdbff',
}

const fluentBaseLight = createLightTheme(brand)
const fluentBaseDark = createDarkTheme(brand)

export const fluentLightTheme: Theme = {
  ...fluentBaseLight,
  colorNeutralBackground1: '#eef1f7',
  colorNeutralBackground2: '#ffffff',
  colorNeutralStroke1: '#d0d7e2',
  shadow4: '0 8px 24px rgba(0,0,0,0.08)',
  shadow8: '0 12px 32px rgba(0,0,0,0.12)',
  fontFamilyBase: "'Segoe UI', 'Segoe UI Web (West European)', -apple-system, BlinkMacSystemFont, 'Helvetica Neue', sans-serif",
}

export const fluentDarkTheme: Theme = {
  ...fluentBaseDark,
  colorNeutralBackground1: '#0f1624',
  colorNeutralBackground2: '#111820',
  colorNeutralStroke1: '#1f2633',
  shadow4: '0 8px 24px rgba(0,0,0,0.5)',
  shadow8: '0 12px 32px rgba(0,0,0,0.6)',
  fontFamilyBase: "'Segoe UI', 'Segoe UI Web (West European)', -apple-system, BlinkMacSystemFont, 'Helvetica Neue', sans-serif",
}
