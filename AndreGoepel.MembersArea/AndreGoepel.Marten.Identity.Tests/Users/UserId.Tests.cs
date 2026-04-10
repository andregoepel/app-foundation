using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Tests.Users;

public class UserIdTests
{
    [Fact]
    public void New_ReturnsDistinctIds()
    {
        var id1 = UserId.New();
        var id2 = UserId.New();

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void New_ValueIsNotEmpty()
    {
        var id = UserId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void ParseString_RoundTrips()
    {
        var original = UserId.New();
        var parsed = UserId.Parse(original.ToString());

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void ParseGuid_RoundTrips()
    {
        var guid = Guid.NewGuid();
        var id = UserId.Parse(guid);

        Assert.Equal(guid, id.Value);
    }

    [Fact]
    public void ParseString_InvalidGuid_Throws()
    {
        Assert.Throws<FormatException>(() => UserId.Parse("not-a-guid"));
    }

    [Fact]
    public void ToString_ReturnsGuidString()
    {
        var guid = Guid.NewGuid();
        var id = UserId.Parse(guid);

        Assert.Equal(guid.ToString(), id.ToString());
    }

    [Fact]
    public void ImplicitToGuid_ReturnsValue()
    {
        var guid = Guid.NewGuid();
        var id = UserId.Parse(guid);

        Guid result = id;

        Assert.Equal(guid, result);
    }

    [Fact]
    public void ExplicitFromGuid_ReturnsId()
    {
        var guid = Guid.NewGuid();

        var id = (UserId)guid;

        Assert.Equal(guid, id.Value);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var guid = Guid.NewGuid();
        var id1 = UserId.Parse(guid);
        var id2 = UserId.Parse(guid);

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Equality_DifferentValue_NotEqual()
    {
        var id1 = UserId.New();
        var id2 = UserId.New();

        Assert.NotEqual(id1, id2);
    }
}
