namespace QuillForge.Core.Utilities;

/// <summary>
/// Rough token count estimator using the ~4 characters per token heuristic.
/// Good enough for budgeting decisions; not a replacement for a real tokenizer.
/// </summary>
public static class TokenEstimator
{
    private const int CharsPerToken = 4;

    public static int Estimate(string text)
        => string.IsNullOrEmpty(text) ? 0 : text.Length / CharsPerToken;

    public static int Estimate(IEnumerable<string> texts)
        => texts.Sum(t => Estimate(t));
}
