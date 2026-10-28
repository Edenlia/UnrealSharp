param(
    [Parameter(Mandatory)][ValidateSet('Client','Server')][string]$Kind,
    [Parameter(Mandatory)][string]$ArchiveRoot
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$engine = Split-Path (Split-Path $project -Parent) -Parent
$workspace = Split-Path $engine -Parent
$report = Join-Path $workspace 'CodeProjects/UnrealSharp-reports/csharp-package-client-server-20261028'
$archive = (Resolve-Path -LiteralPath $ArchiveRoot).Path
if (!$archive.StartsWith($project + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive outside this test project' }
$target = if ($Kind -eq 'Client') { 'CSharpPackageClient' } else { 'CSharpPackageDedicated' }
$platform = 'Windows' + $Kind
$managed = Join-Path $archive 'UnrealSharpTestProject/Binaries/Managed/net10.0'
$stage = Join-Path $project "Saved/StagedBuilds/$platform/UnrealSharpTestProject/Binaries/Managed/net10.0"
$publish = Join-Path $project "Intermediate/UnrealSharp/Publish/$target/Win64/Development/x64/Binaries/Managed/net10.0"
$exe = Join-Path $archive "UnrealSharpTestProject/Binaries/Win64/$target.exe"
foreach ($required in @($managed,$stage,$publish,$exe)) { if (!(Test-Path -LiteralPath $required)) { throw "Missing expected output: $required" } }
$manifest = @()
foreach ($file in (Get-ChildItem -LiteralPath $publish -Recurse -File)) {
    $relative = $file.FullName.Substring($publish.Length + 1)
    $hash = (Get-FileHash -LiteralPath $file.FullName).Hash
    foreach ($root in @($stage,$managed)) {
        $copy = Join-Path $root $relative
        if (!(Test-Path -LiteralPath $copy) -or (Get-FileHash -LiteralPath $copy).Hash -ne $hash) { throw "Managed publish/stage/archive mismatch: $relative" }
    }
    $manifest += @{path=$relative;sha256=$hash;bytes=$file.Length}
}
foreach ($root in @($stage,$managed)) { if (@(Get-ChildItem -LiteralPath $root -Recurse -File).Count -ne $manifest.Count) { throw 'Unexpected managed file count' } }
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath "$report/$Kind-managed-manifest.json"
$probePath = Join-Path $report "$Kind-runtime-probe.json"
if (Test-Path -LiteralPath $probePath) { throw 'Probe already exists; preserve the earlier run before repeating' }
$busy = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(UnrealEditor-Cmd|UnrealBuildTool|AutomationTool|dotnet|CSharpPackage.*)\.exe$' -and ($_.ExecutablePath -like "$engine\*" -or $_.CommandLine -like "*$engine*") -and $_.CommandLine -notmatch 'VBCSCompiler|/nodemode:' })
if ($busy.Count) { throw "Build, cook or packaged runtime still active: $($busy.ProcessId)" }
$priorProbe = $env:PACKAGE_STAGE_PROBE_RESULT
$env:PACKAGE_STAGE_PROBE_RESULT = $probePath
try {
    $proc = Start-Process -FilePath $exe -ArgumentList @('-nullrhi','-unattended','-nosound','-ExecCmds=Quit',"`"-abslog=$report/$Kind-Packaged.log`"") -WorkingDirectory $archive -WindowStyle Hidden -PassThru
    @{pid=$proc.Id;exe=$exe;archive=$archive;kind=$Kind;started=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath "$report/$Kind-runtime-process.json"
    $proc.WaitForExit()
    $result = @{exit_code=$proc.ExitCode;exe=$exe;managed_files=$manifest.Count}
    $result | ConvertTo-Json | Set-Content -LiteralPath "$report/$Kind-runtime-result.json"
    if ($proc.ExitCode -ne 0) { throw "Packaged process failed: $($proc.ExitCode)" }
    $probe = Get-Content -LiteralPath $probePath -Raw | ConvertFrom-Json
    if ($probe.version -ne 'client-server-ui-20261028' -or $probe.target -ne "$Kind/Development" -or $probe.content -ne 'client-server-package-content-20261028') { throw 'Unexpected packaged managed probe' }
    foreach ($path in @($probe.directory,$probe.runtime,$probe.process)) {
        if (![IO.Path]::GetFullPath($path).StartsWith($archive + '\',[StringComparison]::OrdinalIgnoreCase)) { throw "Runtime path outside archive: $path" }
    }
    $result | ConvertTo-Json
    $probe | ConvertTo-Json
} finally { $env:PACKAGE_STAGE_PROBE_RESULT = $priorProbe }
