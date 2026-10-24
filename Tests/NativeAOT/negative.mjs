// Exercise the real Game loader with disposable bad managed NativeAOT libraries.
// No native Unreal compilation; restore the task-owned package payload byte-for-byte.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { execFileSync, spawn } from 'node:child_process';

const here = path.dirname(fileURLToPath(import.meta.url));
const [round, archive] = process.argv.slice(2);
if (![round, archive].every(value => /^[a-z0-9-]+$/.test(value ?? ''))) {
    throw Error('Usage: node Tests/NativeAOT/negative.mjs <unique-round> <B-archive-round>');
}
const root = '/Users/shuyangzhang/Workspace/UnrealEngine/Tests/NativeAOT/eden-main';
const dir = path.join(root, 'runs', round);
if (fs.existsSync(dir)) throw Error('Evidence round already exists');
const payload = path.join(root, 'archives', archive,
    'Mac/SharpTestProject1.app/Contents/UE/SharpTestProject1/Binaries/Managed/net10.0/SharpTestProject1.AOT.dylib');
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
function idle() {
    const busy = execFileSync('ps', ['-axo', 'command'], { encoding: 'utf8' }).split('\n').filter(line =>
        /UnrealEditor\.app\/Contents\/MacOS\/UnrealEditor|\/UnrealEditor-Cmd\b|SharpTestProject1?\.app\/Contents\/MacOS\/SharpTestProject1?\b|^\S*dotnet .*UnrealBuildTool\.dll|^\S*dotnet .*AutomationTool\.dll/.test(line));
    if (busy.length) throw Error('Build/Editor occupancy: ' + JSON.stringify(busy));
}
async function run(command, args, cwd, logFile) {
    fs.writeFileSync(logFile + '.command.json', JSON.stringify({ command, args, cwd }, null, 2));
    const output = fs.createWriteStream(logFile);
    const child = spawn(command, args, { cwd, stdio: ['ignore', 'pipe', 'pipe'] });
    child.stdout.pipe(output, { end: false });
    child.stderr.pipe(output, { end: false });
    return new Promise((resolve, reject) => {
        child.once('error', reject);
        child.once('close', (code, signal) => output.end(() => resolve({ code, signal })));
    });
}
idle();
fs.mkdirSync(dir, { recursive: true });
const backup = path.join(dir, 'production.AOT.dylib');
fs.copyFileSync(payload, backup);
const originalHash = hash(backup);
const results = [];
try {
    for (const [kind, expected] of [
        ['invalid-library', 'Failed to load NativeAOT library'],
        ['missing-export', 'NativeAOT library is missing InitializeAotRuntime'],
        ['initialization-failure', 'Expected initialization failure: 中文 é'],
    ]) {
        const fixture = path.join(dir, kind);
        fs.mkdirSync(fixture);
        const library = path.join(fixture, 'publish/NegativeFixture.dylib');
        if (kind !== 'invalid-library') {
            fs.writeFileSync(path.join(fixture, 'NegativeFixture.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><PublishAot>true</PublishAot><NativeLib>Shared</NativeLib><AllowUnsafeBlocks>true</AllowUnsafeBlocks><AssemblyName>NegativeFixture</AssemblyName></PropertyGroup></Project>`);
            const body = kind === 'missing-export'
                ? '[UnmanagedCallersOnly(EntryPoint="UnrelatedExport")] public static int Probe() => 0;'
                : '[UnmanagedCallersOnly(EntryPoint="InitializeAotRuntime")] public static void Initialize(byte* path, void* plugins, void* binds, void* callbacks, byte* result) { result[0] = 0; byte[] text = Encoding.UTF8.GetBytes("Expected initialization failure: 中文 é\\0"); for (int i = 0; i < text.Length; i++) result[i + 1] = text[i]; }';
            fs.writeFileSync(path.join(fixture, 'Entry.cs'), 'using System.Runtime.InteropServices; using System.Text; public static unsafe class Entry { ' + body + ' }');
            const build = await run('dotnet', ['publish', 'NegativeFixture.csproj', '-r', 'osx-arm64', '-c', 'Release', '-o', 'publish', '--nologo'], fixture, path.join(fixture, 'publish.log'));
            if (build.code !== 0) throw Error('Negative fixture publish failed: ' + kind);
        }
        idle();
        if (kind === 'invalid-library') fs.writeFileSync(payload, 'Deliberately invalid task-owned NativeAOT fixture\n');
        else fs.copyFileSync(library, payload);
        const fixtureHash = hash(payload);
        const gameRound = round + '-' + kind;
        console.log('Testing loader boundary: ' + kind);
        const execution = await run(process.execPath, [path.join(here, 'run.mjs'), 'game', 'B', gameRound, archive, '-NoLogThread', '-forcelogflush'], path.resolve(here, '../..'), path.join(fixture, 'runner.log'));
        const gameDir = path.join(root, 'runs', gameRound);
        const consoleLog = fs.readFileSync(path.join(gameDir, 'console.log'), 'utf8');
        const fileLog = path.join(gameDir, 'game.log');
        const log = consoleLog + (fs.existsSync(fileLog) ? fs.readFileSync(fileLog, 'utf8') : '');
        const result = JSON.parse(fs.readFileSync(path.join(gameDir, 'result.json')));
        const passed = execution.code !== 0 && result.checksPassed === false && log.includes(expected)
            && !/dyld.*(?:libhostfxr|libcoreclr|libhostpolicy)/i.test(log);
        results.push({ kind, fixtureHash, expected, passed, execution, gameResult: result.exitCode });
        fs.writeFileSync(path.join(dir, 'results.json'), JSON.stringify(results, null, 2));
        if (!passed) throw Error('Unexpected loader result: ' + kind);
        idle();
        fs.copyFileSync(backup, payload);
        console.log('NEGATIVE PASS ' + kind);
    }
} finally {
    idle();
    fs.copyFileSync(backup, payload);
    const restoredHash = hash(payload);
    fs.writeFileSync(path.join(dir, 'restored.json'), JSON.stringify({ payload, originalHash, restoredHash, identical: originalHash === restoredHash }, null, 2));
    if (restoredHash !== originalHash) throw Error('Production payload restoration failed');
}
