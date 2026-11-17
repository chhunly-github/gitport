param([string]$Jdk='C:\Program Files\Java\jdk-21.0.12',[string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'reports\latest'),[datetimeoffset]$Deadline=[datetimeoffset]::MaxValue)
$ErrorActionPreference='Stop'
if([datetimeoffset]::Now -ge $Deadline){throw 'The authorized refinement deadline has passed. Review saved evidence without changing the design.'}
$project=$PSScriptRoot
New-Item -ItemType Directory -Path $EvidenceDirectory,(Join-Path $project 'reports') -Force | Out-Null
$lockPath=Join-Path $project 'reports\check.lock'
try{$checkLock=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Another GitPort check is running. No overlapping build was started.'}
function Get-DesignHash {
    $files=@(Get-ChildItem -LiteralPath (Join-Path $project 'src\main'),(Join-Path $project 'tools'),(Join-Path $project 'assets') -Recurse -File)
    $files+=Get-Item -LiteralPath (Join-Path $project 'Build.ps1')
    $lines=@($files | Sort-Object FullName | ForEach-Object { $_.FullName.Substring($project.Length).Replace('\','/')+' '+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash })
    $sha=[Security.Cryptography.SHA256]::Create()
    try{return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))).Replace('-','')}finally{$sha.Dispose()}
}
$previousLocation=Get-Location
try{
    Set-Location -LiteralPath $project
    $start=[datetimeoffset]::Now;$sourceHash=Get-DesignHash
    $running=@(Get-Process -Name GitPort -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $project 'dist\GitPort\GitPort.exe') })
    if($running.Count -gt 0){throw 'Close the staging GitPort app before replacing its build. No process was terminated.'}
    $env:GITPORT_TEST_GIT=Join-Path $project 'dist\GitPort\git\cmd\git.exe'
    & (Join-Path $project 'Build.ps1') -Jdk $Jdk *>&1 | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'tests.log')
    $jar=Join-Path $project 'dist\GitPort\app\gitport.jar'
    $backup=Join-Path $project ('build\previous-jars\'+$start.ToString('yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    if(Test-Path -LiteralPath $jar){Copy-Item -LiteralPath $jar -Destination $backup}
    Copy-Item -LiteralPath (Join-Path $project 'build\input\gitport.jar') -Destination $jar -Force
    Copy-Item -LiteralPath (Join-Path $project 'build\contrast.csv') -Destination (Join-Path $EvidenceDirectory 'contrast.csv')
    $screenshots=Join-Path $EvidenceDirectory ('screenshots-'+$start.ToString('yyyyMMdd-HHmmss'))
    & (Join-Path $Jdk 'bin\java.exe') -cp "$jar;$(Join-Path $project 'build\test-classes')" gitport.UiAcceptanceTest $screenshots *>&1 | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'ui-tests.log')
    if($LASTEXITCODE -ne 0){throw 'UI acceptance checks failed. No successful evidence manifest was written.'}
    if((Get-DesignHash) -ne $sourceHash){throw 'Source changed during testing. Captures are stale; rebuild before reporting.'}
    $captures=@(Get-ChildItem -LiteralPath $screenshots -Filter *.png | ForEach-Object { @{name=$_.Name;path=$_.FullName;captured_at=$_.LastWriteTimeUtc.ToString('o');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
    if($captures.Count -lt 7){throw 'Expected seven fresh UI captures.'}
    $manifest=@{version='0.2.0';source_sha256=$sourceHash;jar_sha256=(Get-FileHash -LiteralPath $jar).Hash;jar=$jar;launcher_sha256=(Get-FileHash -LiteralPath (Join-Path $project 'dist\GitPort\GitPort.exe')).Hash;icon_sha256=(Get-FileHash -LiteralPath (Join-Path $project 'assets\GitPort.ico')).Hash;started_at=$start.ToString('o');completed_at=[datetimeoffset]::Now.ToString('o');screenshots=$captures;capture_type='Rendered app components; real Git operations on disposable generated fixture repository; sample bookmark names are test data';git_version=(& $env:GITPORT_TEST_GIT --version);limitations=@('Live HTTPS/SSH remote authentication not verified','No live remote credentials or company repositories used','Large production-repository performance and clean-Windows recovery not verified')}
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'manifest.json') -Encoding utf8
    if($EvidenceDirectory -eq (Join-Path $project 'reports\latest')){& (Join-Path $project 'Refresh-Report.ps1')}
    Write-Host "Fresh verified evidence: $EvidenceDirectory\manifest.json"
}finally{Set-Location -LiteralPath $previousLocation;$checkLock.Dispose()}
