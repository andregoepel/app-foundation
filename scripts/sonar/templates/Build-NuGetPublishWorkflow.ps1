param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
)

$ErrorActionPreference = 'Stop'
$templates = Join-Path $RepositoryRoot 'scripts/sonar/templates'
$caller = [IO.File]::ReadAllText((Join-Path $templates 'nuget-publish.caller.yml')).Replace("`r`n", "`n")
$steps = [IO.File]::ReadAllText((Join-Path $templates 'nuget-publish.steps.yml')).Replace("`r`n", "`n")
$marker = '      # INSERT DISABLED PUBLICATION STEPS'
if (($caller.Split($marker).Count - 1) -ne 1 -or -not $steps.Contains("`nsteps:`n")) {
    throw 'Expected one caller insertion marker and one steps fragment.'
}

$body = $steps.Substring($steps.IndexOf("`nsteps:`n", [StringComparison]::Ordinal) + 8).TrimEnd("`n")
$indented = ($body.Split("`n") | ForEach-Object { if ($_.Length -gt 0) { '    ' + $_ } else { '' } }) -join "`n"
$rendered = $caller.Replace($marker, $indented)
$guards = @($rendered.Split("`n") | Where-Object { $_ -match '^\s+if:' })
if ($guards.Count -ne 16 -or @($guards | Where-Object { $_ -notmatch '^\s+if: \$\{\{ false && ' }).Count -ne 0) {
    throw 'The job and all fifteen steps must remain disabled.'
}

$outputDirectory = Join-Path $RepositoryRoot 'artifacts/sonar/publish-workflow-template'
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$output = Join-Path $outputDirectory 'nuget-publish.yml'
[IO.File]::WriteAllText($output, $rendered, [Text.UTF8Encoding]::new($false))
Write-Output $output
