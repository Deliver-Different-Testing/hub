using Hub.ViewModels;

namespace Hub.Tests.ViewModels;

public class ErrorViewModelTests
{
    [Fact]
    public void ShowRequestId_WhenSet_ReturnsTrue()
    {
        var model = new ErrorViewModel { RequestId = "abc123" };

        Assert.True(model.ShowRequestId);
    }

    [Fact]
    public void ShowRequestId_WhenNull_ReturnsFalse()
    {
        var model = new ErrorViewModel { RequestId = null! };

        Assert.False(model.ShowRequestId);
    }

    [Fact]
    public void ShowRequestId_WhenEmpty_ReturnsFalse()
    {
        var model = new ErrorViewModel { RequestId = string.Empty };

        Assert.False(model.ShowRequestId);
    }
}
