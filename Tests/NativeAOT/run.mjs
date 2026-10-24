// Reproduce the two existing macOS test slots without changing their AutoBuild authority.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { execFileSync, spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const workspace = path.resolve(repo, '../..');
const engine = '/Users/Shared/Epic Games/UE_5.8';
const evidence = path.join(workspace, 'UnrealEngine/Tests/NativeAOT/eden-main');
const [mode, slot, round, ...extra] = process.argv.slice(2);
if (!['A', 'B'].includes(slot) || !/^[a-z0-9-]+$/.test(round ?? '')) {
    throw Error('Usage: node Tests/NativeAOT/run.mjs <preserve|deploy|native|probe|archive|game|uat> <A|B> <unique-round> [args]');
}
const name = slot === 'A' ? 'SharpTestProject' : 'SharpTestProject1';
const project = path.join(workspace, 'UEProjects', name);
const plugin = path.join(project, 'Plugins/UnrealSharp');
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const git = (root, ...args) => execFileSync('git', ['-C', root, ...args], { encoding: 'utf8' }).trim();
const skip = new Set(['.git', 'Binaries', 'Intermediate', 'Saved', 'DerivedDataCache', 'bin', 'obj', '.vs', '.idea']);
function files(root, rel = '') {
    return fs.readdirSync(path.join(root, rel), { withFileTypes: true }).flatMap(entry => {
        const child = path.join(rel, entry.name);
        if (skip.has(entry.name) || entry.isSymbolicLink()) return [];
        if (entry.isDirectory()) return files(root, child);
        if (!entry.isFile() || /\.(uasset|umap)$/i.test(entry.name)) return [];
        return [child];
    });
}
function copy(from, to) {
    if (fs.existsSync(to) && fs.readFileSync(from).equals(fs.readFileSync(to))) return;
    fs.mkdirSync(path.dirname(to), { recursive: true });
    fs.copyFileSync(from, to);
}
function assertIdle() {
    const busy = execFileSync('ps', ['-axo', 'command'], { encoding: 'utf8' }).split('\n').filter(line =>
        /UnrealEditor\.app\/Contents\/MacOS\/UnrealEditor|\/UnrealEditor-Cmd\b|SharpTestProject1?\.app\/Contents\/MacOS\/SharpTestProject1?\b|^\S*dotnet .*UnrealBuildTool\.dll|^\S*dotnet .*AutomationTool\.dll/.test(line));
    if (busy.length) throw Error('Build/Editor occupancy: ' + JSON.stringify(busy));
}
function nativeSources() {
    return [path.join(plugin, 'Source'), path.join(project, 'Source')].flatMap(root =>
        files(root).filter(file => /\.(cpp|h|cs|csproj)$/.test(file)).map(file => ({
            path: path.join(root, file), sha256: hash(fs.readFileSync(path.join(root, file)))
        })));
}
assertIdle();
const dir = path.join(evidence, 'runs', round);
if (fs.existsSync(dir)) throw Error('Evidence round already exists: ' + dir);
fs.mkdirSync(dir, { recursive: true });
const json = (file, value) => fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n');
function requireBuild(target) {
    const success = JSON.parse(fs.readFileSync(path.join(evidence, `${slot}-${target}-success.json`)));
    if (success.exitCode !== 0 || success.engine !== engine) throw Error('Canonical Installed UE build must succeed first');
    if (JSON.stringify(nativeSources()) !== JSON.stringify(success.nativeSources)) throw Error('Native inputs changed since successful ' + target + ' build');
}

if (mode === 'preserve') {
    const backup = path.join(evidence, 'original', slot);
    if (fs.existsSync(backup)) throw Error('Original slot already preserved');
    const paths = ['Source', 'Script', 'Config', 'Plugins/UnrealSharp'].flatMap(rel =>
        files(path.join(project, rel)).map(file => path.join(rel, file)));
    const manifest = paths.map(file => {
        copy(path.join(project, file), path.join(backup, file));
        return { path: file, sha256: hash(fs.readFileSync(path.join(project, file))) };
    });
    json(path.join(backup, 'manifest.json'), manifest);
    console.log(JSON.stringify({ mode, slot, files: manifest.length, backup }));
    process.exit(0);
}

