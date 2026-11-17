"""Mac prebuild bootstrap. The managed guard verifies the real UBT ancestor."""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    result = hashlib.sha256()
    with open(path, 'rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def read_json(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, 'Duplicate JSON key: ' + key)
            result[key] = value
        return result
    return json.loads(Path(path).read_text(encoding='utf-8-sig'), object_pairs_hook=unique)


def main():
    parser = argparse.ArgumentParser()
    for option in ('engine', 'plugin', 'project', 'target', 'platform', 'configuration', 'type'):
        parser.add_argument('--' + option, required=True)
    options = parser.parse_args()
    if options.platform != 'Wasm':
        return 0
    require(sys.platform == 'darwin' and options.type == 'Game', 'This bootstrap requires a Mac-hosted Wasm Game.')
    full = lambda value: Path(os.path.abspath(value))
    plugin, engine, project = map(full, (options.plugin, options.engine, options.project))
    script = full(__file__)
    require(plugin == script.parents[3], 'The guard script is not from the requested plugin.')
    require(re.fullmatch(r'[A-Za-z0-9_]+', options.target), 'Invalid Wasm target.')
    require(options.configuration in ('Debug', 'DebugGame', 'Development', 'Shipping', 'Test'), 'Invalid Wasm configuration.')
    projects = [entry for entry in project.iterdir() if entry.is_file() and entry.suffix.lower() == '.uproject']
    require(len(projects) == 1, 'Wasm guard requires one code project in ProjectDir.')
    input_path = project / 'Intermediate/UnrealSharp/Wasm/NativeBuild' / options.target / options.configuration / 'wasm64/BuildInput.json'
    build_input = read_json(input_path)
    profile = build_input['Profile']
    require(build_input['Schema'] == 'UnrealSharp.WasmBuildInput' and build_input['SchemaVersion'] == 1 and
            profile['PluginDirectory'] == str(plugin) and profile['EngineDirectory'] == str(engine) and
            profile['ProjectFile'] == str(projects[0]), 'No matching frozen native-build selection.')
    physical_plugin = plugin.resolve(strict=True)
    require(build_input['PathIdentities'].get(str(plugin)) == str(physical_plugin), 'Plugin ancestor identity differs from BuildInput.')

    def safe(path):
        require(plugin.resolve(strict=True) == physical_plugin, 'Plugin ancestor changed during guard bootstrap.')
        path = full(path)
        require(path.is_relative_to(plugin), 'Guard tool output escaped the plugin.')
        cursor = plugin
        for part in path.relative_to(plugin).parts:
            cursor /= part
            try:
                info = cursor.lstat()
            except FileNotFoundError:
                continue
            require(not stat.S_ISLNK(info.st_mode), 'Unverified link in guard tool output: ' + str(cursor))
        return path

    host = 'mac-arm64' if os.uname().machine == 'arm64' else 'mac-x64'
    dotnet = engine / 'Binaries/ThirdParty/DotNet/10.0' / host / 'dotnet'
    require(dotnet.is_file(), 'The selected UE 5.9 Mac bundled .NET host is missing.')
    guard = plugin / 'Build/Runtime/Wasm/BuildGuard'
    # Include every compiled source and the actual interpreter/bootstrap/host in
    # the tool key; a stale cached guard cannot omit a newly added source file.
    sources = [script, full(sys.executable), dotnet, guard / 'UnrealSharp.Wasm.BuildGuard.csproj',
               plugin / 'Source/ThirdParty/UnrealSharpWasmRuntime/UnrealSharpWasmRuntime.Build.cs', *guard.glob('*.cs')]

    def build_command(output):
        # The guard has no package references or directory-level build contract.
        # Ambient UE/UAT imports can redirect bin/obj outside this keyed lock's
        # owned artifacts directory (for example EpicGamesMsBuild's uatobj).
        return [str(dotnet), 'build', str(guard / 'UnrealSharp.Wasm.BuildGuard.csproj'),
                '--configuration', 'Release', '--artifacts-path', str(output), '--verbosity', 'quiet',
                '-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false',
                '-p:ImportDirectoryPackagesProps=false']

    def source_key():
        require(set(guard.glob('*.cs')) == set(path for path in sources if path.parent == guard and path.suffix == '.cs'),
                'Guard source inventory changed.')
        inputs = {'Sources': [{'Path': str(path), 'Sha256': digest(path)} for path in sorted(sources)],
                  'BuildCommand': build_command('<owned-artifacts-directory>')}
        return hashlib.sha256(json.dumps(inputs, sort_keys=True, separators=(',', ':')).encode('utf-8')).hexdigest()

    key = source_key()
    base = safe(plugin / 'Intermediate/UnrealSharp/BuildGuardTools')
    base.mkdir(parents=True, exist_ok=True)
    safe(base)
    # flock is released by the OS even on a failed or interrupted bootstrap.
    with open(safe(base / (key + '.lock')), 'a+b') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        tool = safe(base / key)
        if not tool.exists():
            temporary = safe(base / (key + '.tmp.' + uuid.uuid4().hex))
            result = subprocess.run(build_command(temporary))
            require(result.returncode == 0, 'The normal managed guard build failed; output is preserved.')
            require(source_key() == key, 'Guard source changed during its managed build.')
            binary_dir = safe(temporary / 'bin/UnrealSharp.Wasm.BuildGuard/release')
            files = [{'Name': path.name, 'Sha256': digest(safe(path))} for path in sorted(binary_dir.iterdir()) if path.is_file()]
            require(any(item['Name'] == 'UnrealSharp.Wasm.BuildGuard.dll' for item in files), 'Guard output is missing.')
            safe(temporary / 'ToolReceipt.json').write_text(json.dumps({'SourceKey': key, 'Files': files}), encoding='utf-8')
            safe(temporary).rename(safe(tool))
        receipt = read_json(safe(tool / 'ToolReceipt.json'))
        require(receipt['SourceKey'] == key and source_key() == key, 'Guard tool source identity changed.')
        binary_dir = safe(tool / 'bin/UnrealSharp.Wasm.BuildGuard/release')
        names = [entry['Name'] for entry in receipt['Files']]
        require(len(names) == len(set(names)) and set(names) == {path.name for path in binary_dir.iterdir() if path.is_file()}, 'Guard output inventory changed.')
        for entry in receipt['Files']:
            require(Path(entry['Name']).name == entry['Name'], 'Invalid guard receipt path.')
            require(digest(safe(binary_dir / entry['Name'])) == entry['Sha256'], 'Guard tool output changed.')
        command = [str(dotnet), str(safe(binary_dir / 'UnrealSharp.Wasm.BuildGuard.dll'))]
        for key, value in (('engine', engine), ('plugin', plugin), ('project', projects[0]), ('target', options.target),
                           ('platform', options.platform), ('configuration', options.configuration), ('type', options.type)):
            command.extend(['--' + key, str(value)])
        return subprocess.run(command).returncode


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as error:
        print('UnrealSharp Wasm guard rejected: ' + str(error), file=sys.stderr)
        sys.exit(1)
