from pathlib import Path

script = Path(__file__).with_name('CampaignCSharpPackage.py').read_text()
exec(script.split("check_probe('B', 'verified-B-run', 1)")[0])
events = json.loads((reports / 'campaign.json').read_text())
run('B', 'Stage', 'restored-B-stage')
check_probe('B', 'restored-B-run', 2)
b_source = projects['B'] / 'Script/PackageStageProbe/Probe.cs'
valid = b_source.read_text()
try:
    b_source.write_text(valid.replace('version=2', 'version=3'), encoding='utf-8')
    (projects['B'] / 'Script/PackageStageProbe/package-stage.txt').write_text('package-content-v3\n', encoding='utf-8')
    run('B', 'SkipStage', 'true-skipstage-B')
    check_probe('B', 'true-skipstage-B-run', 2)
finally:
    b_source.write_text(valid, encoding='utf-8')
    (projects['B'] / 'Script/PackageStageProbe/package-stage.txt').write_text('package-content-v2\n', encoding='utf-8')
counts = {'A': 0, 'B': 0}
for slot in ['A', 'B', 'B', 'A', 'A', 'B']:
    counts[slot] += 1
    label = f'noop-{slot}-{counts[slot]}'
    run(slot, 'Stage', label + '-stage')
    if slot == 'A': run(slot, 'Manual', label + '-manual')
    check_probe(slot, label + '-run', 2)
print('CAMPAIGN PASSED', flush=True)
