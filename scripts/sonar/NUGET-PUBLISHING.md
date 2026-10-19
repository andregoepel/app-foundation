# Official NuGet publishing integration (#244)

Status: reviewed design and inactive examples. This document does not register
a workflow or provide a publishing entry point. Existing Actions and the
non-publishing commands remain unchanged. Production activation needs a separate
reviewed PR; #244 remains open.

## Use the official tools

Use the official `NuGet/login` GitHub Action for trusted publishing and the SDK's
`dotnet nuget push` command for uploading prepared files. There is no application
upload adapter, credential wrapper, HTTP transport or direct `NuGet.Protocol`
dependency. Sonar and GitHub verification remain the responsibility of the
existing readiness/provenance/handoff code.

The current shared publishing action already uses official NuGet tooling, but
checks out and packs again. The eventual repository-local publishing job must
consume the exact verified package batch instead. Repacking after verification
would require validating that new batch; it cannot inherit the earlier hashes.

## Required order in the future publishing job

1. Restrict publication to an explicitly created `vX.Y.Z` tag in this repository.
   Reject the acceptance namespace, retargeted tags and commits other than the
   current main tip. Main success alone must never publish a release.
2. Bootstrap the reviewed verifier before credentials are acquired. Authenticate
   the preparation run/attempt and artifact origin, resolve the original tag
   object, and verify the complete twelve-file batch plus fresh main CI/E2E/Sonar
   evidence. Validate the versioned build before packing in the preparation job.
3. Keep the transferred files in a private, isolated runner directory. Do not
   execute artifact contents or rebuild/repack them in the credential-bearing job.
4. Complete independent verification immediately before `NuGet/login`. Only then
   invoke the pinned login action in the repository-local job allowed by the
   account's trusted-publisher policy.
5. Before each individual package or symbol command, repeat provenance, tag,
   readiness and whole-batch verification, then check that file's hash immediately
   before invoking the CLI. A symbol-only selection still requires all twelve
   original files to verify. Prevent concurrent mutation of the batch.
6. Record the attempted filename, package/version, hash, producer/run/attempt and
   CLI outcome in trusted storage. Stop on the first failure or cancellation;
   reconcile partial or uncertain publication before an explicitly selected retry.

These distributed checks are not an atomic lock on GitHub, Sonar or the file
system. The activation review must establish the trusted runner and file ownership
boundary; an earlier dry-run receipt alone never authorizes a later upload.

## Inactive step examples

The following are separate step examples, not a complete runnable workflow. They
deliberately use `if: ${{ false }}`. They contain no verification implementation:
the ordered checks above must be wired and tested before enabling any step. Do not
copy them into `.github/workflows` as an activation shortcut.

The login action requires `id-token: write` in the future repository-local job
and a matching NuGet trusted-publisher policy. Its default NuGet.org token service
and audience stay fixed. The pin below is the same revision already used by the
existing publisher; re-review its implementation and provenance at activation.

```yaml
- name: Obtain temporary NuGet credentials after verified handoff
  if: ${{ false }}
  id: nuget-login
  uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841
  with:
    user: ${{ secrets.NUGET_USER }}
```

For the following examples, `VERIFIED_PACKAGE_FILE` or `VERIFIED_SYMBOL_FILE`
must be the absolute path of exactly one approved file, obtained from independently
verified inventory. `PUSH_CONFIG` must name a job-owned configuration created
before login, containing only `<configuration />`. It contains no credentials or
alternate feeds. Paths must not come directly from dispatch input or unverified
artifact JSON. Revalidate the complete batch after login and immediately before
each step; the snippets do not perform those checks themselves.

```yaml
- name: Push one reverified package
  if: ${{ false }}
  shell: bash
  env:
    NUGET_API_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}
  run: |
    set -euo pipefail
    : "${NUGET_API_KEY:?}" "${VERIFIED_PACKAGE_FILE:?}" "${PUSH_CONFIG:?}"
    dotnet nuget push "$VERIFIED_PACKAGE_FILE" \
      --source https://api.nuget.org/v3/index.json \
      --configfile "$PUSH_CONFIG" \
      --no-symbols --timeout 300
```

