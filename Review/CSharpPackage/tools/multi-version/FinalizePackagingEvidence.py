import hashlib
import json
from pathlib import Path

workspace=Path(r'C:\Users\edenlia\Workspace')
report=workspace/'CodeProjects/UnrealSharp-reports/csharp-package/multi-version-20261025'
rows=[]
for file in sorted(report.glob('*.result.json')):
    data=json.loads(file.read_text(encoding='utf-8-sig'))
    if data['operation'] not in ('Stage','CookStage','SkipStage') or data['exit_code']!=0:continue
    count=file.with_name(file.name.replace('.result.json','.log')).read_text(encoding='utf-8-sig').count('Publishing managed code for staged target')
    expected=1 if data['slot']=='2' and data['operation']!='SkipStage' else 0
    assert count==expected,(file.name,count,expected)
    rows.append({'case':file.name,'publisher_calls':count})
inputs=[]
for file in sorted(report.glob('*-inputs.json')):
    records=json.loads(file.read_text(encoding='utf-8-sig'))
    for record in records:
        path=Path(record['path'])
        actual=hashlib.sha256(path.read_bytes()).hexdigest().upper()
        assert actual==record['sha256'],str(path)
    inputs.append({'input_manifest':file.name,'unchanged_files':len(records)})
copies={}
for version in ('56','57'):
    name=f'SharpTestProject{version}_2'
    project=workspace/'UEProjects'/name
    saved=project/'Saved/CSharpPackageMulti-20261025'
    roots=[project/'Intermediate/UnrealSharp/Publish'/name/'Win64/Development/x64/Binaries/Managed/net10.0',saved/'Stage/Windows'/name/'Binaries/Managed/net10.0',saved/'Archive/Windows'/name/'Binaries/Managed/net10.0']
    data=[]
    for root in roots:
        items={}
        for p in sorted(root.rglob('*')):
            if p.is_file():
                with p.open('rb') as f:items[p.relative_to(root).as_posix()]={'size':p.stat().st_size,'sha256':hashlib.file_digest(f,'sha256').hexdigest()}
        data.append(items)
    assert data[0]==data[1]==data[2] and len(data[0])==220
    copies[version]={'equal':True,'count':len(data[0]),'manifest':data[0]}
result={'publisher_calls':rows,'project_inputs':inputs,'final_copy_chains':copies}
(report/'final-evidence-checks.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print({'stage_cases':len(rows),'input_manifests':inputs,'final_copy_counts':{v:data['count'] for v,data in copies.items()}},flush=True)
