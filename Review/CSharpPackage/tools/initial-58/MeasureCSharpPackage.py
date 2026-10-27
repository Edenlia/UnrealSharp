from pathlib import Path
script = Path(__file__).with_name('CampaignCSharpPackage.py').read_text()
exec(script.split("check_probe('B', 'verified-B-run', 1)")[0])
events = json.loads((reports / 'campaign.json').read_text())
for slot in ['A', 'B']:
    run(slot, 'Stage', f'perf-warm-{slot}-stage')
    if slot == 'A': run(slot, 'Manual', 'perf-warm-A-manual')
    check_probe(slot, f'perf-warm-{slot}-run', 2)
counts = {'A': 0, 'B': 0}
for slot in ['A', 'B', 'B', 'A', 'A', 'B']:
    counts[slot] += 1
    label = f'perf-{slot}-{counts[slot]}'
    run(slot, 'Stage', label + '-stage')
    if slot == 'A': run(slot, 'Manual', label + '-manual')
    check_probe(slot, label + '-run', 2)
print('PERFORMANCE CAMPAIGN PASSED', flush=True)
