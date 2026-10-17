#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
project="tools/AndreGoepel.AppFoundation.Sonar/AndreGoepel.AppFoundation.Sonar.csproj"
mkdir -p artifacts/sonar-bootstrap
bootstrap="$(mktemp -d "$root/artifacts/sonar-bootstrap/run-XXXXXXXX")"
trap 'rm -rf -- "$bootstrap"' EXIT
scan_token="${APPFOUNDATION_SONAR_TOKEN:-}"
verify_token="${APPFOUNDATION_SONAR_VERIFY_TOKEN:-}"
github_token="${APPFOUNDATION_GITHUB_TOKEN:-}"
unset APPFOUNDATION_SONAR_TOKEN APPFOUNDATION_SONAR_VERIFY_TOKEN APPFOUNDATION_GITHUB_TOKEN
dotnet restore "$project" --locked-mode
dotnet publish "$project" -c Release --no-restore --no-self-contained -p:SonarQubeTargetsImported=true -o "$bootstrap"
APPFOUNDATION_SONAR_TOKEN="$scan_token" APPFOUNDATION_SONAR_VERIFY_TOKEN="$verify_token" \
    APPFOUNDATION_GITHUB_TOKEN="$github_token" \
    dotnet "$bootstrap/AndreGoepel.AppFoundation.Sonar.dll" --root "$root" "$@"
