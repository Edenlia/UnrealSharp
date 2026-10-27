import hashlib
import json
import zipfile
from pathlib import Path

workspace=Path(r'C:\Users\edenlia\Workspace')
report=workspace/'CodeProjects/UnrealSharp-reports/csharp-package/multi-version-20261025'
mapping={'SharpTestProject56_1':'step2.zip','SharpTestProject56_2':'final-candidate.zip',
         'SharpTestProject57_1':'base.zip','SharpTestProject57_2':'final-candidate.zip','SharpTestProject1':'final-candidate.zip'}
result={}
for project,archive in mapping.items():
    root=workspace/'UEProjects'/project/'Plugins/UnrealSharp'
    records=[]
    with zipfile.ZipFile(report/archive) as source:
        for info in source.infolist():
            if info.is_dir(): continue
            path=root/info.filename
            assert path.is_file(),str(path)
            expected=source.read(info);actual=path.read_bytes()
            eol_only=False
            if expected!=actual:
                assert path.suffix in ('.cs','.csproj'),str(path)
                eol_only=expected.replace(b'\r\n',b'\n')==actual.replace(b'\r\n',b'\n')
                assert eol_only,str(path)
            records.append({'path':info.filename,'sha256':hashlib.sha256(actual).hexdigest(),'git_crlf_only':eol_only})
    result[project]={'snapshot':archive,'files':len(records),'source_records':records}
(report/'final-source-verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print({name:{'snapshot':data['snapshot'],'matched_files':data['files']} for name,data in result.items()},flush=True)
