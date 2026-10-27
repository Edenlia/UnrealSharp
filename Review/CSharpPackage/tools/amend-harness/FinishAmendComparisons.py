import json
import difflib
import shutil
import hashlib
from pathlib import Path
from AmendPackaging import R, project, manifest, save

summary = {}
for version in ('56', '57', '58'):
    pre = json.loads((R / f'{version}-pre-amend-published.json').read_text())
    final = json.loads((R / f'{version}_2-final-managed.json').read_text())['publish']
    differences = [n for n in sorted(set(pre) | set(final)) if pre.get(n) != final.get(n)]
    assert not differences, (version, differences)
    summary[version] = {'final_candidate_matches_pre_amend_files': len(final), 'differences': differences}
    roots = [project(version, s) / 'Script' / (project(version, s).name + '.RuntimeGlue') for s in ('1', '2')]
    data = [manifest(root, {'.cs'}) for root in roots]
    for side, records in zip(('A', 'B'), data): save(f'{version}-runtimeglue-source-{side}.json', records)
    delta = [n for n in sorted(set(data[0]) | set(data[1])) if data[0].get(n) != data[1].get(n)]
    text = []
    for name in delta:
        values = [(root / name).read_text(encoding='utf-8-sig').splitlines(True) if (root / name).exists() else [] for root in roots]
        text.extend(difflib.unified_diff(*values, fromfile='A/' + name, tofile='B/' + name))
    (R / f'{version}-runtimeglue-source.diff').write_text(''.join(text), encoding='utf-8')
    summary[version]['AB_runtimeglue_source_differences'] = delta
save('final-equivalence.json', summary)

transient = project('58', '2') / 'Plugins/UnrealSharp/Managed/UnrealSharp/UnrealSharp.Log/bin/Release/net10.0/UnrealSharp.Log.dll'
expected = json.loads((R / '58_2-publish.json').read_text())['UnrealSharp.Log.dll']['sha256']
assert hashlib.sha256(transient.read_bytes()).hexdigest() == expected
(R / 'transient-assemblies/58').mkdir(parents=True, exist_ok=True)
shutil.copy2(transient, R / 'transient-assemblies/58/UnrealSharp.Log.dll')
print(json.dumps(summary, indent=2), flush=True)
