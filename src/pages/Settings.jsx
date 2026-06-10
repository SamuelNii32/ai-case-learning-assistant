import React, { useContext } from 'react'
import { AuthContext } from '@/contexts/AuthContext'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { User, Mail, Shield, Trash2 } from 'lucide-react'
import toast from 'react-hot-toast'

function roleLabel(role) {
  if (role === 'instructor') return 'Instructor'
  if (role === 'student') return 'Student'
  return 'Account'
}

export default function SettingsPage() {
  const auth = useContext(AuthContext)
  const user = auth?.user || {}
  const displayName = user.fullName || user.name || 'Not provided'
  const email = user.email || 'Not available'
  const role = roleLabel(user.role)

  function handleDeletionRequest() {
    toast(
      'Account deletion is handled by the project administrator during the classroom pilot.'
    )
  }

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-4 md:p-6">
      <header>
        <h1 className="text-3xl font-bold text-[#2C2218]">Settings</h1>
        <p className="mt-1 text-sm text-[#5C4C3C]">
          Review your account details and classroom access.
        </p>
      </header>

      <Card className="p-5 md:p-6">
        <div className="mb-5">
          <h2 className="text-lg font-semibold text-[#2C2218]">Profile</h2>
          <p className="text-sm text-[#5C4C3C]">This information is used for class rosters and progress views.</p>
        </div>

        <div className="grid gap-3 md:grid-cols-3">
          <div className="rounded-md border border-slate-200 bg-white p-4">
            <div className="mb-2 flex items-center gap-2 text-xs font-medium uppercase text-slate-500">
              <User className="h-4 w-4" />
              Name
            </div>
            <div className="text-sm font-semibold text-[#2C2218]">{displayName}</div>
          </div>

          <div className="rounded-md border border-slate-200 bg-white p-4">
            <div className="mb-2 flex items-center gap-2 text-xs font-medium uppercase text-slate-500">
              <Mail className="h-4 w-4" />
              Email
            </div>
            <div className="break-words text-sm font-semibold text-[#2C2218]">{email}</div>
          </div>

          <div className="rounded-md border border-slate-200 bg-white p-4">
            <div className="mb-2 flex items-center gap-2 text-xs font-medium uppercase text-slate-500">
              <Shield className="h-4 w-4" />
              Role
            </div>
            <div className="text-sm font-semibold text-[#2C2218]">{role}</div>
          </div>
        </div>
      </Card>

      <Card className="p-5 md:p-6">
        <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
          <div>
            <h2 className="text-lg font-semibold text-[#2C2218]">Account deletion</h2>
            <p className="mt-1 max-w-2xl text-sm text-[#5C4C3C]">
              For the classroom pilot, account deletion is handled by the project administrator so class records,
              assignments, notes, and progress data can be reviewed safely before removal.
            </p>
          </div>
          <Button
            type="button"
            variant="outline"
            onClick={handleDeletionRequest}
            className="inline-flex items-center gap-2 border-red-200 text-red-700 hover:bg-red-50"
          >
            <Trash2 className="h-4 w-4" />
            Request deletion
          </Button>
        </div>
      </Card>
    </div>
  )
}
