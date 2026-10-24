// Drive one managed-only change through the normal Installed Editor file watcher.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';

const here = path.dirname(fileURLToPath(import.meta.url));
const [slot, round, expectedText, ...files] = process.argv.slice(2);
const expected = Number(expectedText);
if (!['A', 'B'].includes(slot) || !/^[a-z0-9-]+$/.test(round ?? '') || !Number.isInteger(expected) || expected < 1 ||
    !files.length || files.some(file => !/^[A-Za-z0-9]+\.cs$/.test(file))) {
    throw Error('Usage: node Tests/NativeAOT/live.mjs A|B <unique-round> <expected-checks> <changed-fixture.cs>...');
}
const workspace = '/Users/shuyangzhang/Workspace';
const dir = path.join(workspace, 'UnrealEngine/Tests/NativeAOT/eden-main/runs', round);
if (fs.existsSync(dir)) throw Error('Evidence round already exists: ' + dir);
const project = path.join(workspace, 'UEProjects', slot === 'A' ? 'SharpTestProject' : 'SharpTestProject1');
const copies = files.map(file => [path.join(here, 'Fixtures/Script/PackagingManagedProbe', file), path.join(project, 'Script/PackagingManagedProbe', file)]);
if (copies.every(([source, target]) => fs.existsSync(target) && fs.readFileSync(source).equals(fs.readFileSync(target)))) {
    throw Error('No changed managed fixture; cannot claim a hot-reload check');
}
const editor = spawn(process.execPath, [path.join(here, 'run.mjs'), 'editor', slot, round], { stdio: 'inherit' });
let exited = false;
const completion = new Promise((resolve, reject) => {
    editor.once('error', reject);
    editor.once('exit', (code, signal) => { exited = true; resolve({ code, signal }); });
});
const read = file => fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : '';
const log = () => read(path.join(dir, 'editor.log'));
async function until(predicate, seconds, stage) {
    const deadline = Date.now() + seconds * 1000;
    while (Date.now() < deadline) {
        if (predicate()) return;
        if (exited) throw Error('Editor exited during ' + stage);
        await new Promise(resolve => setTimeout(resolve, 500));
    }
    throw Error('Timed out during ' + stage + '; evidence retained at ' + dir);
}
function request(id, action) {
    const target = path.join(dir, 'request.json');
    fs.writeFileSync(target + '.tmp', JSON.stringify({ id, action }));
    fs.renameSync(target + '.tmp', target);
}
let failure;
try {
    await until(() => fs.existsSync(path.join(dir, 'python-ready.json')) && log().includes('C# Hot Reload is ready.'), 120, 'Editor startup');
    const start = log().length;
    fs.mkdirSync(path.join(dir, 'fixtures'));
    const manifest = copies.map(([source, target]) => {
        const bytes = fs.readFileSync(source);
        fs.writeFileSync(path.join(dir, 'fixtures', path.basename(source)), bytes);
        fs.writeFileSync(target, bytes);
        return { source, target, sha256: crypto.createHash('sha256').update(bytes).digest('hex') };
    });
    fs.writeFileSync(path.join(dir, 'fixture-manifest.json'), JSON.stringify(manifest, null, 2));
    console.log('Managed fixture change copied after Editor readiness.');
    await until(() => {
        const recent = log().slice(start);
        if (/error CS\d+|C# Hot Reload failed/i.test(recent)) throw Error('Managed build/reload failed; inspect ' + dir);
        return recent.includes('C# Hot Reload completed');
    }, 120, 'managed hot reload');
    request('after-reload', 'run');
    await until(() => fs.existsSync(path.join(dir, 'after-reload.json')), 60, 'reflected tests');
    const result = JSON.parse(read(path.join(dir, 'after-reload.json')));
    if (!result.passed || result.checks !== expected) throw Error('Unexpected test result: ' + JSON.stringify(result));
    if (/Handled ensure|Assertion failed|=== Critical error/.test(log().slice(start))) {
        throw Error('Native ensure/assertion during reload or tests; inspect ' + dir);
    }
    console.log('LIVE PASS ' + slot + ' ' + expected + ' checks');
} catch (error) {
    failure = error;
} finally {
    if (!exited && fs.existsSync(path.join(dir, 'python-ready.json'))) request('close-live-probe', 'quit');
}
const result = await completion;
if (failure) throw failure;
if (result.code !== 0) throw Error('Editor did not exit successfully: ' + JSON.stringify(result));
