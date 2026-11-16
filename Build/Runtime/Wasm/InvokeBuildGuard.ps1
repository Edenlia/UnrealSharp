param(
    [Parameter(Mandatory=$true)][string]$EngineDirectory,
    [Parameter(Mandatory=$true)][string]$PluginDirectory,
    [Parameter(Mandatory=$true)][string]$ProjectDirectory,
    [Parameter(Mandatory=$true)][string]$TargetName,
    [Parameter(Mandatory=$true)][string]$Platform,
    [Parameter(Mandatory=$true)][string]$Configuration,
    [Parameter(Mandatory=$true)][string]$TargetType
)
$ErrorActionPreference = 'Stop'
if ($Platform -ne 'Wasm') { exit 0 }
try {
    if ($env:OS -ne 'Windows_NT' -or $TargetType -ne 'Game') { throw 'Wasm static CoreCLR currently requires a Windows-hosted Game build.' }
    $plugin = [IO.Path]::GetFullPath($PluginDirectory)
    $engine = [IO.Path]::GetFullPath($EngineDirectory)
    $project = [IO.Path]::GetFullPath($ProjectDirectory)
    if ($plugin -ne [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))) { throw 'The guard script is not from the requested plugin.' }
    if ($TargetName -notmatch '^[A-Za-z0-9_]+$' -or $Configuration -notin @('Debug','DebugGame','Development','Shipping','Test')) { throw 'Invalid Wasm target/configuration.' }
    $projects = @(Get-ChildItem -LiteralPath $project -File | Where-Object { $_.Extension -ieq '.uproject' })
    if ($projects.Count -ne 1) { throw 'Wasm guard requires one explicit code project in ProjectDir.' }
    $inputPath = Join-Path $project "Intermediate/UnrealSharp/Wasm/NativeBuild/$TargetName/$Configuration/wasm64/BuildInput.json"
    $buildInput = Get-Content -LiteralPath $inputPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($buildInput.Schema -ne 'UnrealSharp.WasmBuildInput' -or $buildInput.Profile.PluginDirectory -ne $plugin -or
        $buildInput.Profile.ProjectFile -ne $projects[0].FullName -or $buildInput.Profile.EngineDirectory -ne $engine) { throw 'No matching frozen native-build selection.' }

    # Public Win32 file identity supports the selected project's existing ancestor
    # junction. New links beneath the plugin/tool root are never authorized.
    Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles;
