namespace AndreGoepel.Marten.Identity.Roles;

public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(Guid.NewGuid());

    public static RoleId Parse(string value) => new(Guid.Parse(value));

    public static RoleId Parse(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