Each `.snupkg` gets its own verification and command. Suppressing automatic symbol
upload in the package command prevents a second file from being sent before its
own verification boundary.

```yaml
- name: Push one reverified symbol package
  if: ${{ false }}
  shell: bash
  env:
    NUGET_API_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}
    NUGET_SYMBOL_API_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}
  run: |
    set -euo pipefail
    : "${NUGET_API_KEY:?}" "${NUGET_SYMBOL_API_KEY:?}" "${VERIFIED_SYMBOL_FILE:?}" "${PUSH_CONFIG:?}"
    dotnet nuget push "$VERIFIED_SYMBOL_FILE" \
      --source https://api.nuget.org/v3/index.json \
      --symbol-source https://www.nuget.org/api/v2/symbolpackage \
      --configfile "$PUSH_CONFIG" \
      --no-service-endpoint --timeout 300
```

The SDK is pinned by `global.json`. Verify its bundled NuGet CLI supports the
environment-based API keys (NuGet 7.6 or later) and `--configfile` before activation.
Use step-scoped credentials, no command-line key, shell tracing, job-wide key
export, persisted key configuration or credential-containing artifacts. Do not
restore/build after login. The runner remains a trusted process boundary.

## Verification and credential separation still need wiring

The existing `--nuget-rehearse` command is deliberately credential-free and always
uses `NuGetDiscardSink`; it is not a credential-bearing publishing command. Both
bootstraps reject OIDC request variables and NuGet credentials before restore/build,
and `ProcessRunner` removes them from child environments. Keep those protections.

A job with OIDC permission has request variables even before calling `NuGet/login`.
The activation PR must therefore define a dedicated verification step/process
environment without publication credentials, using the verifier bootstrapped before
login and read-only service credentials. After login, keep the NuGet key out of
that verifier and its children; expose it only to the official push step. Do not
simply call the current bootstrap in a credential-bearing environment or remove
its credential rejection to make the workflow pass.

The existing authenticated producer currently accepts only test tags. Production
tag triggering, the reviewed producer hash and the repository-local publishing job
must be updated together in the separately approved activation change. Rehearsal
receipts retain `dryRun: true` and `releaseEligible: false`.

## Failure and retry semantics

One CLI invocation is not a guarantee of one HTTP request: NuGet owns its internal
retry and transport behavior. No custom transport is retained to override it.
Do not claim the removed adapter's single-request guarantee for this design.

Invoke one explicit file at a time, without wildcards, `--skip-duplicate`, an outer
retry loop, `continue-on-error`, insecure endpoints or interactive authentication.
A nonzero exit, timeout, cancellation or interrupted runner must stop remaining
files. It does not prove that the server rejected the upload; a lost response can
follow acceptance. A zero exit is also not proof of completed package indexing.

Before retrying, reconcile the actual package/symbol state and the exact intended
bytes against the trusted attempt record. A 409 is not automatic success. Do not
overwrite a published version, repack the same version, refresh a stale receipt or
reuse an earlier green result as authorization. Reruns and symbol-only retries need
fresh verification and a newly approved selection. Durable outcome records and
account-level bypass controls remain acceptance requirements for #244.

## Validation boundary

The existing synthetic tests still cover readiness/provenance rejection, complete
batch verification, changes around simulated login/uploads, partial and symbol-only
retries, and uncertain outcomes. The live discard-sink drills remain documented in
[NUGET-INTEGRATION.md](NUGET-INTEGRATION.md#completed-intended-runner-acceptance).
Neither proves this future credential-bearing workflow is active or accepted.

The adapter-specific tests and dependency were removed with the adapter. Do not
retain their HTTP response classifications or test count as evidence for the CLI.
Before activation, test the actual workflow ordering and failure handling against a
non-publishing substitute, then review account-level trusted-publisher/API-key
controls for all six packages. A real release remains separately authorized.

References: [official NuGet/login action](https://github.com/NuGet/login/tree/8d196754b4036150537f80ac539e15c2f1028841),
[trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing),
[dotnet nuget push](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push),
[NuGet credential environment variables](https://learn.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-environment-variables).
