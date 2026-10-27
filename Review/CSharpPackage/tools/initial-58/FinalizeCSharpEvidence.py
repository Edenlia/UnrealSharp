import difflib
import hashlib
import json
import re
import statistics
from pathlib import Path

workspace = Path(r'C:\Users\edenlia\Workspace')
report = workspace / 'CodeProjects/UnrealSharp-reports/csharp-package'
projects = [workspace / 'UEProjects' / name for name in ('SharpTestProject', 'SharpTestProject1')]
roots = [p / 'Saved/UnrealSharpAB/20261025-csharp-package/Archive/Windows' / p.name / 'Binaries/Managed' for p in projects]
manifests = []
for slot, root in zip('AB', roots):
    data = {p.relative_to(root).as_posix(): {'size': p.stat().st_size, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(root.rglob('*')) if p.is_file()}
    manifests.append(data)
    (report / f'published-{slot}.json').write_text(json.dumps(data, indent=2), encoding='utf-8')
a, b = manifests
common = a.keys() & b.keys()
changed = sorted(k for k in common if a[k] != b[k])
left_only, right_only = sorted(a.keys() - b.keys()), sorted(b.keys() - a.keys())
comparison = {'A_count': len(a), 'B_count': len(b), 'A_bytes': sum(x['size'] for x in a.values()), 'B_bytes': sum(x['size'] for x in b.values()), 'identical': len(common) - len(changed), 'changed': changed, 'A_only': left_only, 'B_only': right_only}
(report / 'published-comparison.json').write_text(json.dumps(comparison, indent=2), encoding='utf-8')
candidate = projects[1]
publish = candidate / 'Intermediate/UnrealSharp/Publish' / candidate.name / 'Win64/Development/x64/Binaries/Managed'
stage = candidate / 'Saved/UnrealSharpAB/20261025-csharp-package/Stage/Windows' / candidate.name / 'Binaries/Managed'
chain = {}
for label, root in [('publish', publish), ('stage', stage), ('archive', roots[1])]:
    chain[label] = {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob('*') if p.is_file()}
assert chain['publish'] == chain['stage'] == chain['archive'] and len(chain['archive']) == 225
(report / 'candidate-copy-chain.json').write_text(json.dumps(chain, indent=2), encoding='utf-8')
diff = []
for name in changed + left_only + right_only:
    if Path(name).suffix not in ('.json', '.txt', '.props', '.csproj'): continue
    texts = [(root / name).read_text(encoding='utf-8-sig').splitlines(True) if (root / name).exists() else [] for root in roots]
    diff.extend(difflib.unified_diff(*texts, fromfile='A/' + name, tofile='B/' + name))
(report / 'published-text-full.diff').write_text(''.join(diff), encoding='utf-8')
runtime_roots = [p / 'Script' / (p.name + '.RuntimeGlue') for p in projects]
runtime_manifests = [{p.name: {'sha256': hashlib.sha256(p.read_bytes()).hexdigest(), 'size': p.stat().st_size} for p in root.iterdir() if p.is_file() and p.suffix in ('.cs', '.csproj')} for root in runtime_roots]
for slot, manifest in zip('AB', runtime_manifests):
    (report / f'runtimeglue-source-{slot}.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
diff = []
for name in sorted(runtime_manifests[0].keys() | runtime_manifests[1].keys()):
    texts = [(root / name).read_text(encoding='utf-8-sig').splitlines(True) if (root / name).exists() else [] for root in runtime_roots]
    diff.extend(difflib.unified_diff(*texts, fromfile='A/' + name, tofile='B/' + name))
(report / 'runtimeglue-source-full.diff').write_text(''.join(diff), encoding='utf-8')

def read_result(label):
    return json.loads((report / f'{label}.result.json').read_text(encoding='utf-8-sig'))

def read_log(label):
    data = (report / f'{label}.log').read_bytes()
    return data.decode('utf-16' if data[:2] in [b'\xff\xfe', b'\xfe\xff'] else 'utf-8-sig')

performance = {}
for slot in 'AB':
    runs = []
    for i in range(1, 4):
        stage_label = f'perf-{slot}-{i}-stage'
        stage = read_result(stage_label)
        assert stage['exit_code'] == 0
        stage_phase = float(re.search(r'Stage command time: ([0-9.]+) s', read_log(stage_label))[1])
        manual = read_result(f'perf-A-{i}-manual')['seconds'] if slot == 'A' else 0
        assert 'version=2 target=Game/Development content=package-content-v2' in (report / f'perf-{slot}-{i}-run-probe.txt').read_text()
        runs.append({'run': i, 'stage_process_s': stage['seconds'], 'stage_phase_s': stage_phase, 'manual_process_s': manual, 'complete_workflow_s': stage['seconds'] + manual})
    values = [x['complete_workflow_s'] for x in runs]
    performance[slot] = {'runs': runs, 'median_s': statistics.median(values), 'min_s': min(values), 'max_s': max(values)}
performance['delta_s'] = performance['B']['median_s'] - performance['A']['median_s']
performance['delta_percent'] = 100 * performance['delta_s'] / performance['A']['median_s']
performance['sample_order'] = ['A1', 'B1', 'B2', 'A2', 'A3', 'B3']
(report / 'performance.json').write_text(json.dumps(performance, indent=2), encoding='utf-8')
print(json.dumps({'artifacts': comparison, 'performance': performance}, indent=2))