if (mode === 'deploy') {
    if (!fs.existsSync(path.join(evidence, 'original', slot, 'manifest.json'))) throw Error('Preserve slot first');
    const source = slot === 'A' ? path.join(evidence, 'baseline') : repo;
    const selected = [...new Set(git(source, 'ls-files', '-z').split('\0').filter(Boolean))]
        .filter(file => /^(Build|Config|Managed|Source|Resources)\//.test(file) || (!file.includes('/') && /\.(props|targets|json|uplugin)$/.test(file)))
        .filter(file => fs.existsSync(path.join(source, file)));
    const selectedSet = new Set(selected);
    const previousFile = path.join(evidence, slot + '-deployment.json');
    const previous = fs.existsSync(previousFile) ? JSON.parse(fs.readFileSync(previousFile)).files : [];
    const owned = new Set(previous.map(file => file.path));
    const removed = [];
    const retained = [];
    for (const root of ['Build', 'Config', 'Managed', 'Source']) {
        for (const file of files(path.join(plugin, root)).map(file => path.join(root, file))) {
            // Only remove files deployed by this runner; keep pre-existing project fixtures.
            if (!selectedSet.has(file) && /\.(cs|cpp|h|csproj)$/.test(file)) {
                if (owned.has(file)) {
                    fs.unlinkSync(path.join(plugin, file));
                    removed.push(file);
                } else {
                    retained.push({ path: file, sha256: hash(fs.readFileSync(path.join(plugin, file))) });
                }
            }
        }
    }
    const manifest = selected.map(file => {
        copy(path.join(source, file), path.join(plugin, file));
        return { path: file, sha256: hash(fs.readFileSync(path.join(source, file))) };
    });
    const patch = git(source, 'diff', 'HEAD', '--binary');
    fs.writeFileSync(path.join(dir, 'candidate.patch'), patch);
    const identity = { source, head: git(source, 'rev-parse', 'HEAD'), patchSha256: hash(patch), removed, retained, files: manifest };
    json(path.join(dir, 'deployment.json'), identity);
    json(path.join(evidence, slot + '-deployment.json'), identity);
    console.log(JSON.stringify({ mode, slot, head: identity.head, files: manifest.length, removed: removed.length, dir }));
    process.exit(0);
}

let command, args, gate, gameReport, gameLog;
if (mode === 'native') {
    const target = extra[0] ?? 'Editor';
    if (!['Editor', 'Game'].includes(target)) throw Error('Unknown canonical target');
    const configFile = path.join(project, target === 'Editor' ? 'AutoBuild.json' : 'AutoBuild_Game.json');
    const entry = path.join(project, target === 'Editor' ? 'AutoBuild_Mac.sh' : 'AutoBuild_Game_Mac.sh');
    const config = JSON.parse(fs.readFileSync(configFile));
    if (config.engine !== engine || config.cwd !== project || config.project !== path.join(project, name + '.uproject')) throw Error('Unexpected canonical build configuration');
    command = entry;
    args = ['nativeaot-' + round];
    gate = path.join(evidence, `${slot}-${target}-success.json`);
    json(path.join(dir, 'canonical.json'), { config, configSha256: hash(fs.readFileSync(configFile)), entrySha256: hash(fs.readFileSync(entry)) });
} else if (mode === 'uat') {
    if (!['GenerateAOTBootstrap', 'GenerateProject', 'GenerateSolution', 'BuildSolution', 'BuildUserGlue', 'BuildUserSolution', 'PackageProject'].includes(extra[0])) throw Error('Unsupported managed UAT command');
    command = path.join(engine, 'Engine/Build/BatchFiles/RunUAT.sh');
    args = [extra[0], '-ScriptDir=' + project, '-ScriptsForProject=' + path.join(project, name + '.uproject'), '-Project=' + path.join(project, name + '.uproject'), ...extra.slice(1)];
    if (extra[0] === 'GenerateAOTBootstrap') {
        // Test entry only: references the ordinary production assembly, never recompiles its sources under another name.
        const harness = path.join(evidence, 'bootstrap-generation', slot);
        fs.mkdirSync(harness, { recursive: true });
        const source = fs.readFileSync(path.join(repo, 'Tests/NativeAOT/BootstrapGeneration.cs.template'), 'utf8');
        const projectText = fs.readFileSync(path.join(repo, 'Tests/NativeAOT/BootstrapGeneration.csproj.template'), 'utf8')
            .replaceAll('ENGINE_ROOT', engine).replaceAll('PLUGIN_ROOT', plugin);
        for (const [file, text] of [['Driver.cs', source], ['NativeAOTTests.Automation.csproj', projectText]]) {
            const destination = path.join(harness, file);
            if (!fs.existsSync(destination) || fs.readFileSync(destination, 'utf8') !== text) fs.writeFileSync(destination, text);
        }
        args[0] = 'NativeAOTGenerateBootstrap';
        args[1] = '-ScriptDir=' + harness;
    }
} else if (mode === 'probe') {
    requireBuild('Editor');
    command = path.join(engine, 'Engine/Binaries/Mac/UnrealEditor-Cmd');
    args = [path.join(project, name + '.uproject'), '-run=ParameterBufferExamples', '-nullrhi', '-unattended', '-nosound', '-nop4', '-nosplash', '-stdout', '-FullStdOutLogOutput', '-abslog=' + path.join(dir, 'editor.log'), '-TestReport=' + path.join(dir, 'checks.txt'), ...extra];
} else if (mode === 'archive') {
    requireBuild('Editor');
    requireBuild('Game');
    const archive = path.join(evidence, 'archives', round);
    if (fs.existsSync(archive)) throw Error('Archive already exists');
    command = path.join(engine, 'Engine/Build/BatchFiles/RunUAT.sh');
    // Cooking/staging only. All native compilation belongs to the existing AutoBuild entries.
    args = ['BuildCookRun', '-ScriptsForProject=' + path.join(project, name + '.uproject'), '-ScriptDir=' + project,
        '-project=' + path.join(project, name + '.uproject'), '-nop4', '-unattended', '-utf8output',
        '-skipbuild', '-skipbuildeditor', '-nocompileeditor', '-cook', '-stage', '-pak', '-package', '-archive',
        '-archivedirectory=' + archive, '-target=' + name, '-platform=Mac', '-map=/Engine/Maps/Entry', '-clientconfig=Development'];
} else if (mode === 'game') {
    requireBuild('Game');
    const archiveRound = extra[0];
    if (!/^[a-z0-9-]+$/.test(archiveRound ?? '')) throw Error('Supply archive round');
    const archiveResult = JSON.parse(fs.readFileSync(path.join(evidence, 'runs', archiveRound, 'result.json')));
    if (archiveResult.exitCode !== 0 || archiveResult.engine !== engine || archiveResult.slot !== slot
        || JSON.stringify(archiveResult.nativeSources) !== JSON.stringify(nativeSources())) throw Error('Archive does not match successful build');
    command = path.join(evidence, 'archives', archiveRound, 'Mac', name + '.app/Contents/MacOS', name);
    const bundle = path.resolve(path.dirname(command), '../Info.plist');
    const bundleId = execFileSync('plutil', ['-extract', 'CFBundleIdentifier', 'raw', '-o', '-', bundle], { encoding: 'utf8' }).trim();
    if (!/^[A-Za-z0-9.-]+$/.test(bundleId)) throw Error('Unexpected bundle identifier');
    const saved = path.join(os.homedir(), 'Library/Containers', bundleId, 'Data/Library/Application Support/Epic', name, 'Saved/NativeAOT');
    gameReport = path.join(saved, round + '-checks.txt');
    gameLog = path.join(saved, round + '-game.log');
    if (fs.existsSync(gameReport) || fs.existsSync(gameLog)) throw Error('Game evidence already exists');
    args = ['-ParameterBufferTests', '-nullrhi', '-unattended', '-nosound', '-nop4', '-nosplash', '-stdout',
        '-FullStdOutLogOutput', '-abslog=' + gameLog, '-TestReport=' + gameReport, ...extra.slice(1)];
} else throw Error('Unknown mode: ' + mode);

const environmentOverrides = mode === 'game' ? { DYLD_PRINT_LIBRARIES: '1' } : {};
const identity = { mode, slot, engine, command, args, cwd: project, environmentOverrides, started: new Date().toISOString(), nativeSources: nativeSources() };
json(path.join(dir, 'command.json'), identity);
const log = fs.createWriteStream(path.join(dir, 'console.log'));
let pending = '';
const output = bytes => {
    log.write(bytes);
    pending += bytes;
    const lines = pending.split('\n'); pending = lines.pop();
    for (const line of lines) if (/error|exception|Result:|BUILD SUCCESS|BUILD FAILED|ROUND RESULT|^RESULT |NATIVEAOT-|TOTAL .*PASS|ParameterBufferExamples exit/i.test(line)) console.log(line.slice(0, 1600));
};
const start = performance.now();
const child = spawn(command, args, { cwd: project, stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, ...environmentOverrides } });
child.stdout.on('data', output); child.stderr.on('data', output);
child.on('error', error => console.error(error));
child.on('close', (code, signal) => log.end(() => {
    const processExitCode = code;
    let checksPassed;
    if (mode === 'game' || mode === 'probe') {
        if (gameReport && fs.existsSync(gameReport)) copy(gameReport, path.join(dir, 'checks.txt'));
        if (gameLog && fs.existsSync(gameLog)) copy(gameLog, path.join(dir, 'game.log'));
        const report = path.join(dir, 'checks.txt');
        const checks = fs.existsSync(report) ? fs.readFileSync(report, 'utf8') : '';
        checksPassed = /TOTAL \d+ PASS/.test(checks) && !/^(FAIL|ERROR)\b/m.test(checks);
        if (!checksPassed && code === 0) code = 1;
    }
    const result = { ...identity, exitCode: code, processExitCode, checksPassed, signal, seconds: (performance.now() - start) / 1000 };
    json(path.join(dir, 'result.json'), result);
    if (code === 0 && gate) json(gate, result);
    console.log('NATIVEAOT RESULT ' + JSON.stringify({ mode, slot, exitCode: code, signal, seconds: result.seconds, dir }));
    process.exitCode = code ?? 1;
}));
