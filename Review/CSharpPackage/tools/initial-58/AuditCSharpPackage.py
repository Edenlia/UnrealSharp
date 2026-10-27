import difflib
import hashlib
import json
import os
from pathlib import Path

workspace = Path(r'C:\Users\edenlia\Workspace')
report = workspace / 'CodeProjects/UnrealSharp-reports/csharp-package'
projects = [workspace / 'UEProjects' / name for name in ('SharpTestProject', 'SharpTestProject1')]

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def manifest(root, text_only=False):
    if not root.exists():
        return {}
    result = {}
    for directory, folders, files in os.walk(root):
        folders[:] = [name for name in folders if name not in ('bin', 'obj')]
        for name in files:
            path = Path(directory) / name
            if text_only and path.suffix not in ('.cs', '.csproj', '.props', '.json'):
                continue
            result[path.relative_to(root).as_posix()] = {'size': path.stat().st_size, 'sha256': digest(path)}
    print(f'Manifest {root}: {len(result)}', flush=True)
    return dict(sorted(result.items()))

results = {}
diffs = []
for target in ('Editor', 'Game'):
    for scope in ('plugin', 'project'):
        roots = [(project / 'Plugins/UnrealSharp' if scope == 'plugin' else project)
                 / 'Intermediate/UnrealSharp/UHT' / target for project in projects]
        manifests = [manifest(root, True) for root in roots]
        key = f'{target}-{scope}'
        for slot, data in zip(('A', 'B'), manifests):
            (report / f'generated-{key}-{slot}.json').write_text(json.dumps(data, indent=2), encoding='utf-8')
        common = set(manifests[0]) & set(manifests[1])
        changed = sorted(path for path in common if manifests[0][path] != manifests[1][path])
        results[key] = {'A': len(manifests[0]), 'B': len(manifests[1]), 'identical': len(common) - len(changed),
                        'changed': changed, 'A_only': sorted(set(manifests[0]) - set(manifests[1])),
                        'B_only': sorted(set(manifests[1]) - set(manifests[0]))}
        for path in changed + results[key]['A_only'] + results[key]['B_only']:
            texts = [(root / path).read_text(encoding='utf-8-sig').splitlines(True) if (root / path).exists() else [] for root in roots]
            diffs.extend(difflib.unified_diff(*texts, fromfile=f'A/{key}/{path}', tofile=f'B/{key}/{path}'))

(report / 'generated-full.diff').write_text(''.join(diffs), encoding='utf-8')
for slot, project in zip(('A', 'B'), projects):
    archived = project / 'Saved/UnrealSharpAB/20261025-csharp-package/Archive/Windows' / project.name / 'Binaries/Managed'
    data = manifest(archived)
    (report / f'published-{slot}.json').write_text(json.dumps(data, indent=2), encoding='utf-8')
    results[f'published-{slot}'] = {'count': len(data), 'bytes': sum(item['size'] for item in data.values())}

(report / 'artifact-audit.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
print(json.dumps(results, indent=2))
