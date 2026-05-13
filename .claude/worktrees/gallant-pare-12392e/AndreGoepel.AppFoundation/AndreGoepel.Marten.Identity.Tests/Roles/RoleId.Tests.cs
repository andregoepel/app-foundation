using AndreGoepel.Marten.Identity.Roles;

namespace AndreGoepel.Marten.Identity.Tests.Roles;

public class RoleIdTests
{
    [Fact]
    public void New_ReturnsDistinctIds()
    {
        // Act
        var id1 = RoleId.New();
        var id2 = RoleId.New();

        // Assert
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void New_ValueIsNotEmpty()
    {
        // Act
        var id = RoleId.New();

        // Assert
        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void ParseString_RoundTrips()
    {
        // Arrange
        var original = RoleId.New();

        // Act
        var parsed = RoleId.Parse(original.ToString());

        // Assert
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void ParseGuid_RoundTrips()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var id = RoleId.Parse(guid);

        // Assert
        Assert.Equal(guid, id.Value);
    }

    [Fact]
    public void ParseString_InvalidGuid_Throws()
    {
        // Act / Assert
        Assert.Throws<FormatException>(() => RoleId.Parse("not-a-guid"));
    }

    [Fact]
    public void ToString_ReturnsGuidString()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var id = RoleId.Parse(guid);

        // Assert
        Assert.Equal(guid.ToString(), id.ToString());
    }

    [Fact]
    public void ImplicitToGuid_ReturnsValue()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var id = RoleId.Parse(guid);

        // Act
        Guid result = id;

        // Assert
        Assert.Equal(guid, result);
    }

    [Fact]
    public void ExplicitFromGuid_ReturnsId()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var id = (RoleId)guid;

        // Assert
        Assert.Equal(guid, id.Value);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var id1 = RoleId.Parse(guid);
        var id2 = RoleId.Parse(guid);

        // Assert
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Equality_DifferentValue_NotEqual()
    {
        // Act
        var id1 = RoleId.New();
        var id2 = RoleId.New();

        // Assert
        Assert.NotEqual(id1, id2);
    }
}
