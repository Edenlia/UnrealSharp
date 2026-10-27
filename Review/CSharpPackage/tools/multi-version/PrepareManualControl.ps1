$ErrorActionPreference='Stop'
$project='C:\Users\edenlia\Workspace\UEProjects\SharpTestProject56_1'
$reports='C:\Users\edenlia\Workspace\CodeProjects\UnrealSharp-reports\csharp-package\multi-version-20261025'
$busy=Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(UnrealEditor.*|UnrealBuildTool|AutomationTool|dotnet|SharpTestProject.*)\.exe$' -and $_.CommandLine -notmatch '/nodemode:1|VBCSCompiler\.dll' -and ($_.ExecutablePath -match 'UE_5\.6\\|SharpTestProject56_[12]\\' -or $_.CommandLine -match 'UE_5\.6\\|SharpTestProject56_[12]\\') }
if($busy){throw "Engine/project busy: $($busy.ProcessId)"}
$plugin=[IO.Path]::GetFullPath("$project\Plugins\UnrealSharp")
$backup=[IO.Path]::GetFullPath("$project\Saved\CSharpPackageMulti-20261025\original-base-plugin")
if(!$plugin.StartsWith($project+'\') -or !$backup.StartsWith($project+'\')){throw 'Path escaped'}
if(Test-Path -LiteralPath $backup){throw 'Backup exists'}
if((Get-Item -LiteralPath $project).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse project'}
if((Get-Item -LiteralPath $plugin).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse plugin'}
if(Get-ChildItem -LiteralPath $plugin -Recurse -Force -Attributes ReparsePoint){throw 'Nested reparse plugin'}
Move-Item -LiteralPath $plugin -Destination $backup
Expand-Archive -LiteralPath "$reports\step2.zip" -DestinationPath $plugin
$now=[DateTime]::UtcNow
Get-ChildItem -LiteralPath $plugin -Recurse -File | ForEach-Object {$_.LastWriteTimeUtc=$now}
Get-ChildItem -LiteralPath $plugin -Recurse -File | ForEach-Object {[ordered]@{path=$_.FullName.Substring($plugin.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$reports\56_1-manual-control-deployment.json" -Encoding utf8
