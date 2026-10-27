import difflib
import json
import statistics
import zipfile
from pathlib import Path
from AmendPackaging import BASE, HEAD, PRE, R, ROOT, project, saved_root, manifest, save

def changed(a, b):
    return [name for name in sorted(set(a) | set(b)) if a.get(name) != b.get(name)]

def generated(root):
    all_files = manifest(root, {'.cs', '.csproj', '.props', '.json', '.targets', '.h', '.cpp'})
    return {k: v for k, v in all_files.items() if not {'bin', 'obj'}.intersection(Path(k).parts)}

results = {}
for version in ('56', '57', '58'):
    pair = [project(version, slot) for slot in ('1', '2')]
    generated_cache = {}
    for scope in ('plugin', 'project'):
        roots = [(p / 'Plugins/UnrealSharp' if scope == 'plugin' else p) / 'Intermediate/UnrealSharp/UHT' for p in pair]
        data = [generated(root) for root in roots]
        for slot, values in zip(('1', '2'), data): generated_cache[(slot, scope)] = values
        key = f'{version}-generated-{scope}'
        for slot, values in zip(('A', 'B'), data): save(f'{key}-{slot}.json', values)
        delta = changed(*data)
        diffs = []
        for name in delta:
            texts = [(root / name).read_text(encoding='utf-8-sig').splitlines(True) if (root / name).exists() else [] for root in roots]
            diffs.extend(difflib.unified_diff(*texts, fromfile='A/' + name, tofile='B/' + name))
        (R / f'{key}.diff').write_text(''.join(diffs), encoding='utf-8')
        results[key] = {'A': len(data[0]), 'B': len(data[1]), 'differences': delta}
    output_data = []
    for slot, p in zip(('1', '2'), pair):
        saved = saved_root(p, version)
        folders = {'stage': saved / 'Stage/Windows' / p.name / 'Binaries/Managed/net10.0', 'archive': saved / 'Archive/Windows' / p.name / 'Binaries/Managed/net10.0'}
        if slot == '2': folders['publish'] = p / 'Intermediate/UnrealSharp/Publish' / p.name / 'Win64/Development/x64/Binaries/Managed/net10.0'
        data = {key: manifest(root) for key, root in folders.items()}
        for key, records in data.items(): save(f'{version}_{slot}-{key}.json', records)
        output_data.append(data['archive'])
        if version in ('56', '57'):
            historical_path = ROOT / 'multi-version-20261025' / f'{version}_{slot}-archive-manifest.json'
            if historical_path.exists():
                historical = json.loads(historical_path.read_text())
                results[f'{version}_{slot}-historical-published'] = {'differences': changed(historical, data['archive']), 'historical_count': len(historical), 'current_count': len(data['archive'])}
        if slot == '2':
            assert data['stage'] == data['archive'] == data['publish'] and data['publish'], (version, 'copy chain')
            pre = json.loads((R / f'{version}-pre-amend-published.json').read_text())
            results[version + '-pre-amend-output'] = {'pre': len(pre), 'after': len(data['publish']), 'differences': changed(pre, data['publish'])}
        else:
            assert not data['stage'] and data['archive'], (version, 'original baseline')
        identity = json.loads((R / f'{version}_{slot}-amended-Identity-identity.json').read_text(encoding='utf-8-sig'))
        assert Path(identity['Plugin']) == p / 'Plugins/UnrealSharp/UnrealSharp.uplugin', identity
        assert Path(identity['Assembly']) == p / 'Plugins/UnrealSharp/Build/Scripts/bin/Development/UnrealSharp.Automation.dll', identity
        results[f'{version}_{slot}-managed'] = {'counts': {k: len(v) for k, v in data.items()}, 'identity': identity, 'total_bytes': sum(v['size'] for v in data['archive'].values())}
        before = json.loads((R / f'{version}_{slot}-generated-before.json').read_text())
        after = {scope: {name: value for name, value in generated_cache[(slot, scope)].items() if Path(name).suffix != '.json'} for scope in ('project', 'plugin')}
        save(f'{version}_{slot}-generated-after.json', after)
        results[f'{version}_{slot}-generation-reuse'] = {scope: {'count': len(after[scope]), 'differences': changed(before[scope], after[scope])} for scope in before}
    results[f'{version}-published-difference'] = {'changed': changed(*output_data), 'identical': len(set(output_data[0]) & set(output_data[1])) - len([n for n in set(output_data[0]) & set(output_data[1]) if output_data[0][n] != output_data[1][n]])}
    cases = json.loads((R / f'{version}_2-amended-Boundary-cases.json').read_text(encoding='utf-8-sig'))
    assert len(cases['cases']) == 18 and all(c['passed'] for c in cases['cases'])
    results[version + '-boundaries'] = len(cases['cases'])
save('artifact-audit.json', results)
print(json.dumps({k: v for k, v in results.items() if not k.endswith('published-difference')}, indent=2), flush=True)

performance = {}
for version in ('56', '57', '58'):
    path = R / f'{version}-performance.json'
    if not path.exists(): continue
    samples = json.loads(path.read_text())
    performance[version] = {}
    for slot in ('1', '2'):
        values = [s['seconds'] for s in samples if s['slot'] == slot and not s['warmup']]
        assert len(values) == 3
        performance[version][slot] = {'values': values, 'median': statistics.median(values), 'min': min(values), 'max': max(values)}
    a, b = [performance[version][s]['median'] for s in ('1', '2')]
    performance[version]['delta_seconds'] = b - a
    performance[version]['delta_percent'] = (b - a) / a * 100
pre_values = [s['seconds'] for s in json.loads((R / 'pre-amend-performance.json').read_text()) if not s['warmup']]
performance['pre-amend-56'] = {'revision': PRE, 'values': pre_values, 'median': statistics.median(pre_values), 'min': min(pre_values), 'max': max(pre_values)}
save('performance-summary.json', performance)
print(json.dumps(performance, indent=2), flush=True)
