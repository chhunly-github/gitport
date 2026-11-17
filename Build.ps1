param([switch]$Package, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot

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
$distDir = Join-Path $project 'dist\GitPort'
$tempPublish = Join-Path $env:TEMP ('gitport-publish-' + [guid]::NewGuid().ToString('N'))
dotnet publish "$project\src-cs\GitPort\GitPort.csproj" -c Release -o $tempPublish
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Copy-Item -Path "$tempPublish\GitPort.*" -Destination $distDir -Force
Remove-Item -Recurse -Force $tempPublish -ErrorAction SilentlyContinue

# Clean any runtime data folders
if (Test-Path (Join-Path $distDir 'data')) { Remove-Item -Recurse -Force (Join-Path $distDir 'data') }
if (Test-Path (Join-Path $distDir 'temp')) { Remove-Item -Recurse -Force (Join-Path $distDir 'temp') }

Write-Host "==> GitPort ready at: $distDir\GitPort.exe" -ForegroundColor Green

if ($Package) {
    $zipDir = Join-Path $project 'zip'
    New-Item -ItemType Directory -Path $zipDir -Force | Out-Null

    Write-Host "==> Packaging source zip: $zipDir\gitport1.0.zip..." -ForegroundColor Cyan
    git archive -o "$zipDir\gitport1.0.zip" HEAD

    Write-Host "==> Packaging portable zip: $zipDir\gitport1.0-portable.zip..." -ForegroundColor Cyan
    $portableZip = Join-Path $zipDir 'gitport1.0-portable.zip'
    if (Test-Path $portableZip) { Remove-Item -Force $portableZip }
    Compress-Archive -Path "$distDir\*" -DestinationPath $portableZip -Force
    Write-Host "==> Packages generated successfully in $zipDir" -ForegroundColor Green
}
