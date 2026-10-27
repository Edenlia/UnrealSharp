param([ValidateSet('A','B')][string]$Slot)
$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\edenlia\Workspace\CodeProjects\UnrealSharp'
$reports = 'C:\Users\edenlia\Workspace\CodeProjects\UnrealSharp-reports\csharp-package'
$project = if ($Slot -eq 'A') { 'C:\Users\edenlia\Workspace\UEProjects\SharpTestProject' } else { 'C:\Users\edenlia\Workspace\UEProjects\SharpTestProject1' }
$project = (Get-Item -LiteralPath $project).FullName
$saved = Join-Path $project 'Saved\UnrealSharpAB\20261025-csharp-package'
if (Test-Path -LiteralPath $saved) { throw "Already prepared: $saved" }
$busy = Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(UnrealEditor.*|UnrealBuildTool|AutomationTool|cl|link)\.exe$' -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'UnrealBuildTool|AutomationTool') }
if ($busy) { throw "Build/editor process present: $($busy.ProcessId)" }
New-Item -ItemType Directory -Path "$saved\before" | Out-Null
$archive = Join-Path $reports 'baseline.zip'
if (!(Test-Path -LiteralPath $archive)) {
    & git -C $repo archive --format=zip "--output=$archive" cb8577e3a47b5bdc5454239a17f61a6282a50ae2
    if ($LASTEXITCODE -ne 0) { throw 'git archive failed' }
}
foreach ($relative in @('Plugins\UnrealSharp', 'Script', 'Binaries\Managed', 'Intermediate\UnrealSharp', 'Build\UnrealSharp')) {
    $path = [IO.Path]::GetFullPath((Join-Path $project $relative))
    $destination = [IO.Path]::GetFullPath((Join-Path "$saved\before" ($relative.Replace('\','-'))))
    if (!$path.StartsWith($project + '\', [StringComparison]::OrdinalIgnoreCase) -or !$destination.StartsWith($project + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped project' }
    if (Test-Path -LiteralPath $path) {
        $reparse = Get-Item -LiteralPath $path | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }
        $reparse += @(Get-ChildItem -LiteralPath $path -Recurse -Force -Attributes ReparsePoint)
        if ($reparse) { throw "Reparse point in $path" }
        Move-Item -LiteralPath $path -Destination $destination
    }
}
$plugin = Join-Path $project 'Plugins\UnrealSharp'
Expand-Archive -LiteralPath $archive -DestinationPath $plugin
if ($Slot -eq 'B') {
    foreach ($relative in @('Build\Scripts\BuildCommands\PackageProject.cs','Build\Scripts\UnrealSharp.Automation.csproj','Build\Scripts\Staging\UnrealSharpStagingHandler.cs')) {
        $target = Join-Path $plugin $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $target
    }
}
# An archive's historical timestamps must not let UBT reuse a previous slot's rules assembly.
$now = [DateTime]::UtcNow
Get-ChildItem -LiteralPath $plugin -Recurse -File | ForEach-Object { $_.LastWriteTimeUtc = $now }
$manifest = Get-ChildItem -LiteralPath $plugin -Recurse -File | ForEach-Object {
    [ordered]@{ path=$_.FullName.Substring($plugin.Length + 1); size=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$reports\deployment-$Slot.json" -Encoding utf8
$probe = Join-Path $project 'Script\PackageStageProbe'
New-Item -ItemType Directory -Path $probe -Force | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <Import Project="../../Plugins/UnrealSharp/UnrealSharp.Shared.props" />
  <ItemGroup>
    <Content Include="package-stage.txt" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath "$probe\PackageStageProbe.csproj" -Encoding utf8
@'
using UnrealSharp.Attributes;
using UnrealSharp.Engine.Core.Modules;
namespace PackageStageProbe;
[UModule]
public class FPackageStageProbe : IModuleInterface
{
    public void StartupModule()
    {
#if WITH_GAME && WITH_DEVELOPMENT
        const string target = "Game/Development";
#elif WITH_EDITOR
        const string target = "Editor";
#else
        const string target = "Unexpected";
#endif
        string directory = UnrealSharp.UnrealSharpUtilities.UCSPathsBlueprintFunctionLibrary.UserAssemblyDirectory;
        string content = File.Exists(Path.Combine(directory, "package-stage.txt")) ? File.ReadAllText(Path.Combine(directory, "package-stage.txt")).Trim() : "missing";
        Console.WriteLine($"PACKAGE_STAGE_PROBE version=1 target={target} content={content} directory={directory}");
    }
    public void ShutdownModule() { }
}
'@ | Set-Content -LiteralPath "$probe\Probe.cs" -Encoding utf8
'package-content-v1' | Set-Content -LiteralPath "$probe\package-stage.txt" -Encoding utf8
Write-Output "Prepared slot $Slot at $project; recovery $saved\before"
