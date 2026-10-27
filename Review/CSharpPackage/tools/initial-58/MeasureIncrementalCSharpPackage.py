from pathlib import Path
script = Path(__file__).with_name('CampaignCSharpPackage.py').read_text()
exec(script.split("check_probe('B', 'verified-B-run', 1)")[0])
events = json.loads((reports / 'campaign.json').read_text())
before = json.loads((reports / 'incremental-hashes.json').read_text())
for p in projects.values():
    source = p / 'Script/PackageStageProbe/Probe.cs'
    source.write_text(source.read_text(encoding='utf-8-sig').replace('version=2', 'version=4'), encoding='utf-8')
    (p / 'Script/PackageStageProbe/package-stage.txt').write_text('package-content-v4\n', encoding='utf-8')
for slot in ['A', 'B']:
    run(slot, 'Stage', f'perf-incremental-{slot}-stage')
    if slot == 'A': run(slot, 'Manual', 'perf-incremental-A-manual')
    check_probe(slot, f'perf-incremental-{slot}-run', 4)
    p = projects[slot]
    assert before[slot]['native'] == digest(p / 'Binaries/Win64' / f'{p.name}.exe')
    assert before[slot]['editor_probe'] == digest(p / 'Binaries/Managed/net10.0/PackageStageProbe.dll')
print('INCREMENTAL CAMPAIGN PASSED', flush=True)
