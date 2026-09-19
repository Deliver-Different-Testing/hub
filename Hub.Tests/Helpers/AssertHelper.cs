using System.Text.Json;

namespace Hub.Tests.Helpers;

internal static class AssertHelper
{
    internal static void JsonEquivalent(object? expected, object? actual)
    {
        var expectedJson = JsonSerializer.Serialize(expected);
        var actualJson = JsonSerializer.Serialize(actual);
        Assert.Equal(expectedJson, actualJson);
    }
}
