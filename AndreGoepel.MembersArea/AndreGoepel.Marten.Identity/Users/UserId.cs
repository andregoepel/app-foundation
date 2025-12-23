namespace AndreGoepel.Marten.Identity.Users;

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());

    public static UserId Parse(string value) => new(Guid.Parse(value));
}
