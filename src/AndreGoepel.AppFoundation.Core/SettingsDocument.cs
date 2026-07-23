namespace AndreGoepel.AppFoundation;

/// <summary>
/// Shared base for small, singleton admin-configured settings records (<see cref="MailService.EmailSettingsDocument"/>
/// and, in consuming apps, their own settings types). Register subclasses with Marten's document
/// hierarchy support (<c>Schema.For&lt;SettingsDocument&gt;().AddSubClass&lt;T&gt;()</c>) so every
/// settings type shares one physical table instead of each getting a one-row table of its own.
/// </summary>
public abstract class SettingsDocument
{
    public required string Id { get; init; }
}
