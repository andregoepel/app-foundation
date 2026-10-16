$ErrorActionPreference = 'Stop'
& python (Join-Path $PSScriptRoot 'analyze.py') @args
exit $LASTEXITCODE
