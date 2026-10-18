using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal interface INuGetRehearsalSink
{
    Task<Result> SimulateLoginAsync(CancellationToken ct);
    Task<Result> SimulateUploadAsync(PackageEvidence package, Stream bytes, CancellationToken ct);
}

internal sealed record NuGetRehearsalReceipt(PackageEvidence[] Accepted)
{
    public bool DryRun { get; } = true;
    public bool ReleaseEligible { get; } = false;
}

internal sealed class NuGetPublicationRehearsal(NuGetHandoff handoff, INuGetRehearsalSink sink)
{
    internal async Task<Result<NuGetRehearsalReceipt>> RunAsync(
        NuGetHandoffInput input,
        IReadOnlyCollection<string>? files,
        CancellationToken ct
    )
    {
        var initial = await handoff.VerifyAsync(input, ct);
        if (initial.IsFailure)
            return Result.Fail<NuGetRehearsalReceipt>(initial.Error);

        var inventory = initial.Value!.Packages;
        var selection = files?.ToArray() ?? inventory.Select(p => p.File).ToArray();
        if (
            selection.Length == 0
            || selection.Distinct(StringComparer.Ordinal).Count() != selection.Length
            || selection.Any(file => !inventory.Any(p => p.File == file))
        )
            return Result.Fail<NuGetRehearsalReceipt>(
                "A rehearsal must select unique files from the complete verified batch."
            );

        var snapshots = new List<(PackageEvidence Package, byte[] Bytes)>();
        foreach (
            var package in inventory.Where(p => selection.Contains(p.File, StringComparer.Ordinal))
        )
        {
            var bytes = await File.ReadAllBytesAsync(
                Path.Combine(input.Directory, "packages", package.File),
                ct
            );
            if (SourceFiles.Hash(bytes) != package.Sha256)
                return Result.Fail<NuGetRehearsalReceipt>(
                    "Package bytes changed while taking the rehearsal snapshot."
                );
            snapshots.Add((package, bytes));
        }

        var beforeLogin = await handoff.VerifyAsync(input, ct);
        if (beforeLogin.IsFailure)
            return Result.Fail<NuGetRehearsalReceipt>(beforeLogin.Error);

        ct.ThrowIfCancellationRequested();
        var login = await sink.SimulateLoginAsync(ct);
        if (login.IsFailure)
            return Result.Fail<NuGetRehearsalReceipt>(login.Error);

        foreach (var snapshot in snapshots)
        {
            var beforeUpload = await handoff.VerifyAsync(input, ct);
            if (beforeUpload.IsFailure)
                return Result.Fail<NuGetRehearsalReceipt>(beforeUpload.Error);

            ct.ThrowIfCancellationRequested();
            using var payload = new MemoryStream(snapshot.Bytes, writable: false);
            var upload = await sink.SimulateUploadAsync(snapshot.Package, payload, ct);
            if (upload.IsFailure)
                return Result.Fail<NuGetRehearsalReceipt>(upload.Error);
        }

        return Result.Ok(new NuGetRehearsalReceipt(snapshots.Select(s => s.Package).ToArray()));
    }
}
