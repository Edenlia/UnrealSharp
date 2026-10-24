"""Run reflected managed tests in a live Editor while its normal C# watcher reloads changes."""
import json
from pathlib import Path
import time
import traceback
import unreal

root = Path(__file__).resolve().parent
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
last_request = None
started = time.monotonic()


def run_checks(request_id):
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    first = actors.spawn_actor_from_class(unreal.Actor, unreal.Vector(0, 0, 0))
    second = actors.spawn_actor_from_class(unreal.Actor, unreal.Vector(10, 0, 0))
    try:
        cls = unreal.load_class(None, "/Script/UnrealSharp.ParameterBufferManagedTests_C")
        instance = unreal.new_object(cls)
        passed = instance.call_method("RunTests", (first, second))
        report = instance.call_method("ReadReport")
        (root / (request_id + "-checks.txt")).write_text(report, encoding="utf-8")
        if not passed or "TOTAL " not in report or "FAIL " in report or "ERROR " in report:
            raise RuntimeError(report)
        return {"passed": True, "checks": len([line for line in report.splitlines() if line.startswith("PASS ")])}
    finally:
        actors.destroy_actor(first)
        actors.destroy_actor(second)


def tick(delta):
    global last_request
    try:
        if time.monotonic() - started > 900:
            raise TimeoutError("Live Editor probe timed out")
        request_path = root / "request.json"
        if not request_path.exists():
            return
        request = json.loads(request_path.read_text(encoding="utf-8"))
        request_id = request["id"]
        if request_id == last_request:
            return
        if not request_id.replace("-", "").isalnum():
            raise ValueError("Invalid request id")
        last_request = request_id
        if request["action"] == "quit":
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.EditorPythonScripting.set_keep_python_script_alive(False)
            return
        if request["action"] != "run":
            raise ValueError("Unsupported action")
        result = run_checks(request_id)
        (root / (request_id + ".json")).write_text(json.dumps(result), encoding="utf-8")
        unreal.log("NATIVEAOT LIVE " + request_id + " " + json.dumps(result))
    except Exception:
        result = {"passed": False, "error": traceback.format_exc()}
        (root / ((last_request or "startup") + ".json")).write_text(json.dumps(result), encoding="utf-8")
        unreal.log_error(result["error"])
        if time.monotonic() - started > 900:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.EditorPythonScripting.set_keep_python_script_alive(False)


handle = unreal.register_slate_post_tick_callback(tick)
(root / "python-ready.json").write_text(json.dumps({"ready": True}), encoding="utf-8")
