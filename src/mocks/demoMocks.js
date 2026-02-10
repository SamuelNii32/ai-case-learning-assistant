// Minimal mock data for demo mode

export const mockUser = {
  userId: 'demo-user',
  email: 'designer@demo.example',
  fullName: 'Demo User',
  role: 'instructor',
}

export const mockUploads = [
  {
    uploadId: 'UP-001',
    fileName: 'Cardiology_Case_Study.pdf',
    name: 'Cardiology Case Study',
  },
  {
    uploadId: 'UP-002',
    fileName: 'Neurology_Notes.pdf',
    name: 'Neurology Notes',
  },
]

export const mockSessions = [
  { id: 'S-100', uploadId: 'UP-001', title: 'Analysis Session 1', createdAt: new Date().toISOString() },
  { id: 'S-101', uploadId: 'UP-002', title: 'Follow-up Session', createdAt: new Date().toISOString() },
]

export const mockClassesMine = [
  { id: 'C-01', name: 'Clinical Methods 101', description: 'Intro to clinical methods', studentCount: 24, caseCount: 2 },
  { id: 'C-02', name: 'Advanced Diagnostics', description: 'Diagnostic reasoning', studentCount: 18, caseCount: 3 },
]

export const mockClassDetails = {
  students: [
    { id: 'STU-1', email: 'student1@example.com', fullName: 'Alex Kim' },
    { id: 'STU-2', email: 'student2@example.com', fullName: 'Priya Nair' },
  ],
  cases: [
    { uploadId: 'UP-001', fileName: 'Cardiology_Case_Study.pdf' },
    { uploadId: 'UP-002', fileName: 'Neurology_Notes.pdf' },
  ],
}

export const mockClassesEnrolled = [
  {
    id: 'C-10',
    name: 'Biochemistry',
    description: 'Fundamentals course',
    cases: [{ uploadId: 'UP-001', fileName: 'Cardiology_Case_Study.pdf' }],
  },
]

export const mockNotes = [
  { id: 'N-1', text: 'Interesting differential.', createdAt: new Date().toISOString() },
]
