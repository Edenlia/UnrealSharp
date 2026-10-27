import hashlib
import json
import os
import shutil
import zipfile
import subprocess
import difflib
from pathlib import Path
from AmendPackaging import BASE, HEAD, PRE, R, TOOLS, project, saved_root, manifest, save

environment = json.loads((R / 'environment.json').read_text())
assert HEAD == json.loads((R / 'identity.json').read_text())['candidate'], 'Contribution changed during validation'
sources = {}
for version in ('56', '57', '58'):
    for slot in ('1', '2'):
        p = project(version, slot)
        plugin = p / 'Plugins/UnrealSharp'
        records = {}
        key = 'base' if slot == '1' else 'candidate'
        with zipfile.ZipFile(R / f'{key}.zip') as z:
            for info in z.infolist():
                if info.is_dir(): continue
                path = plugin / info.filename
                raw, actual = z.read(info), path.read_bytes()
                if actual != raw:
                    assert path.suffix in ('.cs', '.csproj') and actual.replace(b'\r\n', b'\n') == raw.replace(b'\r\n', b'\n'), path
                records[info.filename] = hashlib.sha256(actual).hexdigest()
        extra = []
        for folder, directories, filenames in os.walk(plugin):
            directories[:] = [d for d in directories if d not in ('Binaries', 'Intermediate', 'bin', 'obj', '.vs', '.idea')]
            for name in filenames:
                path = Path(folder) / name
                relative = path.relative_to(plugin).as_posix()
                if path.suffix in ('.cs', '.csproj', '.cpp', '.h', '.props', '.targets', '.uplugin') and relative not in records: extra.append(relative)
        assert not extra, (p, extra)
        for name, original in environment[version][slot].items():
            assert (p / name).read_text(encoding='utf-8-sig') == original, (p, name)
        sources[p.name] = {'revision': BASE if slot == '1' else HEAD, 'file_count': len(records), 'source_hashes': records, 'extra_source': extra, 'build_descriptor_inputs_unchanged': True}
        fixture = R / 'fixtures' / p.name
        fixture.mkdir(parents=True, exist_ok=True)
        for name in ('Probe.cs', 'package-stage.txt', 'PackageStageProbe.csproj'):
            shutil.copy2(p / 'Script/PackageStageProbe' / name, fixture / name)
save('final-source-verification.json', sources)

calls = []
for file in sorted(R.glob('*.result.json')):
    meta = json.loads(file.read_text(encoding='utf-8-sig'))
    if meta['operation'] not in ('Stage', 'CookStage', 'SkipStage'): continue
    log = file.with_name(file.name.replace('.result.json', '.log')).read_text(encoding='utf-8-sig')
    expected = 1 if meta['slot'] == '2' and meta['operation'] != 'SkipStage' else 0
    actual = log.count('[UnrealSharp] Publishing managed code for staged target')
    assert actual == expected, (file.name, expected, actual)
    calls.append({'label': file.stem, 'exit_code': meta['exit_code'], 'expected_publish_calls': expected, 'actual_publish_calls': actual})
save('publisher-call-counts.json', calls)

assemblies = {}
final_outputs = {}
for version in ('56', '57', '58'):
    roots = []
    for slot in ('1', '2'):
        p = project(version, slot)
        saved = saved_root(p, version)
        archive = saved / 'Archive/Windows' / p.name / 'Binaries/Managed/net10.0'
        stage = saved / 'Stage/Windows' / p.name / 'Binaries/Managed/net10.0'
        roots.append(archive)
        data = {'archive': manifest(archive), 'stage': manifest(stage)}
        if slot == '2':
            data['publish'] = manifest(p / 'Intermediate/UnrealSharp/Publish' / p.name / 'Win64/Development/x64/Binaries/Managed/net10.0')
            assert data['publish'] == data['stage'] == data['archive']
        else: assert not data['stage']
        previous = json.loads((R / f'{version}_{slot}-archive.json').read_text())
        differences = [name for name in sorted(set(previous) | set(data['archive'])) if previous.get(name) != data['archive'].get(name)]
        final_outputs[f'{version}_{slot}'] = {'counts': {k: len(v) for k, v in data.items()}, 'changes_since_artifact_audit': differences}
        save(f'{version}_{slot}-final-managed.json', data)
    audit = R / f'{version}-final-api-il'
    audit.mkdir(exist_ok=True)
    for slot, side in (('1', 'A'), ('2', 'B')):
        data = json.loads((R / f'{version}_{slot}-final-managed.json').read_text())
        (audit / f'published-{side}.json').write_text(json.dumps(data['archive'], indent=2))
    subprocess.run(['dotnet', str(TOOLS / 'AssemblyAudit/bin/Release/net10.0/AssemblyAudit.dll'), *map(str, roots), str(audit)], check=True)
    entries = json.loads((audit / 'assembly-api-il.json').read_text())
    entries_summary = []
    for entry in entries:
        equal = entry['A']['api_il_hash'] == entry['B']['api_il_hash']
        output = audit / (entry['file'].replace(' ', '_') + '.diff')
        if equal: output.write_text('')
        else:
            a, b = [(audit / f'{side}-{entry[side]["name"]}.dll.records.txt').read_text().splitlines(True) for side in ('A', 'B')]
            output.write_text(''.join(difflib.unified_diff(a, b, fromfile='A', tofile='B')))
        entries_summary.append({'file': entry['file'], 'equal_api_il': equal, 'diff': str(output)})
    assemblies[version] = entries_summary
save('final-copy-chain.json', final_outputs)
save('final-assembly-summary.json', assemblies)

harness = R / 'harness'
harness.mkdir(exist_ok=True)
for name in ('AmendPackaging.py', 'InvokeAmendPackaging.ps1', 'AuditAmendedPackaging.py', 'FinalizeAmendEvidence.py', 'ExplainAmendSymbols.py'):
    shutil.copy2(TOOLS / name, harness / name)
for version in ('56', '57', '58'):
    p = project(version, '2')
    for group in ('AmendIdentity', 'PackageStageChecks'):
        target = harness / version / group
        target.mkdir(parents=True, exist_ok=True)
        for file in (p / 'Tools' / group).iterdir():
            if file.is_file(): shutil.copy2(file, target / file.name)
print(json.dumps({'sources': {k: v['file_count'] for k, v in sources.items()}, 'publisher_cases': len(calls)}, indent=2), flush=True)
