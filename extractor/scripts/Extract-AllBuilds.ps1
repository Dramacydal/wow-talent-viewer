<#
.SYNOPSIS
  Runs extract-build for every WoW client under -ClientsRoot, logging each build's output
  to its own file instead of the console — so results can be reviewed (by a human or by
  Claude reading the same files off the shared E:\ drive) without pasting console output
  around. See .claude-docs/gotchas.md for why a couple of the earliest alpha clients are
  skipped (no Talent.dbc at all — talents were still a trainer-based mechanic then).

.EXAMPLE
  .\Extract-AllBuilds.ps1
  .\Extract-AllBuilds.ps1 -ClientsRoot E:\wow_data -RepoRoot E:\git\wow-talents-viewer
#>
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path,
    [string]$ClientsRoot = "E:\wow_data"
)

$ErrorActionPreference = "Continue"

$envFile  = Join-Path $RepoRoot "web\.env.local"
$iconsDir = Join-Path $RepoRoot "storage\icons"
$logsDir  = Join-Path $RepoRoot ".tmp\extract-logs"
$cliProj  = Join-Path $RepoRoot "extractor\Extractor.Cli"

New-Item -ItemType Directory -Force -Path $logsDir | Out-Null

# No Talent.dbc at all on these — talents were a trainer-NPC mechanic pre-0.7.0, nothing to
# extract (see gotchas.md). Extending this list is safe if more such clients show up later.
$excluded = @("0.5.3.3368", "0.5.5.3494")

Write-Host "Building extractor once..."
dotnet build $cliProj
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed, aborting." -ForegroundColor Red
    exit 1
}

$clients = Get-ChildItem $ClientsRoot -Directory |
    Where-Object { $_.Name -match '^WoW (.+)$' } |
    ForEach-Object { [PSCustomObject]@{ Build = $Matches[1]; Path = $_.FullName } } |
    Sort-Object { [version]$_.Build }

$summary = @()

foreach ($client in $clients) {
    if ($excluded -contains $client.Build) {
        Write-Host "Skip $($client.Build) (no Talent.dbc)"
        continue
    }

    $logFile = Join-Path $logsDir "$($client.Build).log"
    Write-Host "=== $($client.Build) -> $logFile ==="

    $args = @("run", "--project", $cliProj, "--no-build", "--",
              "extract-build", $client.Path, $client.Build, $envFile, $iconsDir)
    & dotnet @args *>&1 | Tee-Object -FilePath $logFile

    $summary += [PSCustomObject]@{ Build = $client.Build; ExitCode = $LASTEXITCODE; Log = $logFile }
}

Write-Host "`n=== Summary ==="
$summary | Format-Table -AutoSize
$failed = $summary | Where-Object { $_.ExitCode -ne 0 }
if ($failed) {
    Write-Host "$($failed.Count) build(s) failed — see their .log files above." -ForegroundColor Yellow
} else {
    Write-Host "All builds extracted successfully." -ForegroundColor Green
}
