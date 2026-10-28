$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$engine = Split-Path (Split-Path $project -Parent) -Parent
$workspace = Split-Path $engine -Parent
$repo = Join-Path $workspace 'CodeProjects/UnrealSharp'
$report = Join-Path $workspace 'CodeProjects/UnrealSharp-reports/csharp-package-client-server-20261028'
$snapshot = Join-Path $report 'source'
$plugin = [IO.Path]::GetFullPath((Join-Path $engine 'Engine/Plugins/UnrealSharp'))
$head = (& git -C $repo log -1 --format=%H).Trim()
if ($head -ne 'b59ff754ada2ad10c02b65f590c5f18585f8271c' -or @(& git -C $repo status --porcelain).Count) { throw 'Source identity changed' }
if (@(& git -C $engine status --porcelain -- Engine/Plugins/UnrealSharp).Count) { throw 'Host plugin has existing changes' }
if (@(Get-ChildItem -LiteralPath $plugin -Recurse -Force -Attributes ReparsePoint).Count) { throw 'Plugin contains links' }
if ((Resolve-Path $plugin).Path -ne $plugin -or !$plugin.StartsWith([IO.Path]::GetFullPath($engine)+'\')) { throw 'Unsafe plugin path' }
if (Test-Path "$project/Plugins/UnrealSharp") { throw 'Project plugin duplicate' }
$busy = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(UnrealEditor.*|UnrealBuildTool|AutomationTool|dotnet|CSharpPackage.*)\.exe$' -and ($_.ExecutablePath -like "$engine\*" -or $_.CommandLine -like "*$engine*") -and $_.CommandLine -notmatch 'VBCSCompiler|/nodemode:' })
if ($busy.Count) { throw "Engine busy: $($busy.ProcessId)" }
$backup = Join-Path $report 'host-plugin-before'
if (Test-Path $backup) { throw 'Deployment already prepared' }
$old = @(& git -C $engine ls-files Engine/Plugins/UnrealSharp)
$changes = @()
foreach ($tracked in $old) {
    $relative = $tracked.Substring('Engine/Plugins/UnrealSharp/'.Length)
    $oldPath = [IO.Path]::GetFullPath((Join-Path $plugin $relative))
    if (!$oldPath.StartsWith($plugin+'\')) { throw 'Tracked path escaped plugin' }
    $backupPath = Join-Path $backup $relative
    New-Item -ItemType Directory -Path (Split-Path $backupPath) -Force | Out-Null
    Copy-Item -LiteralPath $oldPath -Destination $backupPath
    if (!(Test-Path -LiteralPath (Join-Path $snapshot $relative))) {
        Remove-Item -LiteralPath $oldPath
        $changes += @{path=$relative;action='remove'}
    }
}
$manifest = @(Get-ChildItem -LiteralPath $snapshot -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($snapshot.Length+1)
    $hash = (Get-FileHash -LiteralPath $_.FullName).Hash
    $dst = Join-Path $plugin $relative
    if (!(Test-Path -LiteralPath $dst) -or (Get-FileHash -LiteralPath $dst).Hash -ne $hash) {
        New-Item -ItemType Directory -Path (Split-Path $dst) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $dst
        $changes += @{path=$relative;action='copy'}
    }
    if ((Get-FileHash -LiteralPath $dst).Hash -ne $hash) { throw "Deployment mismatch: $relative" }
    @{path=$relative;size=$_.Length;sha256=$hash}
})
$manifest | ConvertTo-Json -Depth 4 | Set-Content "$report/source-manifest.json"
$changes | ConvertTo-Json -Depth 4 | Set-Content "$report/deployment-changes.json"
foreach ($name in @('AutoBuild.bat','AutoBuildClient.bat','AutoBuildServer.bat','AutoBuildTools.bat','project_env.config.txt')) {
    $path = Join-Path $project $name
    $content = [IO.File]::ReadAllText($path).Replace("`r`n","`n").Replace("`n","`r`n")
    [IO.File]::WriteAllText($path,$content,[Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath "$engine/Engine/Build/Build.version" -Destination "$report/Build.version"
Copy-Item -LiteralPath "$project/AutoBuild.bat","$project/AutoBuildClient.bat","$project/AutoBuildServer.bat","$project/AutoBuildTools.bat","$project/project_env.config.txt" -Destination $report
$inputs = @(Get-ChildItem -LiteralPath $project -Recurse -File | ForEach-Object { @{path=$_.FullName.Substring($project.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
$inputs | ConvertTo-Json -Depth 4 | Set-Content "$report/project-inputs.json"
@{repo=$repo;head=$head;engine=$engine;engine_head=(& git -C $engine log -1 --format=%H).Trim();project=$project;plugin=$plugin;source_files=$manifest.Count;old_source_files=$old.Count;backup=$backup} | ConvertTo-Json | Set-Content "$report/identity.json"
Get-Content "$report/identity.json"
