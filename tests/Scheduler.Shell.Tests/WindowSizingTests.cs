using Scheduler.Shell;

namespace Scheduler.Shell.Tests;

public class WindowSizingTests
{
    [Fact]
    public void Fits_in_work_area_is_unchanged()
    {
        Assert.Equal((1280d, 800d), WindowSizing.Clamp(1280, 800, 1920, 1040, 700, 420));
    }

    [Fact]
    public void Clamped_to_work_area_on_1366x768()
    {
        Assert.Equal((1280d, 720d), WindowSizing.Clamp(1280, 800, 1366, 720, 700, 420));
    }

    [Fact]
    public void Small_work_area_never_goes_below_minimum()
    {
        Assert.Equal((700d, 420d), WindowSizing.Clamp(1280, 800, 600, 400, 700, 420));
    }
}
