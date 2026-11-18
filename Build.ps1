param([switch]$Package, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$distDir = Join-Path $project 'dist\GitPort'
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Write-Host "==> Building GitPort (WPF on .NET)..." -ForegroundColor Cyan
dotnet build "$project\src-cs\GitPort\GitPort.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if (-not $SkipTests) {
    Write-Host "==> Running unit tests..." -ForegroundColor Cyan
    dotnet test "$project\src-cs\GitPort.Tests\GitPort.Tests.csproj" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

# Ensure FolderPicker.exe is built
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (Test-Path -LiteralPath $csc) {
    $pickerSource = Join-Path $project 'tools\FolderPicker.cs'
    $pickerManifest = Join-Path $project 'tools\FolderPicker.manifest'
    $pickerExe = Join-Path $project 'dist\GitPort\FolderPicker.exe'
    if (Test-Path -LiteralPath $pickerSource) {
        if (Test-Path -LiteralPath $pickerManifest) {
            & $csc /nologo /target:winexe /win32manifest:$pickerManifest /out:$pickerExe $pickerSource
        } else {
            & $csc /nologo /target:winexe /out:$pickerExe $pickerSource
        }
    }
}

# Publish to dist\GitPort
Write-Host "==> Publishing release binaries to dist\GitPort..." -ForegroundColor Cyan
$tempPublish = Join-Path $env:TEMP ('gitport-publish-' + [guid]::NewGuid().ToString('N'))
dotnet publish "$project\src-cs\GitPort\GitPort.csproj" -c Release -o $tempPublish
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Copy-Item -Path "$tempPublish\GitPort.*" -Destination $distDir -Force
Copy-Item -LiteralPath (Join-Path $project 'README.md'), (Join-Path $project 'THIRD-PARTY.md') -Destination $distDir -Force
$resolvedPublish = [System.IO.Path]::GetFullPath($tempPublish)
$expectedPrefix = Join-Path ([System.IO.Path]::GetFullPath($env:TEMP)) 'gitport-publish-'
if (-not $resolvedPublish.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected publish directory: $resolvedPublish"
}
Remove-Item -LiteralPath $resolvedPublish -Recurse -Force -ErrorAction SilentlyContinue

# Preserve runtime data, including repository assignments and encrypted profiles.

Write-Host "==> GitPort ready at: $distDir\GitPort.exe" -ForegroundColor Green

if ($Package) {
    $zipDir = Join-Path $project 'zip'
    New-Item -ItemType Directory -Path $zipDir -Force | Out-Null

    Write-Host "==> Packaging committed source snapshot: $zipDir\gitport1.0.zip..." -ForegroundColor Cyan
    git archive -o "$zipDir\gitport1.0.zip" HEAD

    Write-Host "==> Packaging portable zip: $zipDir\gitport1.0-portable.zip..." -ForegroundColor Cyan
    $portableZip = Join-Path $zipDir 'gitport1.0-portable.zip'
    if (Test-Path $portableZip) { Remove-Item -Force $portableZip }
    $packageFiles = @(Get-ChildItem -LiteralPath $distDir | Where-Object { $_.Name -notin @('data', 'temp') } | Select-Object -ExpandProperty FullName)
    Compress-Archive -Path $packageFiles -DestinationPath $portableZip -Force
    Write-Host "==> Packages generated successfully in $zipDir" -ForegroundColor Green
}
