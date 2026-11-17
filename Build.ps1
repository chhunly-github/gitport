param([string]$Jdk = $env:JAVA_HOME, [switch]$Package, [string]$GitSource)
$ErrorActionPreference = 'Stop'
if (-not $Jdk) {
    $compiler = Get-Command javac.exe -ErrorAction SilentlyContinue
    if ($compiler) { $Jdk = Split-Path (Split-Path $compiler.Source) }
}
if (-not (Test-Path -LiteralPath (Join-Path $Jdk 'bin\javac.exe'))) { throw 'Pass -Jdk with a Java 21+ JDK directory.' }
$project = $PSScriptRoot
$classes = Join-Path $project 'build\classes'
$testClasses = Join-Path $project 'build\test-classes'
$inputFolder = Join-Path $project 'build\input'
New-Item -ItemType Directory -Path $classes,$testClasses,$inputFolder -Force | Out-Null
$sources = @(Get-ChildItem (Join-Path $project 'src\main\java') -Recurse -Filter *.java | ForEach-Object FullName)
& (Join-Path $Jdk 'bin\javac.exe') --release 21 -encoding UTF-8 -d $classes @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -Path (Join-Path $project 'src\main\resources\*') -Destination $classes -Recurse -Force
& (Join-Path $Jdk 'bin\jar.exe') --create --file (Join-Path $inputFolder 'gitport.jar') --main-class gitport.Main -C $classes .
if ($LASTEXITCODE -ne 0) { throw 'JAR packaging failed.' }
if (Test-Path -LiteralPath (Join-Path $project 'dist\GitPort\app')) {
    Copy-Item -Path (Join-Path $inputFolder 'gitport.jar') -Destination (Join-Path $project 'dist\GitPort\app\gitport.jar') -Force
}
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (Test-Path -LiteralPath $csc) {
    $pickerSource = Join-Path $project 'tools\FolderPicker.cs'
    $pickerManifest = Join-Path $project 'tools\FolderPicker.manifest'
    if (Test-Path -LiteralPath $pickerSource) {
        $pickerExe = Join-Path $project 'dist\GitPort\FolderPicker.exe'
        if (Test-Path -LiteralPath $pickerManifest) {
            & $csc /nologo /target:winexe /win32manifest:$pickerManifest /out:$pickerExe $pickerSource
        } else {
            & $csc /nologo /target:winexe /out:$pickerExe $pickerSource
        }
    }
}
$tests = @(Get-ChildItem (Join-Path $project 'src\test\java') -Recurse -Filter *.java | ForEach-Object FullName)
if ($tests.Count -gt 0) {
    & (Join-Path $Jdk 'bin\javac.exe') --release 21 -encoding UTF-8 -cp $classes -d $testClasses @tests
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $Jdk 'bin\java.exe') -cp "$classes;$testClasses" gitport.CoreTest
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & (Join-Path $Jdk 'bin\java.exe') -cp "$classes;$testClasses" gitport.DesignTest
    if ($LASTEXITCODE -ne 0) { throw 'Design/accessibility tests failed.' }
}
if ($Package) {
    $destination = Join-Path $project 'dist'
    if (Test-Path -LiteralPath (Join-Path $destination 'GitPort')) { throw 'dist\GitPort already exists. Move it aside before packaging; no data will be overwritten.' }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $launcher = Join-Path $project 'build\askpass.properties'
    @('main-jar=gitport.jar','main-class=gitport.Main','win-console=true') | Set-Content -LiteralPath $launcher -Encoding ascii
    # Package in the permitted Windows temp directory, then copy the completed image.
    # jpackage's Windows access probe can misreport a redirected workspace as unwritable.
    $packageTemporary = Join-Path $env:TEMP ('gitport-package-' + [guid]::NewGuid().ToString('N'))
    & (Join-Path $Jdk 'bin\jpackage.exe') --type app-image --name GitPort --app-version 0.2.0 --icon (Join-Path $project 'assets\GitPort.ico') --vendor GitPort --input $inputFolder --main-jar gitport.jar --main-class gitport.Main --dest $packageTemporary --add-modules java.base,java.desktop,java.logging,java.xml,java.naming,java.security.jgss,jdk.crypto.ec --java-options '-Dfile.encoding=UTF-8' --java-options '-Dsun.java2d.uiScale.enabled=true' --java-options '-Dsun.java2d.d3d=true' --java-options '-Dawt.useSystemAAFontSettings=lcd' --java-options '-Dswing.aatext=true' --add-launcher "GitPortAskPass=$launcher"
    if ($LASTEXITCODE -ne 0) { throw 'Application image packaging failed.' }
    Copy-Item -LiteralPath (Join-Path $packageTemporary 'GitPort') -Destination $destination -Recurse
    Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination (Join-Path $destination 'GitPort\README.md')
    if ($GitSource) { & (Join-Path $project 'Bundle-Git.ps1') -Source $GitSource }
    Write-Host "Ready: $destination\GitPort\GitPort.exe"
}
