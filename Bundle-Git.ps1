param([Parameter(Mandatory=$true)][string]$Source,[string]$Destination=(Join-Path $PSScriptRoot 'dist\GitPort\git'))
$ErrorActionPreference='Stop'
$sourceRoot=(Resolve-Path -LiteralPath $Source).Path
if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot 'cmd\git.exe'))) { throw 'Source must be a complete Git for Windows distribution.' }
if (Test-Path -LiteralPath $Destination) { throw 'Bundled Git destination already exists; nothing overwritten.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
foreach($name in @('bin','cmd','dev','etc','mingw64','ucrt64','usr')) {
    $item=Join-Path $sourceRoot $name
    if(Test-Path -LiteralPath $item){
        if($name -eq 'etc'){
            $etc=Join-Path $Destination 'etc';New-Item -ItemType Directory -Path $etc -Force | Out-Null
            Get-ChildItem -LiteralPath $item -Force | Where-Object Name -ne 'gitconfig' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $etc -Recurse }
            '# GitPort uses a private Git config for each profile.' | Set-Content -LiteralPath (Join-Path $etc 'gitconfig') -Encoding ascii
        } else { Copy-Item -LiteralPath $item -Destination $Destination -Recurse }
    }
}
foreach($name in @('LICENSE.txt','ReleaseNotes.html','git-bash.exe','git-cmd.exe')){
    $item=Join-Path $sourceRoot $name;if(Test-Path -LiteralPath $item){Copy-Item -LiteralPath $item -Destination $Destination}
}
& (Join-Path $Destination 'cmd\git.exe') --version
if($LASTEXITCODE -ne 0){throw 'Bundled Git failed its startup check.'}
Write-Host 'Complete Git runtime bundled. Host temp data, uninstaller and system Git config were excluded.'
