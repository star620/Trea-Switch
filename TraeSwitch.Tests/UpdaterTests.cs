using TraeSwitch.Services;

namespace TraeSwitch.Tests;

public class UpdaterTests
{
    [Theory]
    [InlineData("v1.0.0", 1, 0, 0, 0)]
    [InlineData("1.2.3", 1, 2, 3, 0)]
    [InlineData("v0.9", 0, 9, 0, 0)]
    [InlineData("v1.0.1-beta", 1, 0, 1, 0)]
    [InlineData("V2.3.4.5", 2, 3, 4, 5)]
    public void TryParseTag_合法标签解析为版本号(string tag, int a, int b, int c, int d)
    {
        Assert.True(UpdaterService.TryParseTag(tag, out var v));
        Assert.Equal(new Version(a, b, c, d), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("v")]
    [InlineData("1.2.x")]
    public void TryParseTag_非法标签返回false(string tag)
        => Assert.False(UpdaterService.TryParseTag(tag, out _));
}