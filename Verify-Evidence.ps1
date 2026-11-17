param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'reports\latest'))
$ErrorActionPreference='Stop'
$project=$PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'manifest.json') -Raw | ConvertFrom-Json
$files=@(Get-ChildItem -LiteralPath (Join-Path $project 'src\main'),(Join-Path $project 'tools'),(Join-Path $project 'assets') -Recurse -File)
$files+=Get-Item -LiteralPath (Join-Path $project 'Build.ps1')
$lines=@($files | Sort-Object FullName | ForEach-Object {$_.FullName.Substring($project.Length).Replace('\','/')+' '+(Get-FileHash -LiteralPath $_.FullName).Hash})
$sha=[Security.Cryptography.SHA256]::Create()
try{$sourceHash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))).Replace('-','')}finally{$sha.Dispose()}
if($sourceHash -ne $manifest.source_sha256){throw 'Source/assets changed since the captures. Do not report these screenshots as the latest design.'}
if((Get-FileHash -LiteralPath $manifest.jar).Hash -ne $manifest.jar_sha256){throw 'Packaged JAR changed since capture. Rebuild evidence before reporting.'}
if((Get-FileHash -LiteralPath (Join-Path $project 'dist\GitPort\GitPort.exe')).Hash -ne $manifest.launcher_sha256){throw 'Native launcher changed since capture.'}
if((Get-FileHash -LiteralPath (Join-Path $project 'assets\GitPort.ico')).Hash -ne $manifest.icon_sha256){throw 'Icon changed since capture.'}
foreach($capture in $manifest.screenshots){if((Get-FileHash -LiteralPath $capture.path).Hash -ne $capture.sha256){throw "Capture changed: $($capture.name)"};if([datetimeoffset]$capture.captured_at -lt [datetimeoffset]$manifest.started_at){throw 'A screenshot predates this check run.'}}
$contrast=@(Import-Csv -LiteralPath (Join-Path $EvidenceDirectory 'contrast.csv'));foreach($pair in $contrast){if([double]::Parse($pair.Ratio,[Globalization.CultureInfo]::InvariantCulture) -lt 4.5){throw "Text contrast failed: $($pair.State)"}}
[pscustomobject]@{Fresh=$true;Version=$manifest.version;Completed=$manifest.completed_at;Screenshots=$manifest.screenshots.Count;ContrastPairs=$contrast.Count;SourceHash=$sourceHash;JarHash=$manifest.jar_sha256}
