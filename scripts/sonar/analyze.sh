#!/usr/bin/env bash
set -euo pipefail
for argument in "$@"; do
    if [[ "$argument" == --nuget-dry-run || "$argument" == --nuget-prepare || "$argument" == --nuget-rehearse || "$argument" == --nuget-verify-handoff || "$argument" == --nuget-verify-intent || "$argument" == --nuget-inspect-publication || "$argument" == --staging-contract ]]; then
        for name in ACTIONS_ID_TOKEN_REQUEST_TOKEN ACTIONS_ID_TOKEN_REQUEST_URL NUGET_API_KEY NUGET_AUTH_TOKEN NUGET_SYMBOL_API_KEY; do
            if [[ -n "${!name:-}" ]]; then
                echo 'Run preparation without OIDC access or NuGet publication credentials.' >&2
                exit 1
            fi
        done
    fi
done
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
