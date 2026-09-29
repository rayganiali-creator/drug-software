using MedSmarter.BuildingBlocks;

namespace MedSmarter.BuildingBlocks.Tests;

public class ResultTests
{
    [Fact]
    public void Success_has_no_error()
    {
        var r = Result.Success(42);
        Assert.True(r.IsSuccess);
        Assert.False(r.IsFailure);
        Assert.Equal(42, r.Value);
        Assert.Equal(ResultError.None, r.Error);
    }

    [Fact]
    public void Failure_carries_error_and_value_access_throws()
    {
        var err = new ResultError("x.y", "boom");
        var r = Result.Failure<int>(err);
        Assert.True(r.IsFailure);
        Assert.Equal(err, r.Error);
        Assert.Throws<InvalidOperationException>(() => r.Value);
    }

    [Fact]
    public void Failure_without_error_is_rejected()
        => Assert.ThrowsAny<Exception>(() => Result.Failure(ResultError.None));

    [Fact]
    public void SystemClock_returns_utc()
        => Assert.Equal(TimeSpan.Zero, new SystemClock().UtcNow.Offset);
}
