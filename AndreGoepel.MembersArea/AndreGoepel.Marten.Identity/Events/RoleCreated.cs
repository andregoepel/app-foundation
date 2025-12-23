namespace AndreGoepel.Marten.Identity.Events;

public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(Guid.NewGuid());
}

public record RoleCreated
{
    public required RoleId RoleId { get; init; }
    public required string RoleName { get; init; }
}
