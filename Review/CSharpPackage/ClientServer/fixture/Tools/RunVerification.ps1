param([Parameter(Mandatory)][ValidateSet('Editor','Client','Server','Tools','Managed','Open')][string]$Kind)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$engine = Split-Path (Split-Path $project -Parent) -Parent
$workspace = Split-Path $engine -Parent
$report = Join-Path $workspace 'CodeProjects/UnrealSharp-reports/csharp-package-client-server-20261028'
$busy = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(UnrealEditor.*|UnrealBuildTool|AutomationTool|dotnet|CSharpPackage.*)\.exe$' -and ($_.ExecutablePath -like "$engine\*" -or $_.CommandLine -like "*$engine*") -and $_.CommandLine -notmatch 'VBCSCompiler|/nodemode:' })
if ($busy.Count) { throw "Engine busy: $($busy.ProcessId)" }
if (!(Test-Path "$report/identity.json")) { throw 'Prepare not complete' }
$env:NO_PROXY = "$env:NO_PROXY,localhost,127.0.0.1,::1,[::1]"
if ($Kind -eq 'Open') {
    foreach ($gate in @('Editor','Client','Server','Tools','Managed')) {
        if ((Get-Content "$report/build-$gate.result.json" | ConvertFrom-Json).exit_code -ne 0) { throw "Build gate failed: $gate" }
    }
    $proc = Start-Process "$engine/Engine/Binaries/Win64/UnrealEditor.exe" -ArgumentList @("`"$project/UnrealSharpTestProject.uproject`"",'/Engine/Maps/Entry','-NoSplash',"`"-abslog=$report/Editor.log`"") -WorkingDirectory $project -PassThru
    @{pid=$proc.Id;project=$project;engine=$engine} | ConvertTo-Json | Set-Content "$report/editor-process.json"
    Write-Output "Editor PID $($proc.Id)"
    exit 0
}
$started = Get-Date
Push-Location $project
try {
    if ($Kind -eq 'Managed') {
        & "$engine/Engine/Build/BatchFiles/RunUAT.bat" BuildUserSolution "-ScriptsForProject=$project/UnrealSharpTestProject.uproject" "-Project=$project/UnrealSharpTestProject.uproject" "-OutputPath=$project/Binaries/Managed/net10.0" -TargetConfiguration=Development *> "$report/build-$Kind.log"
    } else {
        $script = switch ($Kind) { 'Editor' {'AutoBuild.bat'} 'Client' {'AutoBuildClient.bat'} 'Server' {'AutoBuildServer.bat'} 'Tools' {'AutoBuildTools.bat'} }
        & (Join-Path $project $script) *> "$report/build-$Kind.log"
    }
    $result = $LASTEXITCODE
} finally { Pop-Location }
@{exit_code=$result;kind=$Kind;started=$started.ToString('o');seconds=((Get-Date)-$started).TotalSeconds;engine=$engine;project=$project} | ConvertTo-Json | Set-Content "$report/build-$Kind.result.json"
Get-Content "$report/build-$Kind.log" -Tail 20
exit $result
