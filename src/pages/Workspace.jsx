import { useParams } from 'react-router-dom'

export default function Workspace() {
  const { id } = useParams()

  return (
    <div className="min-h-screen p-8">
      <h1 className="text-2xl font-semibold">Workspace</h1>
      <p className="text-slate-600 mt-2">
        Opening case ID: <span className="font-medium">{id}</span>
      </p>
    </div>
  )
}
