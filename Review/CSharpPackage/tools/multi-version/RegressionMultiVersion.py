import json
import hashlib
import shutil
import subprocess
from pathlib import Path

def manifest(root):
    return {p.relative_to(root).as_posix():{'size':p.stat().st_size,'sha256':hashlib.file_digest(p.open('rb'),'sha256').hexdigest()}
            for p in sorted(root.rglob('*')) if p.is_file()} if root.exists() else {}

workspace=Path(r'C:\Users\edenlia\Workspace')
reports=workspace/'CodeProjects/UnrealSharp-reports/csharp-package/multi-version-20261025'
runner=workspace/'UEProjects/SharpTestProject56_1/Tools/InvokeMultiVersion.ps1'
shell=shutil.which('pwsh')
assert shell

def execute(version,slot,operation,label,expected=0):
    args=[shell,'-NoProfile','-File',str(runner),'-Version',version,'-Slot',slot,'-Operation',operation,'-Label',label]
    if version=='56' and slot=='2': args+=['-LegacyEntry']
    result=subprocess.run(args)
    assert (result.returncode==expected if expected is not None else result.returncode!=0),(label,result.returncode)
    return json.loads((reports/f'{label}.result.json').read_text(encoding='utf-8-sig')) if operation!='Check' else None

def read_probe(label,version):
    data=json.loads((reports/f'{label}-probe.json').read_text(encoding='utf-8-sig'))
    assert data['version']==version and data['target']=='Game/Development' and data['content']==f'package-content-v{version}',data
    assert '/Archive/Windows/' in data['runtime'].replace('\\','/') and '/Archive/Windows/' in data['directory'].replace('\\','/'),data
    return data

results={}
for version in ('56','57'):
    project=workspace/'UEProjects'/f'SharpTestProject{version}_2'
    probe=project/'Script/PackageStageProbe/Probe.cs'
    content=probe.with_name('package-stage.txt')
    archive=project/'Saved/CSharpPackageMulti-20261025/Archive/Windows'/project.name/'Binaries/Managed/net10.0'
    original=probe.read_bytes(); original_content=content.read_bytes()
    before={'native':manifest(project/'Binaries/Win64'), 'editor_managed':manifest(project/'Binaries/Managed'), 'plugin_native':manifest(project/'Plugins/UnrealSharp/Binaries/Win64'), 'plugin_managed':manifest(project/'Plugins/UnrealSharp/Binaries/Managed')}
    execute(version,'2','Check','unused')
    try:
        probe.write_bytes(original.replace(b'const int version = 1;',b'const int version = 2;'))
        content.write_text('package-content-v2\n',encoding='utf-8')
        execute(version,'2','Stage',f'{version}_2-incremental-stage')
        execute(version,'2','Run',f'{version}_2-incremental-run')
        incremental=read_probe(f'{version}_2-incremental-run',2)
        old_archive=manifest(archive)
        execute(version,'2','Check','unused')
        probe.write_bytes(original+b'\nTHIS_IS_AN_INTENTIONAL_COMPILATION_ERROR\n')
        execute(version,'2','Stage',f'{version}_2-publish-failure',expected=None)
        assert manifest(archive)==old_archive,'Publish failure changed archived managed files'
        execute(version,'2','Check','unused')
        probe.write_bytes(original);content.write_bytes(original_content)
        execute(version,'2','SkipStage',f'{version}_2-skip-stage')
        assert manifest(archive)==old_archive,'skipstage changed archived managed files'
        execute(version,'2','Run',f'{version}_2-skip-stage-run')
        skipped=read_probe(f'{version}_2-skip-stage-run',2)
        execute(version,'2','Stage',f'{version}_2-recovery-stage')
        execute(version,'2','Run',f'{version}_2-recovery-run')
        recovery=read_probe(f'{version}_2-recovery-run',1)
        after={'native':manifest(project/'Binaries/Win64'),'editor_managed':manifest(project/'Binaries/Managed'),'plugin_native':manifest(project/'Plugins/UnrealSharp/Binaries/Win64'),'plugin_managed':manifest(project/'Plugins/UnrealSharp/Binaries/Managed')}
        assert before==after,'Managed staging modified native/Editor managed outputs'
        results[version]={'incremental':incremental,'failure_preserved_archive':True,'skip_stage':skipped,'recovery':recovery,'native_editor_outputs_unchanged':True,'before':before,'after':after}
        (reports/'regressions.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
    finally:
        probe.write_bytes(original);content.write_bytes(original_content)
print('Both version regression sets passed',flush=True)
