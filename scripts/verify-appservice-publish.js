import fs from 'node:fs'
import path from 'node:path'
import process from 'node:process'

const publishDirectory = path.resolve(process.argv[2] || 'artifacts/appservice-publish')
const webRoot = path.join(publishDirectory, 'wwwroot')
const indexPath = path.join(webRoot, 'index.html')
const appAssembly = path.join(publishDirectory, 'IngestionApi.dll')

function requireFile(file, description) {
  if (!fs.existsSync(file) || fs.statSync(file).size === 0) {
    throw new Error(`${description} is missing or empty: ${file}`)
  }
}

requireFile(appAssembly, 'ASP.NET Core application assembly')
requireFile(indexPath, 'React application shell')

const index = fs.readFileSync(indexPath, 'utf8')
if (index.includes('/src/main.jsx')) {
  throw new Error('The published index.html still references Vite development source files.')
}

const assetReferences = [...index.matchAll(/(?:src|href)="(\/assets\/[^"?#]+)/g)].map(
  match => match[1],
)
if (assetReferences.length === 0) {
  throw new Error('The published React shell does not reference any compiled assets.')
}

for (const reference of assetReferences) {
  requireFile(path.join(webRoot, reference.replace(/^\//, '')), `Compiled asset ${reference}`)
}

console.log(
  `Verified combined App Service artifact at ${publishDirectory} (${assetReferences.length} referenced assets).`,
)
