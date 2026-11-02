namespace Hub.Features.AiAssistant;

public sealed class CspKnowledgeCircuit(TimeProvider time, ILogger<CspKnowledgeCircuit> logger)
{
    internal const int TransientFailureThreshold = 3;
    internal static readonly TimeSpan TransientBreak = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan PermanentBreak = TimeSpan.FromMinutes(5);

    private readonly Lock _gate = new();
    private int _consecutiveFailures;
    private DateTimeOffset _openUntil = DateTimeOffset.MinValue;

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return time.GetUtcNow() < _openUntil;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _openUntil = DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// A failure that will not clear by retrying (no API at that address, bad key) opens the circuit at once;
    /// a transient one opens it after repeated failures, and a probe that fails after the break reopens it.
    /// The warning is logged when the circuit opens, so a dead CSP costs one line per break rather than one per message.
    /// </summary>
    public void RecordFailure(string reason, bool permanent)
    {
        TimeSpan? opened = null;
        lock (_gate)
        {
            _consecutiveFailures++;
            if (permanent || _consecutiveFailures >= TransientFailureThreshold)
            {
                opened = permanent ? PermanentBreak : TransientBreak;
                _openUntil = time.GetUtcNow() + opened.Value;
            }
        }

        if (opened is { } span)
        {
            logger.LogWarning("CSP knowledge base unavailable ({Reason}); skipping it for {Seconds}s", reason, (int)span.TotalSeconds);
        }
        else
        {
            logger.LogDebug("CSP knowledge base call failed ({Reason})", reason);
        }
    }
}
