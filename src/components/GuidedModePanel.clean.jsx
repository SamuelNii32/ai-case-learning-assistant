import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'

/**
 * Minimal guided flow with 5 milestones.
 * You can lift state up later to persist into StudentWork.
 */
const STEPS = [
  { key: 'problem', label: 'Problem' },
  { key: 'evidence', label: 'Evidence' },
  { key: 'analysis', label: 'Analysis' },
  { key: 'recommendation', label: 'Recommendation' },
  { key: 'reflection', label: 'Reflection' },
]

export default function GuidedModePanel() {
  const [completed, setCompleted] = useState({})

  const doneCount = STEPS.filter(s => completed[s.key]).length
  const progress = Math.round((doneCount / STEPS.length) * 100)

  function toggle(key) {
    setCompleted(prev => ({ ...prev, [key]: !prev[key] }))
  }

  return (
    <div className="flex-1 overflow-auto p-4 space-y-4">
      <Card className="p-4">
        <div className="flex items-center justify-between mb-2">
          <h3 className="text-sm font-medium text-foreground">Guided Walkthrough</h3>
          <span className="text-xs text-muted-foreground">{progress}%</span>
        </div>

        <div className="space-y-2">
          {STEPS.map(step => (
            <label
              key={step.key}
              className="flex items-center gap-3 rounded-lg border border-border p-2 hover:bg-muted cursor-pointer"
            >
              <input
                type="checkbox"
                checked={!!completed[step.key]}
                onChange={() => toggle(step.key)}
                className="h-4 w-4"
              />
              <span className="text-sm">{step.label}</span>
            </label>
          ))}
        </div>

        <div className="mt-3 flex justify-end">
          <Button size="sm" className="px-3 py-1.5">
            Mark Next Step Complete
          </Button>
        </div>
      </Card>

      <Card className="p-4">
        <h4 className="text-sm font-medium mb-2">Current Step</h4>
        <p className="text-xs text-muted-foreground">
          Use the checkboxes to mark each step complete as you work through the case. You can expand
          this area later with prompts, inputs, and evidence attachments.
        </p>
      </Card>
    </div>
  )
}
