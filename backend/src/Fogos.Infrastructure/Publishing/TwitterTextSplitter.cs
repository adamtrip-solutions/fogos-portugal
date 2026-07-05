namespace Fogos.Infrastructure.Publishing;

/// <summary>
/// Splits a long post into a thread on word boundaries (mirroring the legacy
/// <c>TwitterTool::splitTweets</c> greedy word packing) and appends <c>(i/n)</c> position suffixes.
/// </summary>
/// <remarks>
/// Deviation from legacy: the old PHP splitter added no numbering. This port adds <c>(i/n)</c>
/// suffixes (per the Wave-1 spec) so readers can follow multi-part threads; the packing algorithm
/// (greedy, single-space word boundaries, measured in UTF-16 chars) otherwise matches legacy intent.
/// Text at or under the limit returns a single unsuffixed element.
/// </remarks>
public static class TwitterTextSplitter
{
    public const int DefaultMaxLength = 280;

    // Room reserved for the worst-case " (nn/nn)" suffix while packing words.
    private const int SuffixBudget = 9;

    public static IReadOnlyList<string> Split(string text, int maxLength = DefaultMaxLength)
    {
        text = text ?? "";
        if (text.Length <= maxLength)
            return [text];

        var effective = Math.Max(1, maxLength - SuffixBudget);
        var words = text.Split(' ');
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            // A single word longer than the budget is hard-truncated (legacy truncates an oversized first word).
            var piece = word.Length > effective ? word[..effective] : word;

            var addedLength = current.Length == 0 ? piece.Length : current.Length + 1 + piece.Length;
            if (addedLength > effective && current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
                current.Append(' ');
            current.Append(piece);
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        var total = parts.Count;
        if (total <= 1)
            return parts;

        for (var i = 0; i < total; i++)
            parts[i] = $"{parts[i]} ({i + 1}/{total})";

        return parts;
    }
}
