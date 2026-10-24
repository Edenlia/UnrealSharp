// Reproduce the two existing macOS test slots without changing their AutoBuild authority.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync, spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const workspace = path.resolve(repo, '../..');
const engine = path.join(workspace, 'UnrealEngine');
const evidence = path.join(engine, 'Tests/NativeAOT/eden-main');
const [mode, slot, round, ...extra] = process.argv.slice(2);
if (!['A', 'B'].includes(slot) || !/^[a-z0-9-]+$/.test(round ?? '')) {
    throw Error('Usage: node Tests/NativeAOT/run.mjs <preserve|deploy|native|probe|uat> <A|B> <unique-round> [args]');
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
    fs.mkdirSync(path.dirname(to), { recursive: true });
    fs.copyFileSync(from, to);
}
function assertIdle() {
    const busy = execFileSync('ps', ['-axo', 'command'], { encoding: 'utf8' }).split('\n').filter(line =>
        /UnrealEditor\.app\/Contents\/MacOS\/UnrealEditor|\/UnrealEditor-Cmd\b|^\S*dotnet .*UnrealBuildTool\.dll|^\S*dotnet .*AutomationTool\.dll/.test(line));
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
    const removed = [];
    for (const root of ['Build', 'Config', 'Managed', 'Source']) {
        for (const file of files(path.join(plugin, root)).map(file => path.join(root, file))) {
            // Preserve generated local configuration; remove only obsolete compilation inputs.
            if (!selectedSet.has(file) && /\.(cs|cpp|h|csproj)$/.test(file)) {
                fs.unlinkSync(path.join(plugin, file));
                removed.push(file);
            }
        }
    }
    const manifest = selected.map(file => {
        copy(path.join(source, file), path.join(plugin, file));
        return { path: file, sha256: hash(fs.readFileSync(path.join(source, file))) };
    });
    const patch = git(source, 'diff', 'HEAD', '--binary');
    fs.writeFileSync(path.join(dir, 'candidate.patch'), patch);
    const identity = { source, head: git(source, 'rev-parse', 'HEAD'), patchSha256: hash(patch), removed, files: manifest };
    json(path.join(dir, 'deployment.json'), identity);
    json(path.join(evidence, slot + '-deployment.json'), identity);
    console.log(JSON.stringify({ mode, slot, head: identity.head, files: manifest.length, removed: removed.length, dir }));
    process.exit(0);
}

