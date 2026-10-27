$ErrorActionPreference='Stop'
$project='C:\Users\edenlia\Workspace\UEProjects\SharpTestProject56_2'
$engine='C:\Program Files\Epic Games\UE_5.6'
$reports='C:\Users\edenlia\Workspace\CodeProjects\UnrealSharp-reports\csharp-package\multi-version-20261025'
$busy=Get-CimInstance Win32_Process | Where-Object {$_.Name -match '^(UnrealEditor.*|dotnet|AutomationTool|UnrealBuildTool|SharpTestProject.*)\.exe$' -and $_.CommandLine -notmatch '/nodemode:1|VBCSCompiler\.dll' -and ($_.ExecutablePath -like "$engine\*" -or $_.CommandLine -like "*$engine\*" -or $_.CommandLine -like "*$project\*")}
if($busy){throw "Engine busy: $($busy.ProcessId)"}
$results=@()
Push-Location -LiteralPath $project
try {
    foreach($flag in @('foreign','foreigncode')) {
        $arguments=@("-ScriptsForProject=$project\SharpTestProject56_2.uproject",'BuildCookRunUnrealSharp',"-Project=$project\SharpTestProject56_2.uproject","-$flag")
        & "$engine\Engine\Build\BatchFiles\RunUAT.bat" @arguments *> "$reports\56_2-$flag-rejected.log"
        $result=$LASTEXITCODE
        $log=Get-Content -LiteralPath "$reports\56_2-$flag-rejected.log" -Raw
        if($result -eq 0 -or !$log.Contains('requires an existing project')){throw "Unexpected $flag result: $result"}
        $results+=@{flag=$flag;exit_code=$result;arguments=$arguments;rejected_before_setup=$true}
    }
} finally {Pop-Location}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$reports\legacy-option-checks.json" -Encoding utf8
Write-Output 'foreign and foreigncode rejected as expected'
