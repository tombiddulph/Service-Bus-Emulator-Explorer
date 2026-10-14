import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'

const root = fileURLToPath(new URL('../', import.meta.url))
const ui = join(root, 'app', 'sb-explorer-ui')
const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm'
function run(command, args, cwd = root, env = process.env) {
  // All commands/arguments are fixed above; no user-supplied text is passed to cmd.exe.
  const windowsNpm = process.platform === 'win32' && command === npm
  const result = spawnSync(windowsNpm ? process.env.ComSpec ?? 'cmd.exe' : command,
    windowsNpm ? ['/d', '/s', '/c', [command, ...args].join(' ')] : args,
    { cwd, env, stdio: 'inherit' })
  if (result.error) throw result.error
  if (result.status !== 0) throw new Error(`${command} ${args.join(' ')} failed (${result.status})`)
}

run(npm, ['ci'], ui)
run(npm, ['run', 'build'], ui)
run(npm, ['run', 'lint'], ui)
run(npm, ['test'], ui)
if (!process.argv.includes('--skip-browser')) {
  run(npm, ['exec', '--', 'playwright', 'install', 'chromium'], ui)
  run(npm, ['run', 'test:e2e'], ui)
}
run('dotnet', ['restore', '-r', 'linux-musl-x64'])
run('dotnet', ['build', '--no-restore', '--configuration', 'Release'])
run('dotnet', ['run', '--project', 'test/ServiceBusEmulatorExplorer.Tests/ServiceBusEmulatorExplorer.Tests.csproj', '--configuration', 'Release'], root,
  { ...process.env, OPENAPI_OUTPUT: join(root, 'artifacts', 'openapi.json') })
run(npm, ['run', 'api:generate'], ui)
run('git', ['diff', '--exit-code', 'HEAD', '--', 'app/sb-explorer-ui/src/api/generated.ts'])
console.log('Verification passed. Real-emulator/container integration is a separate CI job.')
