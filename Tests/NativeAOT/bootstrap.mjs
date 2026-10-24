// Publish the production-generated export against the real runtime projects, then call it.
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const workspace = path.resolve(repo, '../..');
const round = process.argv[2];
if (!/^[a-z0-9-]+$/.test(round ?? '')) throw Error('Supply a unique round name');
const root = path.join(workspace, 'UnrealEngine/Tests/NativeAOT/eden-main/runs', round);
if (fs.existsSync(root)) throw Error('Evidence already exists');
const library = path.join(root, 'library');
const caller = path.join(root, 'caller');
fs.mkdirSync(library, { recursive: true });
fs.mkdirSync(caller);
const plugin = path.join(workspace, 'UEProjects/SharpTestProject1/Plugins/UnrealSharp');
const commands = [];
async function run(name, command, args) {
    const record = { name, command, args, cwd: root, started: new Date().toISOString() };
    commands.push(record);
    const log = fs.createWriteStream(path.join(root, name + '.log'));
    const start = performance.now();
    const code = await new Promise((resolve, reject) => {
        const child = spawn(command, args, { cwd: root, stdio: ['ignore', 'pipe', 'pipe'] });
        const output = bytes => { log.write(bytes); };
        child.stdout.on('data', output); child.stderr.on('data', output);
        child.on('error', reject);
        child.on('close', code => log.end(() => resolve(code)));
    });
    Object.assign(record, { exitCode: code, seconds: (performance.now() - start) / 1000 });
    fs.writeFileSync(path.join(root, 'commands.json'), JSON.stringify(commands, null, 2));
    console.log(JSON.stringify(record));
    if (code !== 0) throw Error(name + ' failed; see its preserved log');
}

await run('generate', process.execPath, [path.join(here, 'run.mjs'), 'uat', 'B', round + '-source', 'GenerateAOTBootstrap', '-OutputDirectory=' + library]);
fs.copyFileSync(path.join(here, 'BootstrapProbe.cs.template'), path.join(library, 'Probe.cs'));
fs.writeFileSync(path.join(library, 'BootstrapExport.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks><PublishAot>true</PublishAot><NativeLib>Shared</NativeLib>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="${plugin}/Managed/UnrealSharp/UnrealSharp.Plugins/UnrealSharp.Plugins.csproj" /></ItemGroup>
</Project>`);
await run('publish', 'dotnet', ['publish', path.join(library, 'BootstrapExport.csproj'), '-c', 'Release', '-r', 'osx-arm64', '--artifacts-path', path.join(root, 'artifacts'), '-o', path.join(root, 'publish'), '-p:UseDefaultOutputPath=true', '-p:BuildingAOT=true', '-p:UETargetType=Editor']);
fs.copyFileSync(path.join(here, 'BootstrapCaller.cs.template'), path.join(caller, 'Program.cs'));
fs.writeFileSync(path.join(caller, 'Caller.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><AllowUnsafeBlocks>true</AllowUnsafeBlocks></PropertyGroup></Project>`);
await run('call', 'dotnet', ['run', '--project', path.join(caller, 'Caller.csproj'), '-c', 'Release', '--', path.join(root, 'publish/BootstrapExport.dylib')]);
const result = fs.readFileSync(path.join(root, 'call.log'), 'utf8');
if (!result.includes('BOOTSTRAP PASS')) throw Error('Missing final success marker');
console.log(result);
