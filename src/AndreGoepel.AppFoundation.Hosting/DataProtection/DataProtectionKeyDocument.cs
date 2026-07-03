namespace AndreGoepel.AppFoundation.Hosting.DataProtection;

/// <summary>
/// Marten document holding one DataProtection key ring entry. The key ring must
/// survive container rebuilds — payloads encrypted via <c>IDataProtector</c> are
/// unrecoverable without it — so it is persisted in Postgres alongside the rest
/// of the application data.
/// </summary>
/// <remarks>
/// The type name is part of the storage contract: Marten derives the table name
/// from it (<c>mt_doc_dataprotectionkeydocument</c>), and hosts that persisted
/// keys with an identically-shaped document before this type existed (e.g.
/// finance-app) keep their key ring on upgrade. Renaming this type or its
/// properties requires a data migration.
/// </remarks>
public sealed class DataProtectionKeyDocument
{
    public required string Id { get; init; }

    public required string Xml { get; init; }
}
