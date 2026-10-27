$ErrorActionPreference='Stop'
$repo='C:\Users\edenlia\Workspace\CodeProjects\UnrealSharp'
$reports='C:\Users\edenlia\Workspace\CodeProjects\UnrealSharp-reports\csharp-package\multi-version-20261025'
$busy=Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(UnrealEditor.*|UnrealBuildTool|AutomationTool|dotnet|SharpTestProject.*)\.exe$' -and $_.CommandLine -notmatch '/nodemode:1|VBCSCompiler\.dll' -and ($_.ExecutablePath -match 'UE_5\.[67]\\|SharpTestProject5[67]_[12]\\' -or $_.CommandLine -match 'UE_5\.[67]\\|SharpTestProject5[67]_[12]\\') }
if($busy){throw "Target engine/project busy: $($busy.ProcessId)"}
& git -C $repo archive --format=zip "--output=$reports\step2.zip" 89707cded9b453751d11525170354b0c03133025
if($LASTEXITCODE -ne 0){throw 'Archive failed'}
$changed=@('Build\Scripts\UnrealSharp.Automation.csproj','Build\Scripts\Staging\UnrealSharpStagingHandler.cs','Build\Scripts\Utilities\UnrealSharpStaging.cs','Build\Scripts\BuildCommands\BuildCookRunUnrealSharp.cs')
foreach($version in @('56','57')) {
    $project="C:\Users\edenlia\Workspace\UEProjects\SharpTestProject${version}_2"
    $plugin=[IO.Path]::GetFullPath("$project\Plugins\UnrealSharp")
    $backup=[IO.Path]::GetFullPath("$project\Saved\CSharpPackageMulti-20261025\step2-plugin")
    if(!$plugin.StartsWith($project+'\') -or !$backup.StartsWith($project+'\')){throw 'Path escaped'}
    if(Test-Path -LiteralPath $backup){throw 'Backup exists'}
    if((Get-Item -LiteralPath $project).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse project'}
    if((Get-Item -LiteralPath $plugin).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse plugin'}
    if(Get-ChildItem -LiteralPath $plugin -Recurse -Force -Attributes ReparsePoint){throw 'Nested reparse plugin'}
    Move-Item -LiteralPath $plugin -Destination $backup
    Expand-Archive -LiteralPath "$reports\step2.zip" -DestinationPath $plugin
    foreach($relative in $changed){Copy-Item -LiteralPath "$repo\$relative" -Destination "$plugin\$relative"}
    $now=[DateTime]::UtcNow
    Get-ChildItem -LiteralPath $plugin -Recurse -File | ForEach-Object {$_.LastWriteTimeUtc=$now}
    Get-ChildItem -LiteralPath $plugin -Recurse -File | ForEach-Object {[ordered]@{path=$_.FullName.Substring($plugin.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$reports\${version}_2-deployment-step3.json" -Encoding utf8
    Write-Output "Deployed $project"
}
$changed | ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath "$repo\$_").Hash}} | ConvertTo-Json | Set-Content -LiteralPath "$reports\step3-pending-source.json" -Encoding utf8
