import { useState } from 'react'
import { session } from './api'
import Auth from './Auth'
import Dashboard from './Dashboard'

export default function App() {
  const [token, setToken] = useState(session.get)

  if (!token)
    return (
      <Auth
        onLogin={(t) => {
          session.set(t)
          setToken(t)
        }}
      />
    )

  return (
    <Dashboard
      onLogout={() => {
        session.clear()
        setToken(null)
      }}
    />
  )
}
