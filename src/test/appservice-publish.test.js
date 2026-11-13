import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'
import { preparePublishDirectory } from '../../scripts/publish-appservice.js'

const temporaryDirectories = []

afterEach(() => {
  for (const directory of temporaryDirectories.splice(0)) {
    fs.rmSync(directory, { recursive: true, force: true })
  }
})

describe('App Service clean publish directory', () => {
  it('removes stale development configuration from a reused artifact directory', () => {
    const repository = fs.mkdtempSync(path.join(os.tmpdir(), 'casepilot-publish-test-'))
    temporaryDirectories.push(repository)
    const output = path.join(repository, 'artifacts', 'appservice-publish')
    fs.mkdirSync(output, { recursive: true })
    fs.writeFileSync(path.join(output, 'appsettings.Development.json'), '{"stale":true}')

    const prepared = preparePublishDirectory(repository, 'artifacts/appservice-publish')

    expect(prepared).toBe(output)
    expect(fs.existsSync(output)).toBe(false)
  })

  it('refuses to clean the repository root or a path outside artifacts', () => {
    const repository = fs.mkdtempSync(path.join(os.tmpdir(), 'casepilot-publish-test-'))
    temporaryDirectories.push(repository)

    expect(() => preparePublishDirectory(repository, '.')).toThrow(/child of.*artifacts/i)
    expect(() => preparePublishDirectory(repository, 'publish')).toThrow(/child of.*artifacts/i)
    expect(() => preparePublishDirectory(repository, 'artifacts')).toThrow(/child of.*artifacts/i)
  })
})
