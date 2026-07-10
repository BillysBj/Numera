import { Routes, Route, Navigate } from 'react-router-dom'

// Task 1 shell: minimal routed app. i18n (Task 2) and the Login/Dashboard
// pages + BFF client (Task 3) are wired in subsequent tasks.
function Shell({ title }: { title: string }) {
  return (
    <>
      <header className="app-header">
        <span className="brand">Numera</span>
      </header>
      <main className="app-main">
        <h1>{title}</h1>
        <p className="muted">Plattform-Kern PWA shell.</p>
      </main>
    </>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Shell title="Login" />} />
      <Route path="/dashboard" element={<Shell title="Dashboard" />} />
      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  )
}
