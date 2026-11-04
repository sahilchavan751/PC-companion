using OmniControl.Infrastructure.AudioDisplay;

namespace OmniControl.Tests;

public class UnitTest1
{
    [Fact]
    public async Task TestBrightness()
    {
        var svc = new DisplayControlService();
        var monitors = await svc.GetMonitorsAsync();
        Assert.NotEmpty(monitors);
        var res = await svc.SetBrightnessAsync(monitors[0].DeviceId, 45);
        Assert.True(res.IsSuccess, res.Message);
    }
}
