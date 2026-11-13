import fs from 'node:fs'
import net from 'node:net'
import os from 'node:os'
import path from 'node:path'
import process from 'node:process'
import { spawn } from 'node:child_process'

const publishDirectory = path.resolve(process.argv[2] || 'artifacts/appservice-publish')
const assembly = path.join(publishDirectory, 'IngestionApi.dll')
if (!fs.existsSync(assembly)) throw new Error(`Published application not found: ${assembly}`)

const temporaryDirectory = fs.mkdtempSync(path.join(os.tmpdir(), 'casepilot-appservice-smoke-'))
const output = []
let child

function freePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer()
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address()
      server.close(error => (error ? reject(error) : resolve(port)))
    })
  })
}

async function waitUntilReady(origin) {
  for (let attempt = 0; attempt < 60; attempt += 1) {
    if (child.exitCode !== null) throw new Error(`Published app exited with ${child.exitCode}.`)
    try {
      const response = await fetch(`${origin}/health/live`)
      if (response.ok) return
    } catch {
      // The Kestrel listener may not be ready yet.
    }
    await new Promise(resolve => setTimeout(resolve, 250))
  }
  throw new Error('Published app did not become ready within 15 seconds.')
}

async function expectResponse(origin, route, accept, expectedStatus, expectedText) {
  const response = await fetch(`${origin}${route}`, { headers: { Accept: accept }, redirect: 'manual' })
  const body = await response.text()
  const allowedStatuses = Array.isArray(expectedStatus) ? expectedStatus : [expectedStatus]
  if (!allowedStatuses.includes(response.status)) {
    throw new Error(`${route} returned ${response.status}; expected ${allowedStatuses.join(' or ')}.`)
  }
  if (expectedText && !body.includes(expectedText)) {
    throw new Error(`${route} did not contain the expected ${JSON.stringify(expectedText)} marker.`)
  }
}

try {
  const port = await freePort()
  const origin = `http://127.0.0.1:${port}`
  child = spawn('dotnet', [assembly], {
    cwd: publishDirectory,
    windowsHide: true,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: origin,
      DATABASE_PROVIDER: 'sqlite',
      SQLITE_CONNECTION_STRING: `Data Source=${path.join(temporaryDirectory, 'smoke.db')}`,
      DOCUMENT_STORAGE_PROVIDER: 'local',
      DATA_PROTECTION_KEYS_PATH: path.join(temporaryDirectory, 'keys'),
      OPENAI_API_KEY: 'combined-publish-smoke-test-not-used',
      JWT_SECRET: 'combined-publish-smoke-test-secret-32-characters',
      RUN_BACKGROUND_WORKER: 'false',
      AUTH0_ENABLED: 'false',
      AUTH0_BFF_API_ENABLED: 'false',
      ENABLE_DEBUG_ENDPOINTS: 'false',
      Logging__EventLog__LogLevel__Default: 'None',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  child.stdout.on('data', chunk => output.push(chunk.toString()))
  child.stderr.on('data', chunk => output.push(chunk.toString()))

  await waitUntilReady(origin)
  await expectResponse(origin, '/', 'text/html', 200, '<div id="root"></div>')
  await expectResponse(origin, '/admin/classes', 'text/html', 200, '<div id="root"></div>')
  await expectResponse(origin, '/cases', 'text/html', 200, '<div id="root"></div>')
  await expectResponse(origin, '/api/not-a-route', 'text/html', [401, 404])
  await expectResponse(origin, '/bff/not-a-route', 'text/html', [401, 404])
  await expectResponse(origin, '/signin-auth0', 'text/html', [401, 404])
  await expectResponse(origin, '/cases', 'application/json', 401)

  console.log(`Combined App Service runtime smoke test passed at ${origin}.`)
} catch (error) {
  if (output.length > 0) console.error(output.join('').slice(-12000))
  throw error
} finally {
  if (child && child.exitCode === null) {
    const exited = new Promise(resolve => child.once('exit', resolve))
    child.kill()
    await exited
  }
  fs.rmSync(temporaryDirectory, { recursive: true, force: true })
}