let command, args, gate;
if (mode === 'native') {
    const target = extra[0] ?? 'Editor';
    if (!['Editor', 'Game', 'Shipping', 'Server', 'Client'].includes(target)) throw Error('Unknown canonical target');
    const configFile = path.join(project, `AutoBuild_Source_${target}.json`);
    const entry = path.join(project, `AutoBuild_Source_${target}_Mac.sh`);
    const config = JSON.parse(fs.readFileSync(configFile));
    if (config.engine !== engine || config.cwd !== project || config.project !== path.join(project, name + '.uproject')) throw Error('Unexpected canonical build configuration');
    command = entry;
    args = ['nativeaot-' + round];
    gate = path.join(evidence, `${slot}-${target}-success.json`);
    json(path.join(dir, 'canonical.json'), { config, configSha256: hash(fs.readFileSync(configFile)), entrySha256: hash(fs.readFileSync(entry)) });
} else if (mode === 'uat') {
    if (!['GenerateAOTBootstrap', 'GenerateProject', 'GenerateSolution', 'BuildSolution', 'BuildUserGlue', 'BuildUserSolution', 'PackageProject'].includes(extra[0])) throw Error('Unsupported managed UAT command');
    const harness = path.join(evidence, 'automation', slot);
    fs.mkdirSync(harness, { recursive: true });
    const writeChanged = (file, text) => {
        if (!fs.existsSync(file) || fs.readFileSync(file, 'utf8') !== text) fs.writeFileSync(file, text);
    };
    const productionProject = path.join(plugin, 'Build/Scripts/UnrealSharp.Automation.csproj');
    let projectText = fs.readFileSync(productionProject, 'utf8');
    projectText = projectText.replace('<Project Sdk="Microsoft.NET.Sdk">', `<Project Sdk="Microsoft.NET.Sdk">\n<PropertyGroup><EngineDir>${engine}/Engine</EngineDir><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>`)
        .replaceAll('..\\..\\UnrealSharp.Automation.props', plugin + '/UnrealSharp.Automation.props')
        .replace('<AssemblyName>UnrealSharp.Automation</AssemblyName>', `<AssemblyName>NativeAOTSlot${slot}.Automation</AssemblyName>`)
        .replace(/<Compile Include="\.\.\\\.\.\\Managed\\Shared\\\*\.cs"[^>]*\/>/, `<Compile Include="${plugin}/Managed/Shared/*.cs" />`)
        .replace('</Project>', `<ItemGroup><Compile Include="${plugin}/Build/Scripts/**/*.cs" Exclude="${plugin}/Build/Scripts/**/bin/**;${plugin}/Build/Scripts/**/obj/**" /><Compile Include="Driver.cs" /></ItemGroup>\n</Project>`);
    writeChanged(path.join(harness, `NativeAOTSlot${slot}.Automation.csproj`), projectText);
    writeChanged(path.join(harness, 'Driver.cs'), fs.readFileSync(path.join(repo, 'Tests/NativeAOT/AutomationDriver.cs.template'), 'utf8').replace('NativeAOTValidateSLOT', 'NativeAOTValidate' + slot));
    json(path.join(dir, 'automation-sources.json'), files(path.join(plugin, 'Build/Scripts')).filter(file => file.endsWith('.cs')).map(file => ({ path: file, sha256: hash(fs.readFileSync(path.join(plugin, 'Build/Scripts', file))) })));
    command = path.join(engine, 'Engine/Build/BatchFiles/RunUAT.sh');
    args = ['NativeAOTValidate' + slot, '-NativeAOTAction=' + extra[0], '-ScriptDir=' + harness, '-ScriptsForProject=' + path.join(project, name + '.uproject'), '-Project=' + path.join(project, name + '.uproject'), ...extra.slice(1)];
} else if (mode === 'probe') {
    const success = JSON.parse(fs.readFileSync(path.join(evidence, slot + '-Editor-success.json')));
    if (success.exitCode !== 0) throw Error('Canonical build must succeed first');
    if (JSON.stringify(nativeSources()) !== JSON.stringify(success.nativeSources)) throw Error('Native inputs changed since successful build');
    command = path.join(engine, 'Engine/Binaries/Mac/UnrealEditor-Cmd');
    args = [path.join(project, name + '.uproject'), '-run=ParameterBufferExamples', '-nullrhi', '-unattended', '-nosound', '-nop4', '-nosplash', '-stdout', '-FullStdOutLogOutput', '-abslog=' + path.join(dir, 'editor.log'), '-TestReport=' + path.join(dir, 'checks.txt'), ...extra];
} else throw Error('Unknown mode: ' + mode);

const identity = { mode, slot, command, args, cwd: project, started: new Date().toISOString(), nativeSources: nativeSources() };
json(path.join(dir, 'command.json'), identity);
const log = fs.createWriteStream(path.join(dir, 'console.log'));
let pending = '';
const output = bytes => {
    log.write(bytes);
    pending += bytes;
    const lines = pending.split('\n'); pending = lines.pop();
    for (const line of lines) if (/error|exception|Result:|BUILD SUCCESS|BUILD FAILED|Compile \[|Link \[|ROUND RESULT|^RESULT |NATIVEAOT-|TOTAL .*PASS|ParameterBufferExamples exit/i.test(line)) console.log(line.slice(0, 1600));
};
const start = performance.now();
const child = spawn(command, args, { cwd: project, stdio: ['ignore', 'pipe', 'pipe'] });
child.stdout.on('data', output); child.stderr.on('data', output);
child.on('error', error => console.error(error));
child.on('close', (code, signal) => log.end(() => {
    const result = { ...identity, exitCode: code, signal, seconds: (performance.now() - start) / 1000 };
    json(path.join(dir, 'result.json'), result);
    if (code === 0 && gate) json(gate, result);
    console.log('NATIVEAOT RESULT ' + JSON.stringify({ mode, slot, exitCode: code, signal, seconds: result.seconds, dir }));
    process.exitCode = code ?? 1;
}));
