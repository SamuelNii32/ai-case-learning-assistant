// src/contexts/pdf-controller.js
import React from 'react'
import { createContext, useContext } from 'react'

const PdfControllerContext = createContext(null)

export function PdfControllerProvider({ value, children }) {
  return React.createElement(PdfControllerContext.Provider, { value }, children)
}

export function usePdfController() {
  return useContext(PdfControllerContext)
}
