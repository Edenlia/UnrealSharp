param(
    [ValidateSet('A','B')][string]$Slot,
    [ValidateSet('BuildEditor','BuildGame','CookStage','Stage','Manual','Run','Managed','SkipStage','Initialize')][string]$Operation,
    [string]$Label,
    [switch]$TimestampOutput
)
$ErrorActionPreference = 'Stop'
$project = if ($Slot -eq 'A') { 'C:\Users\edenlia\Workspace\UEProjects\SharpTestProject' } else { 'C:\Users\edenlia\Workspace\UEProjects\SharpTestProject1' }
$name = Split-Path -Leaf $project
$reports = 'C:\Users\edenlia\Workspace\CodeProjects\UnrealSharp-reports\csharp-package'
$envValues = @{}
Get-Content -LiteralPath "$project\project_env.config.txt" | ForEach-Object { $parts = $_.Split('=',2); $envValues[$parts[0]] = $parts[1] }
$engine = $envValues['UE5_ROOT']
if ($engine -ne 'C:\Program Files\Epic Games\UE_5.8' -or $envValues['PROJECT_DIR'] -ne $project -or $envValues['PROJECT_NAME'] -ne $name) { throw 'AutoBuild environment changed' }
$processes = Get-CimInstance Win32_Process
$busy = $processes | Where-Object {
    ($_.Name -match '^(UnrealEditor.*|UnrealBuildTool|AutomationTool|SharpTestProject.*)\.exe$' -or
     ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'UnrealBuildTool|AutomationTool')) -and
    ($_.ExecutablePath -like "$engine\*" -or $_.CommandLine -match 'UE_5\.8|UEProjects\\SharpTestProject')
}
if ($busy) { throw "Build/editor/game process present: $($busy.ProcessId)" }
if ($Operation -notin @('BuildEditor','BuildGame')) {
    # UAT's .NET HTTP client must connect directly to the local IPv6 Zen endpoint.
    $env:NO_PROXY = "$env:NO_PROXY,localhost,127.0.0.1,::1,[::1]"
}
$output = Join-Path $project 'Saved\UnrealSharpAB\20261025-csharp-package'
$archive = Join-Path $output 'Archive'
$stage = Join-Path $output 'Stage'
$uproject = Join-Path $project "$name.uproject"
$arguments = @()
switch ($Operation) {
    'BuildEditor' { $executable = "$project\AutoBuild.bat" }
    'BuildGame' { $executable = "$project\AutoBuildGame.bat" }
    'Initialize' {
        $executable = "$engine\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
        $arguments = @($uproject,'-run=pythonscript','-script=C:\Users\edenlia\Workspace\UEProjects\SharpTestProject\Tools\InitializePackageStage.py','-nullrhi','-unattended','-nosound','-stdout','-FullStdOutLogOutput',"-abslog=$reports\$Label-runtime.log")
    }
    'Managed' {
        $executable = "$engine\Engine\Build\BatchFiles\RunUAT.bat"
        $arguments = @("-ScriptsForProject=$uproject",'BuildUserSolution',"-Project=$uproject","-OutputPath=$project\Binaries\Managed\net10.0",'-TargetConfiguration=Development')
    }
    'Manual' {
        $executable = "$engine\Engine\Build\BatchFiles\RunUAT.bat"
        $arguments = @("-ScriptsForProject=$uproject",'PackageProject',"-Project=$uproject","-ArchiveDirectory=$archive\Windows\$name",'-UETargetType=Game','-UEBuildConfig=Development','-TargetPlatform=Win64','-TargetArchitecture=x64')
    }
    'Run' {
        $executable = "$archive\Windows\$name\Binaries\Win64\$name.exe"
        $arguments = @('/Engine/Maps/Entry','-nullrhi','-unattended','-nosound','-ExecCmds=Quit',"-abslog=$reports\$Label-runtime.log",'-stdout','-FullStdOutLogOutput')
    }
    default {
        $executable = "$engine\Engine\Build\BatchFiles\RunUAT.bat"
        $arguments = @("-ScriptsForProject=$uproject",'BuildCookRun',"-project=$uproject","-target=$name",'-platform=Win64','-clientconfig=Development','-skipbuild','-nocompileeditor','-stage','-pak','-archive',"-stagingdirectory=$stage","-archivedirectory=$archive",'-unattended','-utf8output')
        if ($Operation -eq 'CookStage') { $arguments += @('-cook','-map=/Engine/Maps/Entry') }
        else { $arguments += '-skipcook' }
        if ($Operation -eq 'SkipStage') { $arguments += @('-skipstage','-skippak') }
    }
}
if ([string]::IsNullOrEmpty($Label)) { $Label = "$Slot-$Operation" }

$metadata = [ordered]@{ slot=$Slot;operation=$Operation;executable=$executable;arguments=$arguments;working_directory=$project;started_utc=[DateTime]::UtcNow.ToString('o');loopback_proxy_bypass=($Operation -notin @('BuildEditor','BuildGame')) }
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$reports\$Label.command.json" -Encoding utf8
$timer = [Diagnostics.Stopwatch]::StartNew()
Push-Location -LiteralPath $project
try {
    if ($Operation -eq 'Run') {
        $env:PACKAGE_STAGE_PROBE_RESULT = "$reports\$Label-probe.txt"
        $quoted = $arguments | ForEach-Object { '"' + $_ + '"' }
        $process = Start-Process -FilePath $executable -ArgumentList $quoted -WorkingDirectory $project -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput "$reports\$Label.log" -RedirectStandardError "$reports\$Label-stderr.log"
        $result = $process.ExitCode
    } elseif ($TimestampOutput) {
        & $executable @arguments 2>&1 | ForEach-Object { '{0:o} {1}' -f [DateTime]::UtcNow, $_ } | Out-File -LiteralPath "$reports\$Label.log" -Encoding utf8
        $result = $LASTEXITCODE
    } else { & $executable @arguments *> "$reports\$Label.log"; $result = $LASTEXITCODE }
}
finally { Pop-Location }
$metadata['exit_code'] = $result
$metadata['seconds'] = $timer.Elapsed.TotalSeconds
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$reports\$Label.result.json" -Encoding utf8
if ($Operation -in @('CookStage','Stage') -and $result -eq 0) {
    $uatLogs = Join-Path $env:APPDATA 'Unreal Engine\AutomationTool\Logs\C+Program+Files+Epic+Games+UE_5.8'
    foreach ($file in @('FinalCopyWin64_NonUFSFiles.txt','FinalCopyWin64_UFSFiles.txt','FinalCopyWin64_NonUFSFilesDebug.txt')) {
        if (Test-Path -LiteralPath "$uatLogs\$file") { Copy-Item -LiteralPath "$uatLogs\$file" -Destination "$reports\$Label-$file" }
    }
}
Get-Content -LiteralPath "$reports\$Label.log" -Tail 18
exit $result