public static class UnrealSharpGuardPath {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string p,uint a,uint s,IntPtr z,uint c,uint f,IntPtr t);
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle h,StringBuilder b,uint n,uint f);
 public static string Physical(string p) {
  using(var h=CreateFile(p,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)) {
   if(h.IsInvalid) throw new System.IO.IOException("Cannot identify guard plugin path");
   var b=new StringBuilder(32768); uint n=GetFinalPathNameByHandle(h,b,(uint)b.Capacity,0);
   if(n==0 || n>=b.Capacity) throw new System.IO.IOException("Cannot resolve guard plugin path");
   string r=b.ToString(); return r.StartsWith(@"\\?\UNC\")?@"\\"+r.Substring(8):r.StartsWith(@"\\?\")?r.Substring(4):r;
  }
 }
}
'@
    $physicalPlugin = [UnrealSharpGuardPath]::Physical($plugin)
    $recordedPlugin = $buildInput.PathIdentities.PSObject.Properties | Where-Object { $_.Name -eq $plugin }
    if (!$recordedPlugin -or $recordedPlugin.Value -ne $physicalPlugin) { throw 'Plugin ancestor identity differs from BuildInput.' }
    function Assert-ToolPath([string]$path) {
        if ([UnrealSharpGuardPath]::Physical($plugin) -ne $physicalPlugin) { throw 'Plugin ancestor changed during guard bootstrap.' }
        $full = [IO.Path]::GetFullPath($path)
        if (!$full.StartsWith($plugin.TrimEnd([char[]]'\/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Guard tool output escaped the plugin.' }
        $cursor = $plugin
        foreach ($part in $full.Substring($plugin.Length).TrimStart([char[]]'\/').Split([char[]]'\/')) {
            if (!$part) { continue }; $cursor = Join-Path $cursor $part
            if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Unverified link in guard tool output: $cursor" }
        }
    }
    $dotnet = Join-Path $engine 'Binaries/ThirdParty/DotNet/10.0/win-x64/dotnet.exe'
    if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw 'The selected UE 5.9 Windows x64 bundled .NET host is missing.' }
    $sources = @($PSCommandPath, (Join-Path $PSScriptRoot 'BuildGuard/UnrealSharp.Wasm.BuildGuard.csproj'),
        (Join-Path $PSScriptRoot 'BuildGuard/Program.cs'), (Join-Path $PSScriptRoot 'BuildGuard/WindowsUbtInvocation.cs'),
        (Join-Path $plugin 'Source/ThirdParty/UnrealSharpWasmRuntime/UnrealSharpWasmRuntime.Build.cs'), $dotnet)
    function File-Hash([string]$path) {
        $stream = [IO.File]::OpenRead($path); $sha = [Security.Cryptography.SHA256]::Create()
        try { ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','') } finally { $sha.Dispose(); $stream.Dispose() }
    }
    function Source-Key {
        $text = ($sources | Sort-Object | ForEach-Object { $_ + ':' + (File-Hash $_) }) -join "`n"
        $sha = [Security.Cryptography.SHA256]::Create()
        try { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() }
    }
    $key = Source-Key
    $base = Join-Path $plugin 'Intermediate/UnrealSharp/BuildGuardTools'
    Assert-ToolPath $base; [IO.Directory]::CreateDirectory($base) | Out-Null; Assert-ToolPath $base
    $lockPath = Join-Path $base ($key + '.lock'); Assert-ToolPath $lockPath
    $toolLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try {
        $tool = Join-Path $base $key; Assert-ToolPath $tool
        if (!(Test-Path -LiteralPath $tool)) {
            $temporary = Join-Path $base ($key + '.tmp.' + [Guid]::NewGuid().ToString('N')); Assert-ToolPath $temporary
            & $dotnet build (Join-Path $PSScriptRoot 'BuildGuard/UnrealSharp.Wasm.BuildGuard.csproj') --configuration Release --artifacts-path $temporary --verbosity quiet
            if ($LASTEXITCODE -ne 0) { throw 'The normal managed guard build failed; its output is preserved.' }
            if ((Source-Key) -ne $key) { throw 'Guard source changed during its managed build.' }
            Assert-ToolPath $temporary
            $files = @(Get-ChildItem -LiteralPath (Join-Path $temporary 'bin/UnrealSharp.Wasm.BuildGuard/release') -File | ForEach-Object {
                @{Name=$_.Name;Sha256=(File-Hash $_.FullName)}
            })
            @{SourceKey=$key;Files=$files} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $temporary 'ToolReceipt.json') -Encoding UTF8
            Assert-ToolPath $tool; [IO.Directory]::Move($temporary, $tool)
        }
        Assert-ToolPath $tool
        $receipt = Get-Content -LiteralPath (Join-Path $tool 'ToolReceipt.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($receipt.SourceKey -ne $key -or (Source-Key) -ne $key) { throw 'Guard tool source identity changed.' }
        $bin = Join-Path $tool 'bin/UnrealSharp.Wasm.BuildGuard/release'
        foreach ($file in $receipt.Files) {
            if ([IO.Path]::GetFileName($file.Name) -ne $file.Name) { throw 'Invalid guard tool receipt path.' }
            $path = Join-Path $bin $file.Name; Assert-ToolPath $path
            if ((File-Hash $path) -ne $file.Sha256) { throw 'Guard tool output changed.' }
        }
        & $dotnet (Join-Path $bin 'UnrealSharp.Wasm.BuildGuard.dll') --engine $engine --plugin $plugin --project $projects[0].FullName --target $TargetName --platform $Platform --configuration $Configuration --type $TargetType
        exit $LASTEXITCODE
    } finally { $toolLock.Dispose() }
} catch {
    Write-Error ("UnrealSharp Wasm guard rejected: " + $_.Exception.Message)
    exit 1
}
