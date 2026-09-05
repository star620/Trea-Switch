using System.Drawing;
using TraeSwitch;

namespace TraeSwitch.Tests;

public class SmokeTests
{
    [Fact]
    public void 样式常量非空()
    {
        Assert.Equal(Color.FromArgb(0x1E, 0x29, 0x3B), AppStyle.SideBack);
    }
}
