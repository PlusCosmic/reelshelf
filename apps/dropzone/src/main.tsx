import { StrictMode } from 'react'
import ReactDOM from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MantineProvider } from '@mantine/core'
import { ModalsProvider } from '@mantine/modals'
import { Notifications } from '@mantine/notifications'
import { nucleusTheme } from '@repo/shared/theme'
import App from './App'
import '@mantine/core/styles.css'
import '@mantine/dropzone/styles.css'
import '@mantine/notifications/styles.css'
import './styles.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
      staleTime: 0,
    },
  },
})

const rootElement = document.getElementById('root')
if (rootElement && !rootElement.innerHTML) {
  const root = ReactDOM.createRoot(rootElement)
  root.render(
    <StrictMode>
      <MantineProvider theme={nucleusTheme} defaultColorScheme="dark">
        <ModalsProvider>
          <Notifications />
          <QueryClientProvider client={queryClient}>
            <App />
          </QueryClientProvider>
        </ModalsProvider>
      </MantineProvider>
    </StrictMode>,
  )
}
