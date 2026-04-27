namespace Den.RulesEngine;

public interface IRulesEngineObserver
{
    void Record(EngineTraceRecord record);
}

public sealed class NoOpRulesEngineObserver : IRulesEngineObserver
{
    public void Record(EngineTraceRecord record)
    {
    }
}

public sealed record EngineTraceRecord(
    GameInstanceId GameInstanceId,
    GameModuleId ModuleId,
    GameModuleVersion ModuleVersion,
    long SequenceBeforeResolution,
    string PayloadType,
    RulesResolutionPhase Phase,
    string HandlerName,
    int Priority,
    string Outcome,
    string? ReasonCode = null);
