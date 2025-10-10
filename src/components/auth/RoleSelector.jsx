import { Label } from '../ui/label'

const RoleSelector = ({ role, onRoleChange, className = '' }) => {
  const handleRoleChange = e => {
    onRoleChange(e.target.value)
  }

  return (
    <div className={`space-y-3 ${className}`}>
      <Label className="text-sm font-medium text-gray-700">I am a:</Label>
      <div className="flex flex-col sm:flex-row space-y-3 sm:space-y-0 sm:space-x-6">
        <div className="flex items-center">
          <input
            type="radio"
            id="student"
            name="role"
            value="student"
            checked={role === 'student'}
            onChange={handleRoleChange}
            className="h-4 w-4 text-blue-600 border-gray-300 focus:ring-blue-500"
          />
          <Label htmlFor="student" className="ml-2 text-sm text-gray-700 cursor-pointer">
            Student
          </Label>
        </div>
        <div className="flex items-center">
          <input
            type="radio"
            id="instructor"
            name="role"
            value="instructor"
            checked={role === 'instructor'}
            onChange={handleRoleChange}
            className="h-4 w-4 text-blue-600 border-gray-300 focus:ring-blue-500"
          />
          <Label htmlFor="instructor" className="ml-2 text-sm text-gray-700 cursor-pointer">
            Instructor
          </Label>
        </div>
      </div>
    </div>
  )
}

export default RoleSelector
