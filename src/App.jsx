import { BrowserRouter as Router, Routes, Route } from 'react-router-dom'

function App() {
  return (
    <Router>
      <div className="bg-blue-500 text-white p-8 min-h-screen">
        <h1 className="text-4xl font-bold mb-4">✅ React + Tailwind + Router Working!</h1>
        <p className="text-xl mb-6">Your setup is complete and ready for development!</p>
        
        <div className="bg-white text-black p-6 rounded-lg shadow-lg max-w-md">
          <h2 className="text-2xl font-semibold text-gray-800 mb-4">What's installed:</h2>
          <ul className="space-y-2 text-gray-700">
            <li>✅ React (working)</li>
            <li>✅ Vite (working)</li>
            <li>✅ Tailwind CSS (working)</li>
            <li>✅ React Router (installed & working)</li>
          </ul>
        </div>
        
        <Routes>
          <Route path="/" element={
            <div className="mt-6 p-4 bg-green-500 rounded-lg">
              <p className="text-lg font-semibold">🎉 You're on the home route!</p>
            </div>
          } />
        </Routes>
      </div>
    </Router>
  )
}

export default App
