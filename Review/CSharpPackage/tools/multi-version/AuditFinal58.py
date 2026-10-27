import hashlib
import json
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

workspace=Path(r'C:\Users\edenlia\Workspace')
root=workspace/'CodeProjects/UnrealSharp-reports/csharp-package'
reports=root/'multi-version-20261025'
project=workspace/'UEProjects/SharpTestProject1'
def manifest(folder,text=False):
    paths=[p for p in sorted(folder.rglob('*')) if p.is_file() and (not text or (p.suffix in ('.cs','.csproj','.props','.json') and not {'bin','obj'}.intersection(p.relative_to(folder).parts)))]
    def record(p):
        with p.open('rb') as f: digest=hashlib.file_digest(f,'sha256').hexdigest()
        return p.relative_to(folder).as_posix(),{'size':p.stat().st_size,'sha256':digest}
    with ThreadPoolExecutor(max_workers=8) as pool:return dict(pool.map(record,paths))
results={}
for target in ('Editor','Game'):
    for scope in ('plugin','project'):
        folder=(project/'Plugins/UnrealSharp' if scope=='plugin' else project)/'Intermediate/UnrealSharp/UHT'/target
        data=manifest(folder,True)
        key=f'{target}-{scope}'
        previous=json.loads((root/f'generated-{key}-B.json').read_text(encoding='utf-8'))
        (reports/f'58-final-generated-{key}.json').write_text(json.dumps(data,indent=2),encoding='utf-8')
        differences=[p for p in sorted(set(data)|set(previous)) if data.get(p)!=previous.get(p)]
        results[key]={'count':len(data),'previous_count':len(previous),'differences':differences}
        assert not differences,key
saved=project/'Saved/UnrealSharpAB/20261025-csharp-package'
folders={'publish':project/'Intermediate/UnrealSharp/Publish/SharpTestProject1/Win64/Development/x64/Binaries/Managed/net10.0',
         'stage':saved/'Stage/Windows/SharpTestProject1/Binaries/Managed/net10.0',
         'archive':saved/'Archive/Windows/SharpTestProject1/Binaries/Managed/net10.0'}
data={name:manifest(folder) for name,folder in folders.items()}
for name,values in data.items():(reports/f'58-final-{name}.json').write_text(json.dumps(values,indent=2),encoding='utf-8')
assert data['publish']==data['stage']==data['archive'] and data['publish']
results['managed']={'count':len(data['publish']),'equal':True,'bytes':sum(p['size'] for p in data['publish'].values())}
(reports/'58-final-audit.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
print(json.dumps(results,indent=2),flush=True)
