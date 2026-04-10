using AndreGoepel.Marten.Identity.Roles;

namespace AndreGoepel.Marten.Identity.Tests.Roles;

public class RoleIdTests
{
    [Fact]
    public void New_ReturnsDistinctIds()
    {
        var id1 = RoleId.New();
        var id2 = RoleId.New();

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void New_ValueIsNotEmpty()
    {
        var id = RoleId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void ParseString_RoundTrips()
    {
        var original = RoleId.New();
        var parsed = RoleId.Parse(original.ToString());

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void ParseGuid_RoundTrips()
    {
        var guid = Guid.NewGuid();
        var id = RoleId.Parse(guid);

        Assert.Equal(guid, id.Value);
    }

    [Fact]
    public void ParseString_InvalidGuid_Throws()
    {
        Assert.Throws<FormatException>(() => RoleId.Parse("not-a-guid"));
    }

    [Fact]
    public void ToString_ReturnsGuidString()
    {
        var guid = Guid.NewGuid();
        var id = RoleId.Parse(guid);

        Assert.Equal(guid.ToString(), id.ToString());
    }

    [Fact]
    public void ImplicitToGuid_ReturnsValue()
    {
        var guid = Guid.NewGuid();
        var id = RoleId.Parse(guid);

        Guid result = id;

        Assert.Equal(guid, result);
    }

    [Fact]
    public void ExplicitFromGuid_ReturnsId()
    {
        var guid = Guid.NewGuid();

        var id = (RoleId)guid;

        Assert.Equal(guid, id.Value);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var guid = Guid.NewGuid();
        var id1 = RoleId.Parse(guid);
        var id2 = RoleId.Parse(guid);

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Equality_DifferentValue_NotEqual()
    {
        var id1 = RoleId.New();
        var id2 = RoleId.New();

        Assert.NotEqual(id1, id2);
    }
}
