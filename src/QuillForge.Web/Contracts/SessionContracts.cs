namespace QuillForge.Web.Contracts;

public sealed record SessionCreatedResponse
{
    public required Guid SessionId { get; init; }
    public required string Name { get; init; }
}

public sealed record SessionLoadResponse
{
    public required Guid SessionId { get; init; }
    public required string Name { get; init; }
    public required IEnumerable<SessionMessageDto> Messages { get; init; }
}

public sealed record SessionMessageDto
{
    public required Guid Id { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public List<MessageVariantDto>? Variants { get; init; }
}

public sealed record MessageVariantDto
{
    public required string Content { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record SessionDeletedResponse
{
    public required Guid Deleted { get; init; }
}

public sealed record SessionMessageDeletedResponse
{
    public required int Removed { get; init; }
}

public sealed record SessionForkResponse
{
    public required Guid SessionId { get; init; }
    public required string Name { get; init; }
    public required int MessageCount { get; init; }
}

public sealed record SessionRegenerateResponse
{
    public required Guid? ParentId { get; init; }
    public required Guid SessionId { get; init; }
}
