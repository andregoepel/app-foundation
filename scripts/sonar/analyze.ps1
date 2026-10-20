$ErrorActionPreference = 'Stop'
if ($args -contains '--nuget-dry-run' -or $args -contains '--nuget-prepare' -or $args -contains '--nuget-rehearse' -or $args -contains '--nuget-verify-handoff' -or $args -contains '--nuget-verify-intent' -or $args -contains '--nuget-verify-acceptance-intent' -or $args -contains '--nuget-inspect-acceptance' -or $args -contains '--nuget-inspect-publication' -or $args -contains '--staging-contract') {
    foreach ($name in @('ACTIONS_ID_TOKEN_REQUEST_TOKEN', 'ACTIONS_ID_TOKEN_REQUEST_URL', 'NUGET_API_KEY', 'NUGET_AUTH_TOKEN', 'NUGET_SYMBOL_API_KEY')) {
        if (-not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($name))) {
            throw 'Run preparation without OIDC access or NuGet publication credentials.'
        }
    }
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $root 'tools/AndreGoepel.AppFoundation.Sonar/AndreGoepel.AppFoundation.Sonar.csproj'
$bootstrapRoot = Join-Path $root 'artifacts/sonar-bootstrap'
$bootstrap = Join-Path $bootstrapRoot ([Guid]::NewGuid().ToString('N'))
$scanToken = $env:APPFOUNDATION_SONAR_TOKEN
$verifyToken = $env:APPFOUNDATION_SONAR_VERIFY_TOKEN
$githubToken = $env:APPFOUNDATION_GITHUB_TOKEN
try {
    $env:APPFOUNDATION_SONAR_TOKEN = $null
    $env:APPFOUNDATION_SONAR_VERIFY_TOKEN = $null
    $env:APPFOUNDATION_GITHUB_TOKEN = $null
    Push-Location $root
    dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish $project -c Release --no-restore --no-self-contained -p:SonarQubeTargetsImported=true -o $bootstrap
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $env:APPFOUNDATION_SONAR_TOKEN = $scanToken
    $env:APPFOUNDATION_SONAR_VERIFY_TOKEN = $verifyToken
    $env:APPFOUNDATION_GITHUB_TOKEN = $githubToken
    dotnet (Join-Path $bootstrap 'AndreGoepel.AppFoundation.Sonar.dll') --root $root @args
    exit $LASTEXITCODE
} finally {
    $env:APPFOUNDATION_SONAR_TOKEN = $scanToken
    $env:APPFOUNDATION_SONAR_VERIFY_TOKEN = $verifyToken
    $env:APPFOUNDATION_GITHUB_TOKEN = $githubToken
    Pop-Location
    if ((Test-Path -LiteralPath $bootstrap) -and [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($bootstrap)) -eq $bootstrapRoot) {
        Remove-Item -LiteralPath $bootstrap -Recurse -Force
    }
}
