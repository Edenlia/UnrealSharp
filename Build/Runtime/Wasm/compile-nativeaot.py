"""Compile a UAT-selected closed set of managed assemblies into a wasm64 static archive."""
import concurrent.futures
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile


def build(request):
    bin_dir = Path(request["LlvmRoot"])
    clang = bin_dir / "clang++"
    target = "--target=wasm64-unknown-emscripten"
    ir = subprocess.check_output([clang, target, "-S", "-emit-llvm", "-x", "c", "-", "-o", "-"],
                                 input="", text=True)
    layout = re.search(r'^target datalayout = "([^"]+)"', ir, re.MULTILINE).group(1)
    archive = Path(request["Archive"])
    with tempfile.TemporaryDirectory(dir=archive.parent) as tmp:
        work = Path(tmp)
        source = work / "application.o"
        args = [request["Dotnet"], "exec", "--roll-forward", "Major", request["Compiler"], request["Entry"],
                "--nativelib", "--export-unmanaged-entrypoints", "--targetarch", "wasm64", "--targetos", "browser",
                "--wasm-global-base", "1024", "--parallelism", "2", "--codegenopt", "DataLayout=" + layout,
                "--codegenopt", "LlvmExceptionHandlingModel=emulated", "--codegenopt", "JitVerifyLlvmIR=1",
                "--directpinvoke:libSystem.Native", "--directpinvoke:libSystem.Globalization.Native",
                "--generateunmanagedentrypoints:System.Private.CoreLib,HIDDEN", "--scanreflection", "--stacktracedata",
                "-O", "-o", str(source)]
        for assembly in ("System.Private.CoreLib", "System.Private.StackTraceMetadata", "System.Private.TypeLoader",
                         "System.Private.Reflection.Execution"):
            args.append("--initassembly:" + assembly)
        args.extend("-r:" + path for path in request["References"])
        for name in request["Roots"]:
            args.extend(["--root", name])
        subprocess.run(args, cwd=work, check=True)
        parts = [Path(p) for p in source.with_suffix(".results.txt").read_text().splitlines()]
        if source not in parts or any(p.parent != work or p.suffix not in (".bc", ".o") for p in parts):
            raise ValueError("Unexpected NativeAOT compiler outputs")

        def lower(part):
            if part.suffix == ".o":
                return part
            obj = part.with_suffix(".o")
            subprocess.run([clang, target, "-pthread", "-msimd128", "-fexceptions", "-O2", "-c", part, "-o", obj],
                           check=True)
            return obj

        with concurrent.futures.ThreadPoolExecutor(2) as pool:
            objects = list(pool.map(lower, parts))
        result = work / "application.a"
        subprocess.run([bin_dir / "llvm-ar", "rcs", result, *objects], check=True)
        os.replace(result, archive)


if __name__ == "__main__":
    build(json.loads(Path(sys.argv[1]).read_text()))
