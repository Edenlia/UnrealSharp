import difflib
import json
import sys
from pathlib import Path

workspace=Path(r'C:\Users\edenlia\Workspace')
reports=workspace/'CodeProjects/UnrealSharp-reports/csharp-package/multi-version-20261025'
for version in sys.argv[1:] or ('56','57'):
    data=[json.loads((reports/f'{version}_{slot}-archive-manifest.json').read_text(encoding='utf-8')) for slot in (1,2)]
    roots=[workspace/'UEProjects'/f'SharpTestProject{version}_{slot}'/'Saved/CSharpPackageMulti-20261025/Archive/Windows'/f'SharpTestProject{version}_{slot}'/'Binaries/Managed/net10.0' for slot in (1,2)]
    changed=[p for p in sorted(set(data[0])&set(data[1])) if data[0][p]!=data[1][p]]
    a_only=sorted(set(data[0])-set(data[1]));b_only=sorted(set(data[1])-set(data[0]))
    diffs=[]
    for name in changed+a_only+b_only:
        if Path(name).suffix not in ('.json','.txt','.props','.cs','.csproj','.installed'): continue
        texts=[(root/name).read_text(encoding='utf-8-sig').splitlines(True) if (root/name).exists() else [] for root in roots]
        diffs.extend(difflib.unified_diff(*texts,fromfile='A/'+name,tofile='B/'+name))
    result={'changed':changed,'A_only':a_only,'B_only':b_only,'identical':len(set(data[0])&set(data[1]))-len(changed)}
    (reports/f'{version}-published-delta.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    (reports/f'{version}-published-text.diff').write_text(''.join(diffs),encoding='utf-8')
    print(version,json.dumps(result),flush=True)
