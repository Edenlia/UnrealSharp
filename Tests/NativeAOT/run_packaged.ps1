param(
    [Parameter(Mandatory=$true)][string]$ProjectName,
    [Parameter(Mandatory=$true)][string]$PackagedProject,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$Label,
    [Parameter(Mandatory=$true)][ValidateSet('JIT','NativeAOT')][string]$ExpectedMode,
    [switch]$InspectModules,
    [switch]$ExceptionProbe,
    [switch]$Benchmark
)
$ErrorActionPreference='Stop'
$active=Get-CimInstance Win32_Process | Where-Object {
    $_.Name -match '^UnrealEditor|^SharpTestProject' -or
    ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'UnrealBuildTool.dll|AutomationTool.dll')
}
if($active){throw 'A UE build, Editor or test Game is active; run verification sequentially.'}
$exe=Join-Path $PackagedProject ('Binaries/Win64/'+$ProjectName+'.exe')
$prefix=Join-Path $OutputDirectory $Label
$arguments=@('/Game/NativeAotValidation/Validation','-nullrhi','-unattended','-nosound','-nosplash',
    '-stdout','-FullStdOutLogOutput','-NativeAotProbe','-NativeAotMatrix',('-NativeAotExpected='+$ExpectedMode),('-abslog="'+$prefix+'.log"'))
if($ExceptionProbe){$arguments+='-NativeAotExceptionProbe'}
if($Benchmark){$arguments+='-NativeAotBenchmark'}
$watch=[Diagnostics.Stopwatch]::StartNew()
$game=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput ($prefix+'.stdout.log') -RedirectStandardError ($prefix+'.stderr.log')
$modules=[Collections.Generic.HashSet[string]]::new()
if($InspectModules){
    while(!$game.HasExited){
        try {foreach($module in (Get-Process -Id $game.Id -ErrorAction Stop).Modules){
            if($module.ModuleName -match 'AOT|hostfxr|coreclr'){[void]$modules.Add($module.FileName)}
        }}catch{}
        Start-Sleep -Milliseconds 100
        $game.Refresh()
    }
}
$game.WaitForExit()
$watch.Stop()
$gameExit=$game.ExitCode
$log=Get-Content -LiteralPath ($prefix+'.stdout.log') -Raw
$passed=$gameExit -eq 0 -and $log.Contains('NATIVEAOT_GAME_SMOKE PASS') -and
    $log.Contains('NATIVEAOT_MATRIX mask=1023') -and $log.Contains('NATIVEAOT_PUBLISHED_CONTENT native_dependency=42') -and
    $log.Contains('NATIVEAOT_NATIVE_GC collected=1')
if($ExceptionProbe){$passed=$passed -and $log.Contains('NATIVEAOT_EXPECTED_EXCEPTION') -and $log.Contains('NATIVEAOT_EXCEPTION returned_control=1')}
$benchmarkMatch=[regex]::Match($log,'NATIVEAOT_BENCH mode=(\w+) iterations=(\d+) add_ms=([\d.]+) property_ms=([\d.]+) add_checksum=(\d+) property_checksum=(\d+)')
if($Benchmark){
    $passed=$passed -and $benchmarkMatch.Success -and $benchmarkMatch.Groups[1].Value -eq $ExpectedMode -and
        $benchmarkMatch.Groups[2].Value -eq '50000' -and $benchmarkMatch.Groups[5].Value -eq '2100000' -and
        $benchmarkMatch.Groups[6].Value -eq '6150000'
}
if($InspectModules){
    $hasAot=@($modules | Where-Object {$_ -like '*.AOT.dll'}).Count -gt 0
    $hasClr=@($modules | Where-Object {$_ -match 'hostfxr|coreclr'}).Count -gt 0
    $passed=$passed -and $(if($ExpectedMode -eq 'NativeAOT'){$hasAot -and !$hasClr}else{$hasClr -and !$hasAot})
}
$result=[ordered]@{
    label=$Label; expected_mode=$ExpectedMode; exit_code=$gameExit; passed=$passed
    process_wall_ms=$watch.Elapsed.TotalMilliseconds; executable=$exe; modules=@($modules)
}
if($benchmarkMatch.Success){
    $result.iterations=[int]$benchmarkMatch.Groups[2].Value
    $result.add_ms=[double]::Parse($benchmarkMatch.Groups[3].Value,[Globalization.CultureInfo]::InvariantCulture)
    $result.property_ms=[double]::Parse($benchmarkMatch.Groups[4].Value,[Globalization.CultureInfo]::InvariantCulture)
    $result.add_checksum=[long]$benchmarkMatch.Groups[5].Value
    $result.property_checksum=[long]$benchmarkMatch.Groups[6].Value
}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ($prefix+'.json')
$result | ConvertTo-Json -Depth 4
if(!$passed){exit 1}
