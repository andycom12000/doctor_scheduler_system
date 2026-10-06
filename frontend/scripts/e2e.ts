/**
 * 畫面層 E2E 的總控（issue #84）：`npm run e2e`。
 *
 * 一次做完四件事，而且不論成功失敗都收乾淨：
 *   1. 在系統暫存目錄建一顆全新的 SQLite（不種參考名單，名冊是空的）
 *   2. 起真後端：直接 `dotnet Scheduler.Api.dll`（不用 `dotnet run`：少一層 process、不吃 launchSettings 的 :5080）
 *   3. 起 vite dev server（`--strictPort`，proxy 指到上面的後端）
 *   4. 跑 Playwright；結束後殺掉兩個伺服器（整棵程序樹／程序群組）、刪資料庫、確認兩個 port 真的放掉
 *
 * 不用 Playwright 的 `webServer` 設定：Windows 上它殺 `npm run` 不一定殺得到 vite／dotnet 子程序，
 * 而且 teardown 在伺服器停之前跑，刪不掉還被鎖住的 SQLite 檔。
 *
 * port 固定（後端 5180、前端 5280），刻意避開開發者自己的 5080／5173。
 * 起跑前先確認兩個 port 沒人佔，有人佔就直接失敗，不默默接上別人的伺服器。
 *
 * 伺服器輸出平常丟掉（stdio ignore，不會有 pipe 塞滿的問題）；`E2E_VERBOSE=1` 才轉出來。
 * 清理（殺程序、刪暫存目錄）一律在 `finally`；收到 SIGINT／SIGTERM 只設旗標並殺子程序，讓 main 自己走完 `finally`。
 */
import { spawn, spawnSync, type ChildProcess } from 'node:child_process'
import { existsSync, mkdtempSync, rmSync } from 'node:fs'
import { createConnection } from 'node:net'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const API_PORT = 5180
const WEB_PORT = 5280
const HOST = '127.0.0.1' // 不用 localhost：Node 可能解到 ::1，proxy 會打空
const VERBOSE = Boolean(process.env.E2E_VERBOSE)

const frontendDir = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const apiDll = resolve(frontendDir, '../src/Scheduler.Api/bin/Release/net8.0/Scheduler.Api.dll')
const viteBin = join(frontendDir, 'node_modules/vite/bin/vite.js')
const playwrightCli = join(frontendDir, 'node_modules/@playwright/test/cli.js')

const children: ChildProcess[] = []
let stopping = false

function log(message: string): void {
  console.log(`[e2e] ${message}`)
}

function isListening(port: number): Promise<boolean> {
  return new Promise((done) => {
    const socket = createConnection({ port, host: HOST })
    socket.once('connect', () => {
      socket.destroy()
      done(true)
    })
    socket.once('error', () => done(false))
  })
}

/** 輪詢直到 probe 成立；給了 watch 的子程序一旦結束就立刻失敗，不空等到逾時。 */
async function waitFor(what: string, probe: () => Promise<boolean>, timeoutMs: number, watch?: ChildProcess): Promise<void> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (stopping) throw new Error('收到中止訊號')
    if (watch && (watch.exitCode !== null || watch.signalCode !== null)) {
      throw new Error(`等 ${what} 時，伺服器已結束（exit ${watch.exitCode ?? watch.signalCode}）；用 E2E_VERBOSE=1 看輸出`)
    }
    if (await probe()) return
    await new Promise((r) => setTimeout(r, 250))
  }
  throw new Error(`等不到 ${what}（${timeoutMs / 1000} 秒）`)
}

async function healthOk(): Promise<boolean> {
  try {
    return (await fetch(`http://${HOST}:${API_PORT}/api/health`)).ok
  } catch {
    return false
  }
}

function start(name: string, command: string, args: string[], env: NodeJS.ProcessEnv): ChildProcess {
  const child = spawn(command, args, {
    cwd: frontendDir,
    env: { ...process.env, ...env },
    stdio: VERBOSE ? ['ignore', 'pipe', 'pipe'] : 'ignore',
    // 非 Windows：自成程序群組，才能整組殺（Windows 用 taskkill /T）
    detached: process.platform !== 'win32',
  })
  if (VERBOSE) {
    // 持續讀，pipe 不會塞滿
    const forward = (d: Buffer) => process.stdout.write(`[${name}] ${d}`)
    child.stdout?.on('data', forward)
    child.stderr?.on('data', forward)
  }
  child.once('error', (err) => log(`${name} 啟動失敗：${err.message}`))
  child.once('exit', (code) => {
    if (code !== null && code !== 0 && !stopping) log(`${name} 意外結束（exit ${code}）`)
  })
  children.push(child)
  return child
}

