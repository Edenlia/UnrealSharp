import json
from pathlib import Path
import time
import unreal

project = Path(unreal.Paths.project_dir())
source = project / 'Script/NativeAotProbe/Probe.cs'
original = source.read_bytes()
changed = original.replace(b'HotReloadValue() => 101', b'HotReloadValue() => 202')
assert changed != original
result_path = project / 'Saved/UnrealSharpAB/20261024-nativeaot/hotreload-result.json'
started = time.monotonic()
stage = 0
next_poll = started + 20
observations = []
handle = None

def finish(passed, reason):
    global handle
    if source.read_bytes() != original:
        source.write_bytes(original)
    result_path.write_text(json.dumps({'passed': passed, 'reason': reason, 'observations': observations}), encoding='utf-8')
    unreal.log('NATIVEAOT_HOTRELOAD ' + ('PASS' if passed else 'FAIL') + ' ' + reason)
    unreal.unregister_slate_post_tick_callback(handle)
    unreal.SystemLibrary.quit_editor()

def tick(_delta):
    global stage, next_poll
    now = time.monotonic()
    if now - started > 180:
        finish(False, 'Timed out waiting for 101 -> 202 -> 101')
        return
    if now < next_poll:
        return
    next_poll = now + 1
    try:
        cls = unreal.load_class(None, '/Script/UnrealSharp.NativeAotProbe_C')
        value = unreal.new_object(cls).call_method('HotReloadValue')
        if stage == 0:
            assert value == 101, value
            observations.append(value)
            source.write_bytes(changed)
            stage = 1
        elif stage == 1 and value == 202:
            observations.append(value)
            source.write_bytes(original)
            stage = 2
        elif stage == 2 and value == 101:
            observations.append(value)
            finish(True, 'Managed method body updated twice; source restored')
    except Exception as exc:
        unreal.log_warning('NATIVEAOT_HOTRELOAD waiting: ' + str(exc))

handle = unreal.register_slate_post_tick_callback(tick)
unreal.log('NATIVEAOT_HOTRELOAD waiting for initial managed solution load')
