param([string]$RepoPath)
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
if ($RepoPath) {
    dotnet run --project "$project\src-cs\GitPort\GitPort.csproj" -- "$RepoPath"
} else {
    dotnet run --project "$project\src-cs\GitPort\GitPort.csproj"
}