/** 殺整棵程序樹（Windows）／整個程序群組（其他平台）。 */
function killTree(child: ChildProcess): void {
  if (child.pid === undefined || child.exitCode !== null) return
  if (process.platform === 'win32') {
    spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' })
  } else {
    try {
      process.kill(-child.pid, 'SIGKILL')
    } catch {
      child.kill('SIGKILL')
    }
  }
}

function killAll(): void {
  for (const child of children) killTree(child)
}

/** 非同步跑 Playwright（不卡 event loop），回傳 exit code。 */
function runPlaywright(): Promise<number> {
  return new Promise((done) => {
    const child = spawn(process.execPath, [playwrightCli, 'test', ...process.argv.slice(2)], {
      cwd: frontendDir,
      stdio: 'inherit',
      env: { ...process.env, E2E_BASE_URL: `http://${HOST}:${WEB_PORT}` },
    })
    children.push(child)
    child.once('error', (err) => {
      log(`Playwright 啟動失敗：${err.message}`)
      done(1)
    })
    child.once('exit', (code) => done(code ?? 1))
  })
}

async function main(): Promise<number> {
  for (const [what, file] of [['後端 dll（請用 npm run e2e，它會先 dotnet build）', apiDll], ['vite', viteBin], ['Playwright', playwrightCli]] as const) {
    if (!existsSync(file)) throw new Error(`找不到${what}：${file}`)
  }
  for (const port of [API_PORT, WEB_PORT]) {
    if (await isListening(port)) throw new Error(`port ${port} 已有程序在聽，E2E 不會接上別人的伺服器；請先關掉它再跑`)
  }

  const scratch = mkdtempSync(join(tmpdir(), 'scheduler-e2e-'))
  const dbPath = join(scratch, 'scheduler.db')
  log(`全新資料庫：${dbPath}`)

  try {
    const api = start('api', 'dotnet', [apiDll], {
      ASPNETCORE_URLS: `http://${HOST}:${API_PORT}`,
      SCHEDULER_DATABASE_PATH: dbPath,
      SCHEDULER_SEED_REFERENCE_ROSTER: 'false',
      // 行事曆自動更新會連外網；E2E 要可重現、不依賴網路（#112）
      SCHEDULER_CALENDAR_AUTO_SYNC: 'false',
      // EF Core 每個 SQL 都會記 info，E2E 的 log 太吵
      'Logging__LogLevel__Microsoft.EntityFrameworkCore': 'Warning',
      // 後端本身沒有業務 log；verbose 時打開請求紀錄，才看得到求解、發布等呼叫
      ...(VERBOSE ? { 'Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics': 'Information' } : {}),
    })
    await waitFor('後端 /api/health', healthOk, 60_000, api)
    log(`後端就緒 :${API_PORT}`)

    const web = start('web', process.execPath, [viteBin, '--host', HOST, '--port', String(WEB_PORT), '--strictPort'], {
      API_PROXY_TARGET: `http://${HOST}:${API_PORT}`,
    })
    await waitFor('vite dev server', () => isListening(WEB_PORT), 60_000, web)
    log(`前端就緒 :${WEB_PORT}`)

    return await runPlaywright()
  } finally {
    stopping = true
    killAll()
    // 等 port 真的放掉再刪檔：SQLite 檔還被 dotnet 鎖著時刪不掉
    try {
      const deadline = Date.now() + 15_000
      while ((await isListening(API_PORT)) || (await isListening(WEB_PORT))) {
        if (Date.now() > deadline) throw new Error('5180／5280 沒有放掉')
        await new Promise((r) => setTimeout(r, 250))
      }
    } catch (err) {
      log(`警告：${(err as Error).message}，可能有殘留程序，請檢查 netstat`)
      process.exitCode = 1
    }
    rmSync(scratch, { recursive: true, force: true, maxRetries: 10, retryDelay: 300 })
    log('已收掉伺服器並刪除暫存資料庫')
  }
}

// 只設旗標並殺子程序；main 的 waitFor／runPlaywright 會因此結束，清理交給 finally
for (const signal of ['SIGINT', 'SIGTERM'] as const) {
  process.on(signal, () => {
    stopping = true
    killAll()
    process.exitCode = 130
  })
}

main().then(
  (code) => {
    process.exit(process.exitCode ?? code)
  },
  (err) => {
    console.error(`[e2e] ${(err as Error).message}`)
    process.exit(process.exitCode ?? 1)
  },
)
