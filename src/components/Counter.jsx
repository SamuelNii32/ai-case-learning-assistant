import { useState } from 'react'

function Counter({ initialValue = 0 }) {
  const [count, setCount] = useState(initialValue)

  return (
    <div className="p-4 bg-white rounded-lg shadow">
      <h2 className="text-xl font-bold mb-2">Counter Component</h2>
      <p className="text-gray-600 mb-4">Current count: <span data-testid="count">{count}</span></p>
      <div className="space-x-2">
        <button 
          onClick={() => setCount(count + 1)}
          className="bg-blue-500 hover:bg-blue-600 text-white px-4 py-2 rounded"
          data-testid="increment"
        >
          Increment
        </button>
        <button 
          onClick={() => setCount(count - 1)}
          className="bg-red-500 hover:bg-red-600 text-white px-4 py-2 rounded"
          data-testid="decrement"
        >
          Decrement
        </button>
        <button 
          onClick={() => setCount(0)}
          className="bg-gray-500 hover:bg-gray-600 text-white px-4 py-2 rounded"
          data-testid="reset"
        >
          Reset
        </button>
      </div>
    </div>
  )
}

export default Counter