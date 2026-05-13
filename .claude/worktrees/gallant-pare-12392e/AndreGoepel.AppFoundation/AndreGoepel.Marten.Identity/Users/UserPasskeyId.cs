namespace AndreGoepel.Marten.Identity.Users;

public readonly record struct UserPasskeyId(Guid Value)
{
    public static UserPasskeyId New() => new(Guid.NewGuid());

    public static UserPasskeyId Parse(string value) => new(Guid.Parse(value));
}
