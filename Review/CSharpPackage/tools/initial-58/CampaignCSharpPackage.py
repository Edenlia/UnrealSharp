import hashlib
import json
import subprocess
from pathlib import Path

workspace = Path(r'C:\Users\edenlia\Workspace')
reports = workspace / 'CodeProjects/UnrealSharp-reports/csharp-package'
projects = {slot: workspace / 'UEProjects' / name for slot, name in [('A', 'SharpTestProject'), ('B', 'SharpTestProject1')]}
runner = projects['A'] / 'Tools/InvokeCSharpPackage.ps1'
events = []

def save():
    (reports / 'campaign.json').write_text(json.dumps(events, indent=2), encoding='utf-8')

def run(slot, operation, label, expected=0):
    result = subprocess.run(['powershell.exe', '-NoProfile', '-File', str(runner), '-Slot', slot, '-Operation', operation, '-Label', label], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    meta = json.loads((reports / f'{label}.result.json').read_text(encoding='utf-8-sig'))
    assert result.returncode == meta['exit_code'], (label, result.returncode, meta)
    assert (meta['exit_code'] == 0) == (expected == 0), (label, meta)
    events.append({'label': label, 'slot': slot, 'operation': operation, 'seconds': meta['seconds'], 'exit_code': meta['exit_code']})
    save()
    print(f"{label}: exit={meta['exit_code']} seconds={meta['seconds']:.2f}", flush=True)
    return meta

def check_probe(slot, label, version):
    run(slot, 'Run', label)
    observation = (reports / f'{label}-probe.txt').read_text()
    assert f'version={version} target=Game/Development content=package-content-v{version}' in observation, observation
    assert '/Archive/Windows/' in observation, observation

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def archive(slot):
    p = projects[slot]
    return p / 'Saved/UnrealSharpAB/20261025-csharp-package/Archive/Windows' / p.name / 'Binaries/Managed/net10.0'

check_probe('B', 'verified-B-run', 1)
control = {slot: {'native': digest(p / 'Binaries/Win64' / f'{p.name}.exe'), 'editor_probe': digest(p / 'Binaries/Managed/net10.0/PackageStageProbe.dll'), 'packaged_probe': digest(archive(slot) / 'PackageStageProbe.dll')} for slot, p in projects.items()}
for slot, p in projects.items():
    source = p / 'Script/PackageStageProbe/Probe.cs'
    source.write_text(source.read_text(encoding='utf-8-sig').replace('version=1', 'version=2'), encoding='utf-8')
    (p / 'Script/PackageStageProbe/package-stage.txt').write_text('package-content-v2\n', encoding='utf-8')
for slot in ['A', 'B']:
    run(slot, 'Stage', f'incremental-{slot}-stage')
    if slot == 'A': run(slot, 'Manual', 'incremental-A-manual')
    check_probe(slot, f'incremental-{slot}-run', 2)
    p = projects[slot]
    after = {'native': digest(p / 'Binaries/Win64' / f'{p.name}.exe'), 'editor_probe': digest(p / 'Binaries/Managed/net10.0/PackageStageProbe.dll'), 'packaged_probe': digest(archive(slot) / 'PackageStageProbe.dll')}
    assert all(control[slot][key] == after[key] for key in ['native', 'editor_probe'])
    assert control[slot]['packaged_probe'] != after['packaged_probe']
    control[slot]['after'] = after
(reports / 'incremental-hashes.json').write_text(json.dumps(control, indent=2), encoding='utf-8')

b_source = projects['B'] / 'Script/PackageStageProbe/Probe.cs'
valid = b_source.read_text()
before_failure = digest(archive('B') / 'PackageStageProbe.dll')
try:
    b_source.write_text('#error PACKAGE_STAGE_EXPECTED_ERROR\n' + valid, encoding='utf-8')
    run('B', 'Stage', 'expected-B-publish-failure', expected=1)
    failure_log = (reports / 'expected-B-publish-failure.log').read_text(encoding='utf-16')
    assert 'PACKAGE_STAGE_EXPECTED_ERROR' in failure_log
    assert 'ARCHIVE COMMAND STARTED' not in failure_log
    assert digest(archive('B') / 'PackageStageProbe.dll') == before_failure
finally:
    b_source.write_text(valid, encoding='utf-8')
run('B', 'Stage', 'recovered-B-stage')
check_probe('B', 'recovered-B-run', 2)

try:
    b_source.write_text(valid.replace('version=2', 'version=3'), encoding='utf-8')
    (projects['B'] / 'Script/PackageStageProbe/package-stage.txt').write_text('package-content-v3\n', encoding='utf-8')
    run('B', 'SkipStage', 'skipstage-B')
    check_probe('B', 'skipstage-B-run', 2)
finally:
    b_source.write_text(valid, encoding='utf-8')
    (projects['B'] / 'Script/PackageStageProbe/package-stage.txt').write_text('package-content-v2\n', encoding='utf-8')

counts = {'A': 0, 'B': 0}
for slot in ['A', 'B', 'B', 'A', 'A', 'B']:
    counts[slot] += 1
    label = f'noop-{slot}-{counts[slot]}'
    run(slot, 'Stage', label + '-stage')
    if slot == 'A': run(slot, 'Manual', label + '-manual')
    check_probe(slot, label + '-run', 2)
print('CAMPAIGN PASSED', flush=True)
