param([int]$Start=0)
$ErrorActionPreference='Stop'
$runner=Join-Path $PSScriptRoot 'InvokeMultiVersion.ps1'
$shell=(Get-Process -Id $PID).Path
$steps=@(
    @('56','1','BuildEditor'),@('56','1','BuildGame'),@('56','1','Initialize'),@('56','1','Managed'),@('56','1','CookStage'),@('56','1','Manual'),@('56','1','Run'),
    @('56','2','BuildEditor'),@('56','2','BuildGame'),@('56','2','Initialize'),@('56','2','Managed'),@('56','2','CookStage'),@('56','2','Run'),@('56','2','Boundary'),
    @('57','1','BuildEditor'),@('57','1','BuildGame'),@('57','1','Initialize'),@('57','1','Managed'),@('57','1','CookStage'),@('57','1','Manual'),@('57','1','Run'),
    @('57','2','BuildEditor'),@('57','2','BuildGame'),@('57','2','Initialize'),@('57','2','Managed'),@('57','2','CookStage'),@('57','2','Run'),@('57','2','Boundary')
)
for($index=$Start;$index -lt $steps.Count;$index++){
    $step=$steps[$index]
    Write-Output "Starting $($step -join ' ')"
    $arguments=@('-NoProfile','-File',$runner,'-Version',$step[0],'-Slot',$step[1],'-Operation',$step[2])
    if($step[0] -eq '56' -and $step[1] -eq '1'){$arguments+=@('-Label',"56_1-control-$($step[2])")}
    if($step[0] -eq '56' -and $step[1] -eq '2' -and $step[2] -eq 'BuildEditor'){$arguments+=@('-Label','56_2-BuildEditor-v2')}
    if($step[0] -eq '56' -and $step[1] -eq '2' -and $step[2] -eq 'Initialize'){$arguments+=@('-Label','56_2-Initialize-v2')}
    if($step[0] -eq '56' -and $step[1] -eq '2'){$arguments+='-LegacyEntry'}
    & $shell @arguments
    if($LASTEXITCODE -ne 0){throw "Failed $($step -join ' '): $LASTEXITCODE"}
}
