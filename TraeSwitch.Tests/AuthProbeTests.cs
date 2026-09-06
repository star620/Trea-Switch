using TraeSwitch.Services;

namespace TraeSwitch.Tests;

public class AuthProbeTests
{
    private const string ValidJson =
        """{"iCubeAuthInfo://usertag":"u-token","iCubeAuthInfo://icube.cloudide":"c-token","iCubeServerData://icube.cloudide":"{}"}""";

    [Fact]
    public void 凭据两键齐全_判定已登录()
        => Assert.True(AuthProbe.HasAuthCredsText(ValidJson));

    [Fact]
    public void 缺键_空值_坏JSON_判定未登录()
    {
        Assert.False(AuthProbe.HasAuthCredsText("{}"));
        Assert.False(AuthProbe.HasAuthCredsText(
            """{"iCubeAuthInfo://usertag":"u-token"}"""));                              // 缺 cloudide
        Assert.False(AuthProbe.HasAuthCredsText(
            """{"iCubeAuthInfo://usertag":"","iCubeAuthInfo://icube.cloudide":"c"}""")); // usertag 为空
        Assert.False(AuthProbe.HasAuthCredsText(
            """{"iCubeAuthInfo://usertag":123,"iCubeAuthInfo://icube.cloudide":"c"}""")); // 非字符串
        Assert.False(AuthProbe.HasAuthCredsText("不是 json"));
    }

    [Fact]
    public void 文件不存在返回null_可读有效为true()
    {
        var root = Path.Combine(Path.GetTempPath(), "authprobe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var p = Path.Combine(root, "storage.json");
            Assert.Null(AuthProbe.HasAuthCreds(p)); // 文件不存在
            File.WriteAllText(p, ValidJson);
            Assert.True(AuthProbe.HasAuthCreds(p));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Stamp_变动判定()
    {
        var root = Path.Combine(Path.GetTempPath(), "authprobe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var p = Path.Combine(root, "storage.json");
            File.WriteAllText(p, ValidJson);
            var s1 = AuthProbe.GetStamp(p);
            Assert.False(AuthProbe.StampChanged(s1, AuthProbe.GetStamp(p))); // 未变
            Thread.Sleep(30);
            File.WriteAllText(p, ValidJson + " "); // 改内容（长度变）
            Assert.True(AuthProbe.StampChanged(s1, AuthProbe.GetStamp(p)));  // 变了
            Assert.True(AuthProbe.StampChanged(null, s1));                    // 出现文件
            Assert.False(AuthProbe.StampChanged(null, null));                 // 都不存在
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
