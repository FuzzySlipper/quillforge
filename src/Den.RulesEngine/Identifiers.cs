namespace Den.RulesEngine;

public readonly record struct GameInstanceId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct GameModuleId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct GameModuleVersion(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct GameTemplateVersion(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ParticipantId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ParticipantSetId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct GameStageId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct PendingInputId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct GameEventId(Guid Value)
{
    public static GameEventId NewId() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct GameIntentCommandId(Guid Value)
{
    public static GameIntentCommandId NewId() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
