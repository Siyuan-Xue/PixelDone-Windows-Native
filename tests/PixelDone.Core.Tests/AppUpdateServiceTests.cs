using PixelDone.Core;
using PixelDone.Infrastructure;

namespace PixelDone.Core.Tests;

[TestClass]
public sealed class AppUpdateServiceTests
{
    [TestMethod]
    public void SelectUsesNewestReleaseWithWindowsInstaller()
    {
        var releases = new[]
        {
            new ReleaseDocument(
                "v4.1.0-beta.1",
                "https://example.test/v4.1.0-beta.1",
                false,
                true,
                [new ReleaseAsset(
                    "PixelDone-4.1.0-beta.1-win-x64-setup.exe",
                    "https://example.test/setup.exe")]),
            new ReleaseDocument(
                "v5.0.0-beta.1",
                "https://example.test/v5.0.0-beta.1",
                false,
                true,
                [new ReleaseAsset("PixelDone-5.0.0.apk", "https://example.test/app.apk")]),
        };

        var result = AppUpdateService.Select(
            releases,
            new ProductVersion(4, 0, 0, 1));

        Assert.AreEqual(UpdateState.Available, result.State);
        Assert.AreEqual("4.1.0-beta.1", result.Version);
        Assert.AreEqual("https://example.test/setup.exe", result.Download?.ToString());
    }
}
