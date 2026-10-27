import difflib
import hashlib
import json
import shutil
import subprocess
from pathlib import Path
from AmendPackaging import R, ROOT, TOOLS, project, save

pdb_tool = TOOLS / 'PortablePdbAudit/bin/Release/net10.0/PortablePdbAudit.dll'
specs = [('56-baseline-UnrealSharp', project('56', '1'), 'UnrealSharp', ROOT / 'multi-version-20261025/56_1-archive-manifest.json', R / '56_1-archive.json'), ('58-candidate-Log', project('58', '2'), 'UnrealSharp.Log', R / '58-pre-amend-published.json', R / '58_2-publish.json')]
summary = {}
for key, p, module, old_manifest, new_manifest in specs:
    old = json.loads(old_manifest.read_text())
    new = json.loads(new_manifest.read_text())
    root = p / 'Plugins/UnrealSharp/Managed/UnrealSharp' / module / 'bin/Release/net10.0'
    files = [root / 'win-x64' / f'{module}.pdb', root / f'{module}.pdb']
    report = R / 'symbols' / key
    report.mkdir(parents=True, exist_ok=True)
    observations = []
    for side, file, expected in zip(('old', 'new'), files, (old, new)):
        assert hashlib.sha256(file.read_bytes()).hexdigest() == expected[f'{module}.pdb']['sha256'], (key, file)
        shutil.copy2(file, report / (side + '.pdb'))
        output = report / (side + '.json')
        subprocess.run(['dotnet', str(pdb_tool), str(file), str(output)], check=True)
        observations.append(json.loads(output.read_text()))
    differences = {}
    for field in ('docs', 'methods', 'custom', 'scopes', 'imports', 'locals', 'constants'):
        a, b = [x[field] for x in observations]
        changes = [{'index': i, 'old': a[i] if i < len(a) else None, 'new': b[i] if i < len(b) else None} for i in range(max(len(a), len(b))) if (a[i] if i < len(a) else None) != (b[i] if i < len(b) else None)]
        differences[field] = changes
    (report / 'differences.json').write_text(json.dumps(differences, indent=2), encoding='utf-8')
    # differences.json retains every changed record with its complete old/new value.
    # Avoid a quadratic line diff over the 52 MB formatted metadata inventories.
    doc_paths_only = all(c['old']['hash'] == c['new']['hash'] and c['old']['name'].replace('\\win-x64\\', '\\') == c['new']['name'] for c in differences['docs'])
    imports_equal = all(c['old']['decoded'] == c['new']['decoded'] for c in differences['imports'])
    custom = [{'kind': c['old']['kind'], 'old_utf8': bytes.fromhex(c['old']['value']).decode('utf-8', errors='replace'), 'new_utf8': bytes.fromhex(c['new']['value']).decode('utf-8', errors='replace')} for c in differences['custom']]
    (report / 'custom-decoded.json').write_text(json.dumps(custom, indent=2), encoding='utf-8')
    summary[key] = {'old_source': str(files[0]), 'new_source': str(files[1]), 'ids': [x['id'] for x in observations], 'difference_counts': {field: len(values) for field, values in differences.items()}, 'document_changes_only_rid_paths_same_content': doc_paths_only, 'decoded_imports_equal': imports_equal, 'custom_kinds': [c['kind'] for c in custom]}
save('symbol-summary.json', summary)
print(json.dumps(summary, indent=2), flush=True)
