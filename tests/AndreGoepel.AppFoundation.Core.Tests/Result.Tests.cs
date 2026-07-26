namespace AndreGoepel.AppFoundation.Core.Tests;

public class ResultTests
{
    [Fact]
    public void Ok_Called_ReturnsSuccessWithNoError()
    {
        // Act
        var result = Result.Ok();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_Called_ReturnsFailureWithError()
    {
        // Act
        var result = Result.Fail("something went wrong");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("something went wrong", result.Error);
    }

    [Fact]
    public void OkOfT_Called_ReturnsSuccessWithValue()
    {
        // Act
        var result = Result.Ok(42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void FailOfT_Called_ReturnsFailureWithNoValue()
    {
        // Act
        var result = Result.Fail<int>("invalid input");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(default, result.Value);
        Assert.Equal("invalid input", result.Error);
    }
}
