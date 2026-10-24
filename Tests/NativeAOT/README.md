# NativeAOT integration fixture

These files are copied into a disposable UnrealSharp test project; they do not create or build a native Unreal target themselves. Use the target project's existing native build workflow and preserve its engine, target and configuration. The native fixture needs the usual Core/CoreUObject/Engine module dependencies.

1. Publish `Dependency/NativeAotDependency.csproj` with `dotnet publish -c Release -r win-x64 -o <DependencyOutput>`. This small NativeAOT library exports a constant-return function and tests native dependency staging.
2. Copy `Probe.cs`, `Matrix.cs`, `NativeAotProbe.csproj`, `nativeaot-probe.txt`, and the published `NativeAotDependency.dll` into `Script/NativeAotProbe`. The template import assumes that project-relative location and a project-level UnrealSharp plugin.
3. Add `NativeAotRuntimeProbe.cpp` to the existing native game module. Build Editor using its existing entry point, then build the managed user solution through UnrealSharp's official `BuildUserSolution` command.
4. Run `editor_matrix.py` in the Editor Python commandlet. It checks the managed/native workload and creates an empty `/Game/NativeAotValidation/Validation` map through Editor APIs if absent.
5. In a regular Editor session, run `editor_hotreload.py` through `py <script>`. It changes only the fixture method from 101 to 202 and back, checks both reloads, restores exact source bytes, writes a result JSON under Saved and exits that test Editor. Do not invoke it in an unrelated user session. This is C# Hot Reload, not native Live Coding.
6. Cook the validation map with the verified Editor. Build Game using its existing native entry point and stage with native building disabled. Package managed code once as JIT and once with `-NativeAOT`, from the same source snapshot.
7. Run each packaged Game with `/Game/NativeAotValidation/Validation -nullrhi -unattended -nosound -NativeAotProbe -NativeAotMatrix -NativeAotExpected=JIT` or `NativeAOT` as appropriate. Require exit 0 and all result markers; inspect errors independently of the exit code.

The ten matrix groups cover primitive/enum values, strings/names/text, blittable and nonblittable structs, arrays/maps/sets, UObject/weak/soft references, single and multicast delegates, interface dispatch, inheritance and managed GC. The native driver also checks static functions, inherited UFunctions, a native event override, published UTF-8 content, a staged native dependency, native UObject collection and managed disposal.

`-NativeAotExceptionProbe` additionally tests a thrown managed exception and the next successful call. It temporarily disables crash-on-exception only on the in-process settings object and restores the value; the expected exception is logged. Require the expected message and `NATIVEAOT_EXCEPTION returned_control=1`.

`-NativeAotBenchmark` warms up and executes 50,000 scalar calls and 50,000 native-property roundtrips. The log includes timings and exact checksums. Run samples sequentially with identical flags and workload, alternate A/B order, and keep raw samples; one run or a build duration is not a performance result.

Reflection roots and the AOT-only limitations are described in `Docs/NativeAOT.md`. Preserve actual loaded-library evidence, source/generated-file hashes, commands, exit codes and complete logs with the measurements.

`run_packaged.ps1` drives these checks and writes machine-readable results. Use `-InspectModules` to require the expected AOT or hostfxr/coreclr modules; `-Benchmark` also requires the expected iteration count and checksums. `run_loader_failures.ps1` is only for a disposable NativeAOT package: it tests missing-library and missing-export diagnostics, restoring and SHA256-checking the original library in a finally block. Both runners require idle UE build/Editor/Game processes.
