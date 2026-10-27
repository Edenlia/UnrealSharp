param([ValidateSet('56','57')][string]$Version,[ValidateSet('1','2')][string]$Slot,[ValidateSet('Check','BuildEditor','BuildGame','Managed','Bindings','Initialize','List','CookStage','Stage','Manual','Run','SkipStage','Boundary')][string]$Operation,[string]$Label,[switch]$LegacyEntry,[switch]$TimestampOutput)
$ErrorActionPreference='Stop'
$reports='C:\Users\edenlia\Workspace\CodeProjects\UnrealSharp-reports\csharp-package\multi-version-20261025'
$name="SharpTestProject${Version}_$Slot"
$project="C:\Users\edenlia\Workspace\UEProjects\$name"
$settings=@{}
Get-Content -LiteralPath "$project\project_env.config.txt" | ForEach-Object {$pair=$_.Split('=',2);$settings[$pair[0]]=$pair[1]}
$engine=$settings['UE5_ROOT']
if($settings['PROJECT_DIR'] -ne $project -or $settings['PROJECT_NAME'] -ne $name -or $engine -ne "C:\Program Files\Epic Games\UE_5.$($Version.Substring(1))"){throw 'Unexpected AutoBuild binding'}
$busy=Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(UnrealEditor.*|UnrealBuildTool|AutomationTool|dotnet|SharpTestProject.*)\.exe$' -and $_.CommandLine -notmatch '/nodemode:1|VBCSCompiler\.dll' -and ($_.ExecutablePath -like "$engine\*" -or $_.ExecutablePath -like "$project\*" -or $_.CommandLine -like "*$project\*" -or $_.CommandLine -like "*$engine\*") }
if($busy){throw "Engine/project busy: $($busy.ProcessId)"}
if($Operation -eq 'Check'){Write-Output 'Engine/project idle';exit 0}
if(!$Label){$Label="${Version}_${Slot}-$Operation"}
if(Test-Path -LiteralPath "$reports\$Label.result.json"){throw "Evidence already exists: $Label"}
$uproject="$project\$name.uproject"
$saved="$project\Saved\CSharpPackageMulti-20261025"
$archive="$saved\Archive"
$stage="$saved\Stage"
$arguments=@()
switch($Operation) {
    'BuildEditor' {$executable="$project\AutoBuild.bat"}
    'BuildGame' {$executable="$project\AutoBuildGame.bat"}
    'Run' {
        $executable="$archive\Windows\$name\Binaries\Win64\$name.exe"
        $arguments=@('/Engine/Maps/Entry','-nullrhi','-unattended','-nosound','-ExecCmds=Quit','-stdout','-FullStdOutLogOutput',"-abslog=$reports\$Label-runtime.log")
        $env:PACKAGE_STAGE_PROBE_RESULT="$reports\$Label-probe.json"
    }
    default {
        $executable="$engine\Engine\Build\BatchFiles\RunUAT.bat"
        $env:NO_PROXY="$env:NO_PROXY,localhost,127.0.0.1,::1,[::1]"
        switch($Operation) {
            'List' {$arguments=@("-ScriptsForProject=$uproject",'-List')}
            'Initialize' {$arguments=@("-ScriptsForProject=$uproject",'GenerateProject',"-Project=$uproject","-ProjectName=$name.RuntimeGlue","-ProjectFolder=$project\Script\$name.RuntimeGlue",'-GenerateSolution','-RunUSharpProjectSetup')}
            'Boundary' {$arguments=@("-ScriptsForProject=$uproject","-ScriptDir=$project\Tools\PackageStageChecks",'PackageStageChecks',"-Project=$uproject","-Receipt=$project\Binaries\Win64\$name.target","-Results=$reports\$Label-cases.json")}
            'Managed' {$arguments=@("-ScriptsForProject=$uproject",'BuildUserSolution',"-Project=$uproject","-OutputPath=$project\Binaries\Managed\net10.0",'-TargetConfiguration=Development')}
            'Bindings' {$arguments=@("-ScriptsForProject=$uproject",'BuildSolution',"-Project=$uproject","-Folders=$project\Plugins\UnrealSharp\Managed\UnrealSharp",'-TargetConfiguration=Development')}
            'Manual' {$arguments=@("-ScriptsForProject=$uproject",'PackageProject',"-Project=$uproject","-ArchiveDirectory=$archive\Windows\$name",'-UETargetType=Game','-UEBuildConfig=Development','-TargetPlatform=Win64','-TargetArchitecture=x64')}
            default {
                foreach($required in @('BuildEditor','BuildGame')) {
                    $lastBuild=Get-ChildItem -LiteralPath $reports -Filter "${Version}_${Slot}*.result.json" | ForEach-Object {Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json} | Where-Object {$_.operation -eq $required} | Sort-Object started_utc -Descending | Select-Object -First 1
                    if(!$lastBuild -or $lastBuild.exit_code -ne 0){throw "$required did not succeed"}
                }
                $command=if($LegacyEntry){'BuildCookRunUnrealSharp'}else{'BuildCookRun'}
                $arguments=@("-ScriptsForProject=$uproject",$command,"-project=$uproject","-target=$name",'-platform=Win64','-clientconfig=Development','-skipbuild','-nocompileeditor','-stage','-pak','-archive',"-stagingdirectory=$stage","-archivedirectory=$archive",'-unattended','-utf8output')
                if($Operation -eq 'CookStage'){$arguments+=@('-cook','-map=/Engine/Maps/Entry')}else{$arguments+='-skipcook'}
                if($Operation -eq 'SkipStage'){$arguments+=@('-skipstage','-skippak')}
            }
        }
    }
}
$meta=[ordered]@{version=$Version;slot=$Slot;operation=$Operation;executable=$executable;arguments=$arguments;working_directory=$project;started_utc=[DateTime]::UtcNow.ToString('o')}
$meta | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$reports\$Label.command.json" -Encoding utf8
$timer=[Diagnostics.Stopwatch]::StartNew()
Push-Location -LiteralPath $project
try {
    if($Operation -eq 'Run'){
        $quoted=$arguments | ForEach-Object {'"'+$_+'"'}
        $process=Start-Process -FilePath $executable -ArgumentList $quoted -WorkingDirectory $project -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput "$reports\$Label.log" -RedirectStandardError "$reports\$Label-stderr.log"
        $result=$process.ExitCode
    } elseif($TimestampOutput) {
        & $executable @arguments 2>&1 | ForEach-Object {'{0:o} {1}' -f [DateTime]::UtcNow,$_} | Out-File -LiteralPath "$reports\$Label.log" -Encoding utf8
        $result=$LASTEXITCODE
    } else {& $executable @arguments *> "$reports\$Label.log";$result=$LASTEXITCODE}
} finally {Pop-Location}
$meta['exit_code']=$result
$meta['seconds']=$timer.Elapsed.TotalSeconds
$meta | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$reports\$Label.result.json" -Encoding utf8
if($Operation -in @('BuildEditor','BuildGame')){Copy-Item -LiteralPath "$env:LOCALAPPDATA\UnrealBuildTool\Log.txt" -Destination "$reports\$Label-UBT.log"}
if($Operation -in @('CookStage','Stage') -and $result -eq 0){
    foreach($manifest in @('FinalCopyWin64_NonUFSFiles.txt','FinalCopyWin64_UFSFiles.txt','FinalCopyWin64_NonUFSFilesDebug.txt')){
        Copy-Item -LiteralPath "$env:APPDATA\Unreal Engine\AutomationTool\Logs\C+Program+Files+Epic+Games+UE_5.$($Version.Substring(1))\$manifest" -Destination "$reports\$Label-$manifest"
    }
}
Get-Content -LiteralPath "$reports\$Label.log" -Tail 15
exit $result
