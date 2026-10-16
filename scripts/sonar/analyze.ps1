$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $root 'tools/AndreGoepel.AppFoundation.Sonar/AndreGoepel.AppFoundation.Sonar.csproj'
$bootstrapRoot = Join-Path $root 'artifacts/sonar-bootstrap'
$bootstrap = Join-Path $bootstrapRoot ([Guid]::NewGuid().ToString('N'))
$scanToken = $env:APPFOUNDATION_SONAR_TOKEN
$verifyToken = $env:APPFOUNDATION_SONAR_VERIFY_TOKEN
try {
    $env:APPFOUNDATION_SONAR_TOKEN = $null
    $env:APPFOUNDATION_SONAR_VERIFY_TOKEN = $null
    Push-Location $root
    dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish $project -c Release --no-restore --no-self-contained -p:SonarQubeTargetsImported=true -o $bootstrap
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $env:APPFOUNDATION_SONAR_TOKEN = $scanToken
    $env:APPFOUNDATION_SONAR_VERIFY_TOKEN = $verifyToken
    dotnet (Join-Path $bootstrap 'AndreGoepel.AppFoundation.Sonar.dll') --root $root @args
    exit $LASTEXITCODE
} finally {
    $env:APPFOUNDATION_SONAR_TOKEN = $scanToken
    $env:APPFOUNDATION_SONAR_VERIFY_TOKEN = $verifyToken
    Pop-Location
    if ((Test-Path -LiteralPath $bootstrap) -and [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($bootstrap)) -eq $bootstrapRoot) {
        Remove-Item -LiteralPath $bootstrap -Recurse -Force
    }
}
