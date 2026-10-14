"""Run with UnrealEditor-Cmd <project> -run=pythonscript -script=<this file>."""
import json
from pathlib import Path

import unreal

manager = unreal.find_object(None, "/Engine/Transient.CSManager")
assert manager is not None, "UnrealSharp manager was not initialized"
required_assemblies = {"UnrealSharp", "UnrealSharp.Editor", "UnrealSharpMergedGlue"}

project = Path(unreal.Paths.project_dir()).resolve()
manifests = [project / "Binaries/Managed/UnrealSharp.Target.json"]
contexts = [json.loads(path.read_text(encoding="utf-8-sig")) for path in manifests]
contexts = [target for target in contexts if target["Type"] == "Editor" and target["Configuration"] == "Development"]
assert len(contexts) == 1, "Smoke test requires one Editor/Development context"
target = contexts[0]
assert target["Version"] == 3 and "Id" not in target, "Target manifests must use the direct Intermediate layout"
assert Path(target["IntermediateDirectory"]).is_dir(), "The generated target Intermediate is missing"
output = project / "Binaries/Managed" / target["Framework"]
for name in ("GlueCode", "UserCode"):
    manifest = output / (name + ".LoadOrder.json")
    assert manifest.is_file(), f"Missing {manifest}"
    required = set(json.loads(manifest.read_text(encoding="utf-8-sig"))["LoadOrder"])
    required_assemblies.update(required)

assembly_class = unreal.load_class(None, "/Script/UnrealSharpCore.CSManagedAssembly")
loaded = {obj.get_name() for obj in unreal.ObjectIterator(assembly_class) if obj.get_outer() == manager}
assert required_assemblies <= loaded, f"Managed assembly objects are missing: {sorted(required_assemblies - loaded)}; found={sorted(loaded)}"

unreal.log(f"SEPARATE_GLUE_SMOKE_OK project={project.name} target={target['Name']} layout=project assemblies={len(required_assemblies)}")
