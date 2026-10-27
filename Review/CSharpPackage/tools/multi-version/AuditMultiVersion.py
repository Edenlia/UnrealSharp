import difflib
import hashlib
import json
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

workspace = Path(r'C:\Users\edenlia\Workspace')
report = workspace / 'CodeProjects/UnrealSharp-reports/csharp-package/multi-version-20261025'

def digest(path):
    return hashlib.file_digest(path.open('rb'), 'sha256').hexdigest()

def manifest(root, suffixes=None):
    if not root.exists(): return {}
    paths=[p for p in sorted(root.rglob('*')) if p.is_file()
           and (suffixes is None or not {'bin','obj'}.intersection(p.relative_to(root).parts))
           and (suffixes is None or p.suffix in suffixes)]
    def record(p): return p.relative_to(root).as_posix(), {'size':p.stat().st_size,'sha256':digest(p)}
    with ThreadPoolExecutor(max_workers=8) as pool: return dict(pool.map(record,paths))

def save(name, value):
    (report/name).write_text(json.dumps(value, indent=2), encoding='utf-8')

results = {}
versions=sys.argv[1:] or ['56','57']
for version in versions:
    projects = [workspace / 'UEProjects' / f'SharpTestProject{version}_{slot}' for slot in (1,2)]
    for target in ('Editor','Game'):
        for scope in ('plugin','project'):
            roots = [(p/'Plugins/UnrealSharp' if scope=='plugin' else p)/'Intermediate/UnrealSharp/UHT'/target for p in projects]
            if version=='56' and scope=='plugin':
                roots[0] = projects[0]/'Saved/CSharpPackageMulti-20261025/original-base-plugin/Intermediate/UnrealSharp/UHT'/target
            data = [manifest(p, {'.cs','.csproj','.props','.json'}) for p in roots]
            key = f'{version}-generated-{scope}-{target}'
            for slot, items in zip(('A','B'),data): save(f'{key}-{slot}.json',items)
            changed=[p for p in sorted(set(data[0])|set(data[1])) if data[0].get(p)!=data[1].get(p)]
            diff=[]
            for p in changed:
                texts=[(root/p).read_text(encoding='utf-8-sig').splitlines(True) if (root/p).exists() else [] for root in roots]
                diff.extend(difflib.unified_diff(*texts, fromfile=f'A/{p}',tofile=f'B/{p}'))
            (report/f'{key}.diff').write_text(''.join(diff),encoding='utf-8')
            results[key]={'A':len(data[0]),'B':len(data[1]),'changed':changed,'roots':[str(p) for p in roots]}
    for slot, project in zip(('1','2'),projects):
        saved=project/'Saved/CSharpPackageMulti-20261025'
        roots={'stage':saved/'Stage/Windows'/project.name/'Binaries/Managed/net10.0',
               'archive':saved/'Archive/Windows'/project.name/'Binaries/Managed/net10.0'}
        if slot=='2': roots['publish']=project/'Intermediate/UnrealSharp/Publish'/project.name/'Win64/Development/x64/Binaries/Managed/net10.0'
        data={key:manifest(root) for key,root in roots.items()}
        for key,items in data.items(): save(f'{version}_{slot}-{key}-manifest.json',items)
        results[f'{version}_{slot}-managed']={key:{'count':len(items),'bytes':sum(i['size'] for i in items.values())} for key,items in data.items()}
        if slot=='2':
            equal=data['stage']==data['archive']==data['publish'] and bool(data['publish'])
            results[f'{version}_{slot}-managed']['equal']=equal
            assert equal, (version,slot,'published stage/archive mismatch')
        else: assert not data['stage'] and data['archive'], (version,'manual control unexpectedly staged managed code')
save('artifact-audit'+('' if len(versions)==2 else '-'+versions[0])+'.json',results)
print(json.dumps(results,indent=2))
