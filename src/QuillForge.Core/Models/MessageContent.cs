namespace QuillForge.Core.Models;

/// <summary>
/// A block of content within a message. Messages can contain multiple content blocks.
/// </summary>
public abstract class ContentBlock
{
    public abstract string Type { get; }
}

/// <summary>
/// A plain text content block.
/// </summary>
public sealed class TextBlock(string text) : ContentBlock
{
    public override string Type => "text";
    public string Text { get; } = text;
}

/// <summary>
/// A tool invocation requested by the model.
/// </summary>
public sealed class ToolUseBlock(string id, string name, ToolInput input) : ContentBlock
{
    public override string Type => "tool_use";
    public string Id { get; } = id;
    public string Name { get; } = name;
    public ToolInput Input { get; } = input;
}

/// <summary>
/// The result of a tool invocation, sent back to the model.
/// </summary>
public sealed class ToolResultBlock(string toolUseId, string content, bool isError = false) : ContentBlock
{
    public override string Type => "tool_result";
    public string ToolUseId { get; } = toolUseId;
    public string Content { get; } = content;
    public bool IsError { get; } = isError;
}

/// <summary>
/// Content of a message, consisting of one or more content blocks.
/// </summary>
public sealed class MessageContent
{
    private readonly List<ContentBlock> _blocks;

    public MessageContent(IEnumerable<ContentBlock> blocks)
    {
        _blocks = [.. blocks];
    }

    public MessageContent(string text)
    {
        _blocks = [new TextBlock(text)];
    }

    public IReadOnlyList<ContentBlock> Blocks => _blocks;

    /// <summary>
    /// Concatenates all text blocks into a single string.
    /// </summary>
    public string GetText()
    {
        return string.Join("", _blocks.OfType<TextBlock>().Select(b => b.Text));
    }

    public IEnumerable<ToolUseBlock> GetToolCalls()
    {
        return _blocks.OfType<ToolUseBlock>();
    }
}
