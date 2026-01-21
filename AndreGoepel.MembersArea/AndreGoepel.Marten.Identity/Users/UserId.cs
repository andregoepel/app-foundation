using static System.Runtime.InteropServices.JavaScript.JSType;

namespace AndreGoepel.Marten.Identity.Users;

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());

    public static UserId Parse(string value) => new(Guid.Parse(value));

    public static UserId Parse(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(UserId userId) => userId.Value;

    public static explicit operator UserId(Guid guid) => Parse(guid);
}
