import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

W = Path(r'C:\Users\edenlia\Workspace')
REPO = W / 'CodeProjects/UnrealSharp'
ROOT = W / 'CodeProjects/UnrealSharp-reports/csharp-package'
R = ROOT / 'amend-drop-resolver-20261025'
TOOLS = W / 'UEProjects/SharpTestProject56_1/Tools'
BASE = 'cb8577e3a47b5bdc5454239a17f61a6282a50ae2'
PRE = '777b14237afbcfea740458d1643f433ef7769751'
HEAD = subprocess.check_output(['git', '-C', str(REPO), 'rev-parse', 'featre/csharp-package'], text=True).strip()
SHELL = shutil.which('pwsh')

def save(name, data):
    (R / name).write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding='utf-8')

def project(version, slot):
    name = ('SharpTestProject' + ('1' if slot == '2' else '')) if version == '58' else f'SharpTestProject{version}_{slot}'
    return W / 'UEProjects' / name

def manifest(root, suffixes=None):
    if not root.exists(): return {}
    files = []
    for folder, directories, names in os.walk(root):
        if suffixes is not None: directories[:] = [d for d in directories if d not in ('bin', 'obj')]
        files.extend(Path(folder) / name for name in names if suffixes is None or Path(name).suffix in suffixes)
    def item(path):
        return path.relative_to(root).as_posix(), {'size': path.stat().st_size, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
    with ThreadPoolExecutor(max_workers=8) as pool:
        return dict(pool.map(item, sorted(files)))

def execute(version, slot, operation, label, expected=0):
    cmd = [SHELL, '-NoProfile', '-File', str(TOOLS / 'InvokeAmendPackaging.ps1'), '-Version', version, '-Slot', slot, '-Operation', operation, '-Label', label]
    if version == '56' and slot == '2': cmd.append('-LegacyEntry')
    result = subprocess.run(cmd)
    assert (result.returncode == expected if expected is not None else result.returncode != 0), (label, result.returncode)
    return json.loads((R / f'{label}.result.json').read_text(encoding='utf-8-sig')) if operation != 'Check' else None

def probe(version, slot, label, value=None):
    value = value if value is not None else (4 if version == '58' else 1)
    execute(version, slot, 'Run', label)
    text = (R / f'{label}-probe.json').read_text(encoding='utf-8-sig')
    if version == '58':
        assert f'version={value} target=Game/Development content=package-content-v{value}' in text, text
        assert '/Archive/Windows/' in text.replace('\\', '/'), text
        return text
    data = json.loads(text)
    assert data['version'] == value and data['target'] == 'Game/Development' and data['content'] == f'package-content-v{value}', data
    for field in ('directory', 'runtime', 'process'):
        assert '/Archive/Windows/' in data[field].replace('\\', '/'), data
    return data

def prepare():
    R.mkdir(exist_ok=False)
    for name in ('plan.md', 'validation.md', 'pr.md'):
        shutil.copy2(ROOT / name, R / ('before-' + name))
    for name, revision in (('base', BASE), ('pre-amend', PRE), ('candidate', HEAD)):
        subprocess.run(['git', '-C', str(REPO), 'archive', '--format=zip', f'--output={R / (name + ".zip")}', revision], check=True)
    for label, args in [('range-diff', ['range-diff', f'{BASE}..{PRE}', f'{BASE}..{HEAD}']), ('final', ['diff', BASE, HEAD]), ('amend', ['diff', PRE, HEAD])]:
        (R / f'{label}.diff').write_bytes(subprocess.check_output(['git', '-C', str(REPO), *args]))
    runner = (TOOLS / 'InvokeMultiVersion.ps1').read_text(encoding='utf-8-sig')
    runner = runner.replace("[ValidateSet('56','57')]", "[ValidateSet('56','57','58')]")
    runner = runner.replace("'Boundary')", "'Boundary','Identity')")
    runner = runner.replace('multi-version-20261025', 'amend-drop-resolver-20261025')
    runner = runner.replace('$name="SharpTestProject${Version}_$Slot"', '$name=if($Version -eq \'58\'){if($Slot -eq \'1\'){\'SharpTestProject\'}else{\'SharpTestProject1\'}}else{"SharpTestProject${Version}_$Slot"}')
    runner = runner.replace('$saved="$project\\Saved\\CSharpPackageMulti-20261025"', '$saved=if($Version -eq \'58\'){"$project\\Saved\\UnrealSharpAB\\20261025-csharp-package"}else{"$project\\Saved\\CSharpPackageMulti-20261025"}')
    runner = runner.replace("'List' {$arguments=", "'Identity' {$arguments=@(\"-ScriptsForProject=$uproject\",\"-ScriptDir=$project\\Tools\\PackagingValidation\",'PackagePluginIdentity',\"-Project=$uproject\",\"-Report=$reports\\$Label-identity.json\")}\n            'List' {$arguments=")
    (TOOLS / 'InvokeAmendPackaging.ps1').write_text(runner, encoding='utf-8-sig')
    environment = {}
    for version in ('56', '57', '58'):
        engine = Path(f'C:/Program Files/Epic Games/UE_5.{version[1]}')
        descriptors = list((engine / 'Engine/Plugins').rglob('UnrealSharp.uplugin'))
        environment[version] = {'engine': str(engine), 'build': json.loads((engine / 'Engine/Build/Build.version').read_text()), 'installed': (engine / 'Engine/Build/InstalledBuild.txt').exists(), 'engine_plugins': [str(p) for p in descriptors]}
        assert not descriptors, (version, descriptors)
        for slot in ('1', '2'):
            p = project(version, slot)
            assert p.resolve() == p, p
            environment[version][slot] = {f: (p / f).read_text(encoding='utf-8-sig') for f in ('AutoBuild.bat', 'AutoBuildGame.bat', 'project_env.config.txt', p.name + '.uproject')}
    save('environment.json', environment)
    save('identity.json', {'repo': str(REPO), 'base': BASE, 'pre_amend': PRE, 'candidate': HEAD, 'backup': 'backup/csharp-package-before-drop-resolver-20261025'})
    print('Prepared evidence and runner', HEAD, flush=True)

def deploy():
    sources = {}
    for key in ('base', 'pre-amend', 'candidate'):
        with zipfile.ZipFile(R / f'{key}.zip') as z:
            sources[key] = {i.filename: z.read(i) for i in z.infolist() if not i.is_dir()}
    known = set().union(*(set(d) for d in sources.values()))
    results = {}
    for version in ('56', '57', '58'):
        for slot in ('1', '2'):
            execute(version, slot, 'Check', 'unused')
            p = project(version, slot)
            plugin = p / 'Plugins/UnrealSharp'
            if slot == '2':
                published = p / 'Intermediate/UnrealSharp/Publish' / p.name / 'Win64/Development/x64/Binaries/Managed/net10.0'
                save(f'{version}-pre-amend-published.json', manifest(published))
                for dll in published.glob('*.dll'):
                    if dll.name.startswith('UnrealSharp') or dll.name.startswith('PackageStageProbe') or 'RuntimeGlue' in dll.name:
                        target = R / 'pre-amend-assemblies' / version / dll.name
                        target.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copy2(dll, target)
            assert plugin.resolve() == plugin
            assert not any(x.is_symlink() or (hasattr(x, 'is_junction') and x.is_junction()) for x in plugin.rglob('*'))
            selected = 'base' if slot == '1' else 'candidate'
            expected = sources[selected]
            changes = []
            for name in sorted(known):
                path = plugin / name
                if name not in expected:
                    if path.exists():
                        assert path.resolve().is_relative_to(plugin.resolve())
                        path.unlink()
                        changes.append({'path': name, 'action': 'remove obsolete tracked source'})
                    continue
                raw = expected[name]
                actual = path.read_bytes() if path.exists() else None
                if actual is not None and actual.replace(b'\r\n', b'\n') == raw.replace(b'\r\n', b'\n'): continue
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
                changes.append({'path': name, 'action': 'replace from full fork snapshot'})
            records = {}
            for name, raw in expected.items():
                actual = (plugin / name).read_bytes()
                assert actual.replace(b'\r\n', b'\n') == raw.replace(b'\r\n', b'\n'), (p, name)
                records[name] = hashlib.sha256(actual).hexdigest()
            assert all(not (plugin / name).exists() for name in known - set(expected))
            results[p.name] = {'revision': BASE if slot == '1' else HEAD, 'files': len(records), 'changes': changes, 'source_sha256': records}
            # Capture all real generated text before canonical builds; never touch it.
            save(f'{version}_{slot}-generated-before.json', {scope: manifest(root / 'Intermediate/UnrealSharp/UHT', {'.cs', '.csproj', '.props', '.targets', '.h', '.cpp'}) for scope, root in [('project', p), ('plugin', plugin)]})
            print(p.name, selected, changes, flush=True)
    save('deployment.json', results)

def first(version, slot):
    for operation in ('BuildEditor', 'BuildGame', 'Identity', 'Managed', 'CookStage'):
        execute(version, slot, operation, f'{version}_{slot}-amended-{operation}')
    if slot == '1': execute(version, slot, 'Manual', f'{version}_{slot}-amended-Manual')
    probe(version, slot, f'{version}_{slot}-amended-Run')
    if slot == '2': execute(version, slot, 'Boundary', f'{version}_{slot}-amended-Boundary')
    print('Final functional pass', version, slot, flush=True)

def fixtures():
    identity_source = (project('56', '2') / 'Tools/PackagingValidation/PackagePluginIdentity.cs').read_text()
    identity_source = identity_source.replace('PackagePluginIdentity', 'AmendPluginIdentity')
    for version in ('56', '57', '58'):
        template = (project(version, '2') / 'Tools/PackageStageChecks/PackageStageChecks.Automation.csproj').read_text()
        for slot in ('1', '2'):
            target = project(version, slot) / 'Tools/AmendIdentity'
            target.mkdir(exist_ok=True, parents=True)
            (target / 'AmendIdentity.Automation.csproj').write_text(template, encoding='utf-8')
            (target / 'AmendPluginIdentity.cs').write_text(identity_source, encoding='utf-8')
        check = project(version, '2') / 'Tools/PackageStageChecks/PackageStageChecks.cs'
        text = check.read_text()
        anchor = '        Parameters.DLCFile ='
        if 'plugin-identity-mismatch-rejected' in text: continue
        extra = '''        TargetReceipt Mismatched = ReadReceipt();
        Mismatched.RuntimeDependencies = new RuntimeDependencyList();
        Mismatched.RuntimeDependencies.Add(new RuntimeDependency(new FileReference(Path.Combine(Path.GetDirectoryName(ReceiptPath)!, "OtherPlugin", "UnrealSharp.uplugin"))));
        Reject("plugin-identity-mismatch-rejected", Context(Mismatched), "does not match the native target receipt");
'''
        assert anchor in text
        check.write_text(text.replace(anchor, extra + anchor), encoding='utf-8')
    runner = TOOLS / 'InvokeAmendPackaging.ps1'
    text = runner.read_text(encoding='utf-8-sig').replace('Tools\\PackagingValidation', 'Tools\\AmendIdentity').replace("'PackagePluginIdentity'", "'AmendPluginIdentity'")
    runner.write_text(text, encoding='utf-8-sig')

def premeasure():
    p = project('56', '2')
    records = {}
    with zipfile.ZipFile(R / 'pre-amend.zip') as z:
        for info in z.infolist():
            if info.is_dir(): continue
            actual = (p / 'Plugins/UnrealSharp' / info.filename).read_bytes()
            assert actual.replace(b'\r\n', b'\n') == z.read(info).replace(b'\r\n', b'\n'), info.filename
            records[info.filename] = hashlib.sha256(actual).hexdigest()
    save('pre-amend-source.json', {'revision': PRE, 'files': records, 'initial_test_prefix': '56_2-final-', 'note': 'These initial records execute pre-amend source, before deployment. Final rewritten tests use amended prefix.'})
    samples = []
    for i in range(4):
        label = f'56_2-pre-amend-perf-{i}'
        result = execute('56', '2', 'Stage', label + '-Stage')
        observed = probe('56', '2', label + '-Run')
        samples.append({'revision': PRE, 'round': i, 'warmup': i == 0, 'seconds': result['seconds'], 'probe': observed})
        save('pre-amend-performance.json', samples)
    save('pre-amend-published.json', manifest(p / 'Intermediate/UnrealSharp/Publish' / p.name / 'Win64/Development/x64/Binaries/Managed/net10.0'))

def campaign():
    for version in ('56', '57', '58'):
        for slot in ('1', '2'):
            first(version, slot)

def saved_root(p, version):
    return p / ('Saved/UnrealSharpAB/20261025-csharp-package' if version == '58' else 'Saved/CSharpPackageMulti-20261025')

def regression(version):
    p = project(version, '2')
    src = p / 'Script/PackageStageProbe/Probe.cs'
    content = src.with_name('package-stage.txt')
    original, original_content = src.read_bytes(), content.read_bytes()
    initial = 4 if version == '58' else 1
    value = initial + 1
    archive = saved_root(p, version) / 'Archive/Windows' / p.name / 'Binaries/Managed/net10.0'
    roots = {k: p / v for k, v in {'native': 'Binaries/Win64', 'editor_managed': 'Binaries/Managed', 'plugin_native': 'Plugins/UnrealSharp/Binaries/Win64', 'plugin_managed': 'Plugins/UnrealSharp/Binaries/Managed'}.items()}
    before = {k: manifest(v) for k, v in roots.items()}
    prefix = version + '_2-regression-'
    try:
        execute(version, '2', 'Check', 'unused')
        changed = original.replace(b'version=4', b'version=5') if version == '58' else original.replace(b'const int version = 1;', b'const int version = 2;')
        assert changed != original
        src.write_bytes(changed)
        content.write_text(f'package-content-v{value}\n', encoding='utf-8')
        execute(version, '2', 'Stage', prefix + 'Incremental')
        observed = probe(version, '2', prefix + 'Incremental-Run', value)
        archived = manifest(archive)
        src.write_bytes(original + b'\nTHIS_IS_AN_INTENTIONAL_COMPILATION_ERROR\n')
        execute(version, '2', 'Stage', prefix + 'Failure', expected=None)
        assert manifest(archive) == archived
        src.write_bytes(original); content.write_bytes(original_content)
        execute(version, '2', 'SkipStage', prefix + 'SkipStage')
        assert manifest(archive) == archived
        skipped = probe(version, '2', prefix + 'SkipStage-Run', value)
        execute(version, '2', 'Stage', prefix + 'Recovery')
        recovered = probe(version, '2', prefix + 'Recovery-Run', initial)
        after = {k: manifest(v) for k, v in roots.items()}
        assert before == after, 'Staging altered native or Editor managed output'
        save(f'{version}-regressions.json', {'incremental': observed, 'failure_preserved_archive': True, 'skipstage': skipped, 'recovery': recovered, 'before': before, 'after': after})
    finally:
        src.write_bytes(original); content.write_bytes(original_content)

def measure(version):
    samples = []
    for i, order in ((0, ('1', '2')), (1, ('1', '2')), (2, ('2', '1')), (3, ('1', '2'))):
        for slot in order:
            prefix = f'{version}_{slot}-perf-{i}'
            stage = execute(version, slot, 'Stage', prefix + '-Stage')
            manual = execute(version, slot, 'Manual', prefix + '-Manual') if slot == '1' else None
            observed = probe(version, slot, prefix + '-Run')
            samples.append({'version': version, 'slot': slot, 'round': i, 'warmup': i == 0, 'stage_seconds': stage['seconds'], 'manual_seconds': manual['seconds'] if manual else 0, 'seconds': stage['seconds'] + (manual['seconds'] if manual else 0), 'probe': observed})
            save(f'{version}-performance.json', samples)

if __name__ == '__main__':
    action = sys.argv[1]
    if action == 'prepare': prepare()
    elif action == 'deploy': deploy()
    elif action == 'first': first(*sys.argv[2:4])
    elif action == 'fixtures': fixtures()
    elif action == 'premeasure': premeasure()
    elif action == 'campaign': campaign()
    elif action == 'regression': regression(sys.argv[2])
    elif action == 'measure': measure(sys.argv[2])
    else: raise ValueError(action)
