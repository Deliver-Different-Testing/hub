using Hub.Features.AiAssistant;
using Microsoft.Extensions.Logging;

namespace Hub.Tests.Features.AiAssistant;

public class CspKnowledgeCircuitTests
{
    private readonly FakeTime _time = new();
    private readonly CapturingLogger<CspKnowledgeCircuit> _logger = new();

    private CspKnowledgeCircuit Create() => new(_time, _logger);

    [Fact]
    public void PermanentFailure_OpensForFiveMinutes()
    {
        var circuit = Create();

        circuit.RecordFailure("404", permanent: true);

        Assert.True(circuit.IsOpen);
        _time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        Assert.True(circuit.IsOpen);
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(circuit.IsOpen);
    }

    [Fact]
    public void ThreeTransientFailures_OpenForThirtySeconds()
    {
        var circuit = Create();

        circuit.RecordFailure("500", permanent: false);
        circuit.RecordFailure("500", permanent: false);
        Assert.False(circuit.IsOpen);
        circuit.RecordFailure("500", permanent: false);

        Assert.True(circuit.IsOpen);
        _time.Advance(TimeSpan.FromSeconds(31));
        Assert.False(circuit.IsOpen);
    }

    [Fact]
    public void Success_ResetsTheTransientCount()
    {
        var circuit = Create();
        circuit.RecordFailure("500", permanent: false);
        circuit.RecordFailure("500", permanent: false);

        circuit.RecordSuccess();
        circuit.RecordFailure("500", permanent: false);

        Assert.False(circuit.IsOpen);
    }

    [Fact]
    public void LogsOneWarning_PerOpening()
    {
        var circuit = Create();

        circuit.RecordFailure("500", permanent: false);
        circuit.RecordFailure("500", permanent: false);
        Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Warning);

        circuit.RecordFailure("500", permanent: false);
        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);

        _time.Advance(TimeSpan.FromSeconds(31));
        circuit.RecordFailure("500", permanent: false);
        Assert.Equal(2, _logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }
}
