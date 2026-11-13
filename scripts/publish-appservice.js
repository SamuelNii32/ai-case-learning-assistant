import fs from 'node:fs'
import path from 'node:path'
import process from 'node:process'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

export function preparePublishDirectory(repositoryRoot, requestedOutput) {
  const outputDirectory = path.resolve(repositoryRoot, requestedOutput)
  const artifactsRoot = path.join(repositoryRoot, 'artifacts')
  const relativeOutput = path.relative(artifactsRoot, outputDirectory)

  if (!relativeOutput || relativeOutput.startsWith('..') || path.isAbsolute(relativeOutput)) {
    throw new Error('The App Service publish output must be a child of the repository artifacts directory.')
  }

  // dotnet publish does not remove files left in a custom output directory. A
  // clean directory is required so excluded development files can never survive
  // from an older build and enter a deployment ZIP.
  fs.rmSync(outputDirectory, { recursive: true, force: true })
  return outputDirectory
}

function main() {
  const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
  const outputDirectory = preparePublishDirectory(
    repositoryRoot,
    process.argv[2] || 'artifacts/appservice-publish',
  )
  const result = spawnSync(
    'dotnet',
    [
      'publish',
      'IngestionApi.csproj',
      '--configuration',
      'Release',
      '--output',
      outputDirectory,
      '/p:UseAppHost=false',
    ],
    { cwd: repositoryRoot, stdio: 'inherit', shell: process.platform === 'win32' },
  )

  if (result.error) throw result.error
  if (result.status !== 0) process.exit(result.status ?? 1)
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main()
