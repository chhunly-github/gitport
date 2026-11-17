param([string]$Jdk = $env:JAVA_HOME)
$ErrorActionPreference='Stop'
if (-not $Jdk) { throw 'Pass -Jdk with your Java 21+ JDK directory.' }
& (Join-Path $Jdk 'bin\java.exe') "-Dgitport.home=$PSScriptRoot\dev-workspace" -jar (Join-Path $PSScriptRoot 'build\input\gitport.jar')
