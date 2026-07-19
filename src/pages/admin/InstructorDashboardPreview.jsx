import { Link } from 'react-router-dom'
import { BookOpen, Copy, Plus, RotateCw, Users, Upload } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { mockClassesMine, mockClassDetails } from '@/mocks/demoMocks'

export default function InstructorDashboardPreview() {
  const selectedClass = mockClassesMine[1] || mockClassesMine[0]

  return (
    <div className="min-h-screen bg-[#faf6f0] text-[#2c2218]">
      <header className="border-b border-[#e4d6c7] bg-white/90 backdrop-blur-sm">
        <div className="max-w-7xl mx-auto px-4">
          <div className="flex items-center gap-2 overflow-x-auto whitespace-nowrap">
            <Link
              to="#classes"
              className="flex items-center gap-2 px-4 py-3 text-sm font-semibold border-b-2 border-[#C96A08] text-[#2c2218] bg-[#fff6eb]"
            >
              <BookOpen className="w-4 h-4 shrink-0" />
              My Classes
            </Link>
            <Link
              to="#upload"
              className="flex items-center gap-2 px-4 py-3 text-sm font-semibold border-b-2 border-transparent text-[#7a5c3c] hover:text-[#2c2218] hover:border-[#d9c4ad]"
            >
              <Upload className="w-4 h-4 shrink-0" />
              Upload Cases
            </Link>
          </div>
        </div>
      </header>

      <main className="max-w-7xl mx-auto px-4 py-10 space-y-8">
        <section className="bg-white border border-[#f4e7d8] shadow-[0_25px_45px_rgba(32,20,8,0.08)] rounded-[12px] p-6 md:p-8 space-y-6">
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
            <div>
              <h1 className="text-2xl md:text-3xl font-bold text-[#2c2218]">Classes</h1>
              <p className="text-sm text-[#5C4C3C] mt-1">Manage your classes, students, and assignments.</p>
            </div>
            <Button variant="warm" className="w-full sm:w-auto inline-flex items-center gap-2">
              <Plus size={18} />
              Create Class
            </Button>
          </div>

          <div className="border-b border-[#E8DDD0] bg-white px-1">
            <div className="flex flex-wrap gap-6 text-sm font-semibold text-[#5C4C3C]">
              <button type="button" className="pb-3 text-[#C96A08] border-b-2 border-[#C96A08]">
                My Classes
              </button>
              <button type="button" className="pb-3 border-b-2 border-transparent text-[#7a5c3e] hover:text-[#2c2218] hover:border-[#d9c4ad]">
                Upload Cases
              </button>
            </div>
          </div>

          <div id="classes" className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-6">
            {mockClassesMine.map(cls => (
              <Card
                key={cls.id}
                className="p-5 md:p-6 bg-white border border-[#f3e0ce] rounded-[12px] shadow-[0_10px_25px_rgba(32,20,8,0.08)] transition-shadow hover:shadow-[0_12px_30px_rgba(32,20,8,0.12)]"
              >
                <div className="flex flex-col h-full gap-4 text-[#2c2218]">
                  <div>
                    <h3 className="text-lg font-semibold">{cls.name}</h3>
                    <p className="text-sm text-[#5C4C3C] mt-1">{cls.description}</p>
                  </div>
                  <div className="flex flex-wrap items-center gap-6 text-sm text-[#5C4C3C]">
                    <div className="flex items-center gap-2">
                      <Users size={16} className="text-[#C96A08]" />
                      <span className="font-semibold text-[#2c2218]">{cls.studentCount}</span>
                      <span>students</span>
                    </div>
                    <div className="flex items-center gap-2">
                      <BookOpen size={16} className="text-[#C96A08]" />
                      <span className="font-semibold text-[#2c2218]">{cls.caseCount}</span>
                      <span>cases</span>
                    </div>
                  </div>
                  <div className="mt-auto">
                    <Button variant="warm" size="sm" className="w-full">
                      Manage Class
                    </Button>
                  </div>
                </div>
              </Card>
            ))}
          </div>
        </section>

        <section className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <Card className="p-6 space-y-4 bg-white border border-[#f3e0ce] rounded-[12px] shadow-[0_10px_25px_rgba(32,20,8,0.08)]">
            <div className="flex items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-[#2c2218]">{selectedClass.name}</h2>
                <p className="text-sm text-[#5C4C3C] mt-1">{selectedClass.description}</p>
              </div>
              <Button variant="warm" size="sm" className="whitespace-nowrap">
                Delete class
              </Button>
            </div>

            <div className="border-2 border-[#C96A08]/20 bg-gradient-to-br from-[#fdf4eb] to-[#f9f1e8] rounded-[12px] p-4 space-y-3">
              <div>
                <h3 className="text-base font-semibold text-[#2c2218]">Class Join Code</h3>
                <p className="text-sm text-[#7a5c3c] mt-1">Share this code with students so they can join the class.</p>
              </div>
              <div className="flex items-center gap-3 p-4 bg-white border-2 border-[#C96A08] rounded-[12px]">
                <div className="flex-1 min-w-0">
                  <p className="text-xs text-[#7a5c3c] font-medium uppercase tracking-widest">Join Code</p>
                  <p className="text-3xl font-bold text-[#2c2218] font-mono tracking-wider">CP48NS5F</p>
                </div>
                <Button variant="warm" size="sm" className="whitespace-nowrap flex items-center gap-2">
                  <Copy className="h-4 w-4" />
                  Copy
                </Button>
              </div>
              <Button variant="outline" size="sm" className="inline-flex items-center gap-2">
                <RotateCw className="h-4 w-4" />
                Regenerate code
              </Button>
            </div>

            <div className="space-y-3">
              <h3 className="text-lg font-semibold text-[#2c2218]">Students</h3>
              {mockClassDetails.students.map(student => (
                <div key={student.id} className="flex items-center justify-between gap-3 p-3 bg-[#fdf4eb] border border-[#f3e0ce] rounded-md">
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-[#2c2218]">{student.fullName}</p>
                    <p className="text-xs text-[#7a5c3c]">{student.email}</p>
                  </div>
                  <Button variant="outline" size="sm">
                    Remove
                  </Button>
                </div>
              ))}
            </div>
          </Card>

          <Card className="p-6 space-y-4 bg-white border border-[#f3e0ce] rounded-[12px] shadow-[0_10px_25px_rgba(32,20,8,0.08)]">
            <div>
              <h2 className="text-lg font-semibold text-[#2c2218]">Cases</h2>
              <p className="text-sm text-[#5C4C3C] mt-1">Assign a case from your uploads.</p>
            </div>
            <div className="space-y-2">
              <label className="text-sm font-medium text-[#2c2218]">Select case</label>
              <select className="w-full px-3 py-2 border border-[#e4d6c7] rounded-md text-sm focus:outline-none focus:border-[#C96A08] focus:ring-2 focus:ring-[#C96A08]/30">
                <option>Choose a case</option>
                {mockClassDetails.cases.map(caseItem => (
                  <option key={caseItem.uploadId}>{caseItem.fileName}</option>
                ))}
              </select>
            </div>
            <Button variant="warm" className="w-full sm:w-auto">
              Assign case
            </Button>

            <div className="space-y-3">
              {mockClassDetails.cases.map(caseItem => (
                <div key={caseItem.uploadId} className="flex items-center justify-between gap-3 p-3 bg-[#fdf4eb] border border-[#f3e0ce] rounded-md">
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-[#2c2218]">{caseItem.fileName}</p>
                    <p className="text-xs text-[#7a5c3c]">{caseItem.uploadId}</p>
                  </div>
                  <Button variant="outline" size="sm">
                    Unassign
                  </Button>
                </div>
              ))}
            </div>
          </Card>
        </section>

        <section id="upload" className="bg-white border border-[#f3e0ce] rounded-[12px] shadow-[0_10px_25px_rgba(32,20,8,0.08)] p-6">
          <div className="flex items-center justify-between gap-3 flex-wrap">
            <div>
              <h2 className="text-lg font-semibold text-[#2c2218]">Reading Coach Progress</h2>
              <p className="text-sm text-[#5C4C3C] mt-1">This mock section exists so you can preview the class-detail layout without live data.</p>
            </div>
            <Button variant="outline" size="sm">
              Refresh
            </Button>
          </div>
          <div className="mt-4 grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
            {[
              ['2', 'assigned'],
              ['0', 'started'],
              ['0', 'on track'],
              ['0', 'need attention'],
              ['0', 'completed'],
              ['2', 'not started'],
            ].map(([value, label]) => (
              <div key={label} className="rounded-lg border border-[#f3e0ce] bg-[#fdf4eb] p-3 text-center">
                <div className="text-xl font-bold text-[#2c2218]">{value}</div>
                <div className="text-xs text-[#7a5c3c]">{label}</div>
              </div>
            ))}
          </div>
          <div className="mt-4 overflow-x-auto">
            <table className="min-w-full text-sm">
              <thead className="text-left text-[#7a5c3c]">
                <tr className="border-b border-[#f3e0ce]">
                  <th className="py-3 pr-4">Student</th>
                  <th className="py-3 pr-4">Case</th>
                  <th className="py-3 pr-4">Status</th>
                  <th className="py-3 pr-4">Progress</th>
                  <th className="py-3 pr-4">Last activity</th>
                  <th className="py-3 pr-4"></th>
                </tr>
              </thead>
              <tbody>
                {mockClassDetails.students.map(student => (
                  <tr key={student.id} className="border-b border-[#f3e0ce] last:border-0">
                    <td className="py-4 pr-4">
                      <div className="font-medium text-[#2c2218]">{student.fullName}</div>
                      <div className="text-xs text-[#7a5c3c]">{student.email}</div>
                    </td>
                    <td className="py-4 pr-4 text-[#2c2218]">Cardiology_Case_Study.pdf</td>
                    <td className="py-4 pr-4"><span className="inline-flex rounded-full bg-[#fff2e4] px-2 py-1 text-xs font-semibold text-[#C96A08]">Not started</span></td>
                    <td className="py-4 pr-4 text-[#2c2218]">0/6 steps</td>
                    <td className="py-4 pr-4 text-[#7a5c3c]">No activity yet</td>
                    <td className="py-4 pr-4 text-right"><Button variant="warm" size="sm">View details</Button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      </main>
    </div>
  )
}