param(
    [Parameter(Mandatory=$true)][string]$ProjectName,
    [Parameter(Mandatory=$true)][string]$PackagedProject,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$active=Get-CimInstance Win32_Process | Where-Object {
    $_.Name -match '^UnrealEditor|^SharpTestProject' -or
    ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'UnrealBuildTool.dll|AutomationTool.dll')
}
if($active){throw 'A UE build, Editor or test Game is active.'}
$root=[IO.Path]::GetFullPath($PackagedProject)
$managed=[IO.Path]::GetFullPath((Join-Path $root 'Binaries/Managed/net10.0'))
$library=Join-Path $managed ($ProjectName+'.AOT.dll')
$backup=$library+'.loader-test-backup'
if(!$managed.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
    throw 'Test output must stay inside the packaged project.'
}
if(Test-Path -LiteralPath $backup){throw 'A previous loader-test backup exists; restore it first.'}
$originalHash=(Get-FileHash -LiteralPath $library -Algorithm SHA256).Hash
$exe=Join-Path $root ('Binaries/Win64/'+$ProjectName+'.exe')
$results=@()
foreach($test in @('missing-library','missing-export')){
    $prefix=Join-Path $OutputDirectory ('loader-'+$test)
    Move-Item -LiteralPath $library -Destination $backup
    try {
        if($test -eq 'missing-export'){
            Copy-Item -LiteralPath (Join-Path $managed 'NativeAotDependency.dll') -Destination $library
        }
        $game=Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Hidden -PassThru `
            -ArgumentList @('-nullrhi','-unattended','-nosound','-nosplash','-stdout','-FullStdOutLogOutput') `
            -RedirectStandardOutput ($prefix+'.stdout.log') -RedirectStandardError ($prefix+'.stderr.log')
        $game.WaitForExit()
        $log=Get-Content -LiteralPath ($prefix+'.stdout.log') -Raw
        $expected=if($test -eq 'missing-library'){'Failed to load NativeAOT library:'}else{'does not export InitializeAotRuntime.'}
        $passed=$game.ExitCode -ne 0 -and $log.Contains($expected) -and !$log.Contains('NATIVEAOT_MODULE START')
        $results+=[ordered]@{test=$test;exit_code=$game.ExitCode;passed=$passed;expected_message=$expected}
    } finally {
        if(Test-Path -LiteralPath $library){Remove-Item -LiteralPath $library}
        Move-Item -LiteralPath $backup -Destination $library
        if((Get-FileHash -LiteralPath $library -Algorithm SHA256).Hash -ne $originalHash){throw 'AOT library restoration hash mismatch.'}
    }
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'loader-failures.json')
$results | ConvertTo-Json
if($results.Where({!$_.passed}).Count){exit 1}
