import json
import shutil
import statistics
import subprocess
from pathlib import Path

workspace=Path(r'C:\Users\edenlia\Workspace')
reports=workspace/'CodeProjects/UnrealSharp-reports/csharp-package/multi-version-20261025'
runner=workspace/'UEProjects/SharpTestProject56_1/Tools/InvokeMultiVersion.ps1'
shell=shutil.which('pwsh')
assert shell
samples=[]

def run(version,slot,operation,label):
    args=[shell,'-NoProfile','-File',str(runner),'-Version',version,'-Slot',slot,'-Operation',operation,'-Label',label]
    if version=='56' and slot=='2': args+=['-LegacyEntry']
    subprocess.run(args,check=True)
    return json.loads((reports/f'{label}.result.json').read_text(encoding='utf-8-sig'))

for version in ('56','57'):
    for round_id,order in ((0,('1','2')),(1,('1','2')),(2,('2','1')),(3,('1','2'))):
        for slot in order:
            label=f'{version}_{slot}-perf-{round_id}'
            print(f'Performance workflow {label}',flush=True)
            stage=run(version,slot,'Stage',label+'-stage')
            manual=run(version,slot,'Manual',label+'-manual') if slot=='1' else None
            run(version,slot,'Run',label+'-run')
            probe=json.loads((reports/f'{label}-run-probe.json').read_text(encoding='utf-8-sig'))
            assert probe['version']==1 and probe['target']=='Game/Development' and probe['content']=='package-content-v1',probe
            assert '/Archive/Windows/' in probe['runtime'].replace('\\','/'),probe
            samples.append({'version':version,'slot':slot,'round':round_id,'warmup':round_id==0,'stage_seconds':stage['seconds'],
                'manual_seconds':manual['seconds'] if manual else 0,'total_seconds':stage['seconds']+(manual['seconds'] if manual else 0),
                'probe':probe,'label':label})
            (reports/'performance-raw.json').write_text(json.dumps(samples,indent=2),encoding='utf-8')
summary={}
for version in ('56','57'):
    summary[version]={}
    for slot in ('1','2'):
        values=[s['total_seconds'] for s in samples if s['version']==version and s['slot']==slot and not s['warmup']]
        summary[version][slot]={'n':len(values),'seconds':values,'median':statistics.median(values),'min':min(values),'max':max(values)}
    a,b=[summary[version][slot]['median'] for slot in ('1','2')]
    summary[version]['delta_seconds']=b-a
    summary[version]['delta_percent']=(b-a)/a*100
(reports/'performance-summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2),flush=True)
