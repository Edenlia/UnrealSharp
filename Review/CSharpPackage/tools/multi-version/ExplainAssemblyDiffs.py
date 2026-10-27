import difflib
import hashlib
import json
import sys
from pathlib import Path

workspace=Path(r'C:\Users\edenlia\Workspace')
reports=workspace/'CodeProjects/UnrealSharp-reports/csharp-package/multi-version-20261025'
for version in sys.argv[1:] or ('56','57'):
    audit=reports/f'{version}-assembly-audit'
    entries=json.loads((audit/'assembly-api-il.json').read_text(encoding='utf-8'))
    summary=[]
    for entry in entries:
        name=entry['file'].replace(' ','_')
        if entry['A']['api_il_hash']==entry['B']['api_il_hash']:
            (audit/f'{name}.diff').write_text('',encoding='utf-8')
            summary.append({'file':entry['file'],'equal_api_il_records':True,'diff_file':f'{name}.diff'})
            continue
        paths=[audit/f'{side}-{entry[side]["name"]}.dll.records.txt' for side in ('A','B')]
        text=[p.read_text(encoding='utf-8-sig').splitlines(True) for p in paths]
        (audit/f'{name}.diff').write_text(''.join(difflib.unified_diff(*text,fromfile=str(paths[0]),tofile=str(paths[1]))),encoding='utf-8')
        summary.append({'file':entry['file'],'equal_api_il_records':entry['A']['api_il_hash']==entry['B']['api_il_hash'], 'diff_file':f'{name}.diff'})
    roots=[workspace/'UEProjects'/f'SharpTestProject{version}_{slot}'/'Script'/f'SharpTestProject{version}_{slot}.RuntimeGlue' for slot in (1,2)]
    manifests=[]
    for slot,root in zip(('A','B'),roots):
        data={p.relative_to(root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.rglob('*.cs')) if not {'bin','obj'}.intersection(p.relative_to(root).parts)}
        manifests.append(data)
        (audit/f'runtimeglue-source-{slot}.json').write_text(json.dumps(data,indent=2),encoding='utf-8')
    changed=[name for name in sorted(set(manifests[0])|set(manifests[1])) if manifests[0].get(name)!=manifests[1].get(name)]
    diffs=[]
    for name in changed:
        text=[(root/name).read_text(encoding='utf-8-sig').splitlines(True) if (root/name).exists() else [] for root in roots]
        diffs.extend(difflib.unified_diff(*text,fromfile=f'A/{name}',tofile=f'B/{name}'))
    (audit/'runtimeglue-source.diff').write_text(''.join(diffs),encoding='utf-8')
    (audit/'diff-summary.json').write_text(json.dumps({'assemblies':summary,'runtimeglue_changed_source':changed},indent=2),encoding='utf-8')
    print(version,json.dumps(summary), 'RuntimeGlue sources:', changed,flush=True)
