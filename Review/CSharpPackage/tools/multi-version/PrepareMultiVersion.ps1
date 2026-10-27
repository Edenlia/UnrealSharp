$ErrorActionPreference='Stop'
$repo='C:\Users\edenlia\Workspace\CodeProjects\UnrealSharp'
$reports='C:\Users\edenlia\Workspace\CodeProjects\UnrealSharp-reports\csharp-package\multi-version-20261025'
if(Test-Path -LiteralPath $reports){throw 'Run directory exists'}
$busy=Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(UnrealEditor.*|UnrealBuildTool|AutomationTool|dotnet|SharpTestProject.*)\.exe$' -and $_.CommandLine -notmatch '/nodemode:1|VBCSCompiler\.dll' -and ($_.ExecutablePath -match 'UE_5\.[67]\\|SharpTestProject5[67]_[12]\\' -or $_.CommandLine -match 'UE_5\.[67]\\|SharpTestProject5[67]_[12]\\') }
if($busy){throw "Target engine/project busy: $($busy.ProcessId)"}
New-Item -ItemType Directory -Path $reports | Out-Null
& git -C $repo archive --format=zip "--output=$reports\base.zip" cb8577e3a47b5bdc5454239a17f61a6282a50ae2
if($LASTEXITCODE -ne 0){throw 'Base archive failed'}
& git -C $repo archive --format=zip "--output=$reports\step1.zip" 1723b7ba619b8e4ee778dde672cf9983e63cd6a9
if($LASTEXITCODE -ne 0){throw 'Step1 archive failed'}
foreach($version in @('56','57')) {
    foreach($slot in @('1','2')) {
        $name="SharpTestProject${version}_$slot"
        $project="C:\Users\edenlia\Workspace\UEProjects\$name"
        $saved="$project\Saved\CSharpPackageMulti-20261025"
        if(Test-Path -LiteralPath $saved){throw 'Recovery path exists'}
        if((Get-Item -LiteralPath $project).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse project'}
        New-Item -ItemType Directory -Path "$saved\before" | Out-Null
        $inputs=@('AutoBuild.bat','AutoBuildGame.bat','project_env.config.txt',"$name.uproject") | ForEach-Object {Get-Item -LiteralPath "$project\$_"}
        $inputs+=@(Get-ChildItem -LiteralPath "$project\Source","$project\Config" -Recurse -File)
        $inputs | ForEach-Object {[ordered]@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$reports\${version}_$slot-inputs.json" -Encoding utf8
        foreach($relative in @('Plugins\UnrealSharp','Script','Binaries\Managed','Intermediate\UnrealSharp','Build\UnrealSharp')) {
            $path=[IO.Path]::GetFullPath((Join-Path $project $relative))
            $destination=[IO.Path]::GetFullPath((Join-Path "$saved\before" $relative.Replace('\','-')))
            if(!$path.StartsWith($project+'\',[StringComparison]::OrdinalIgnoreCase) -or !$destination.StartsWith($project+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Path escaped'}
            if(Test-Path -LiteralPath $path){
                if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse input'}
                if(Get-ChildItem -LiteralPath $path -Recurse -Force -Attributes ReparsePoint){throw 'Nested reparse input'}
                Move-Item -LiteralPath $path -Destination $destination
            }
        }
        $plugin="$project\Plugins\UnrealSharp"
        $archive=if($slot -eq '1'){"$reports\base.zip"}else{"$reports\step1.zip"}
        Expand-Archive -LiteralPath $archive -DestinationPath $plugin
        if($slot -eq '2'){Copy-Item -LiteralPath "$repo\Build\Scripts\Utilities\ProjectUtilities.cs" -Destination "$plugin\Build\Scripts\Utilities\ProjectUtilities.cs"}
        $now=[DateTime]::UtcNow
        Get-ChildItem -LiteralPath $plugin -Recurse -File | ForEach-Object {$_.LastWriteTimeUtc=$now}
        Get-ChildItem -LiteralPath $plugin -Recurse -File | ForEach-Object {[ordered]@{path=$_.FullName.Substring($plugin.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$reports\${version}_$slot-deployment-step2.json" -Encoding utf8
        $probe="$project\Script\PackageStageProbe"
        New-Item -ItemType Directory -Path $probe -Force | Out-Null
        @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <Import Project="../../Plugins/UnrealSharp/UnrealSharp.Shared.props" />
  <ItemGroup><Content Include="package-stage.txt" CopyToPublishDirectory="PreserveNewest" /></ItemGroup>
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
        const int version = 1;
        string directory = UnrealSharp.UnrealSharpUtilities.UCSPathsBlueprintFunctionLibrary.UserAssemblyDirectory;
        string path = Path.Combine(directory, "package-stage.txt");
        string content = File.Exists(path) ? File.ReadAllText(path).Trim() : "missing";
        Console.WriteLine($"PACKAGE_STAGE_PROBE version={version} target={target} content={content} directory={directory}");
        string? report = Environment.GetEnvironmentVariable("PACKAGE_STAGE_PROBE_RESULT");
        if (!string.IsNullOrEmpty(report))
            File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { version, target, content, directory, runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), process = Environment.ProcessPath }));
    }
    public void ShutdownModule() { }
}
'@ | Set-Content -LiteralPath "$probe\Probe.cs" -Encoding utf8
        'package-content-v1' | Set-Content -LiteralPath "$probe\package-stage.txt" -Encoding utf8
        Write-Output "Prepared $name"
    }
}
