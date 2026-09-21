using System.Globalization;
using System.Text;

namespace SentinelRelay.Core;

public static class MessageSanitizer
{
    public static string SanitizePlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse)
            {
                if (rune.Value is '\r' or '\n' or '\t')
                    AppendSpace(builder, ref previousWasSpace);
                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                AppendSpace(builder, ref previousWasSpace);
                continue;
            }

            builder.Append(rune.ToString());
            previousWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    public static string SanitizeForDiscord(string? value)
    {
        var sanitized = SanitizePlainText(value);
        if (sanitized.Length == 0)
            return sanitized;

        return sanitized
            .Replace("@everyone", "@\u200Beveryone", StringComparison.OrdinalIgnoreCase)
            .Replace("@here", "@\u200Bhere", StringComparison.OrdinalIgnoreCase)
            .Replace("<@", "<@\u200B", StringComparison.Ordinal)
            .Replace("<#", "<#\u200B", StringComparison.Ordinal);
    }

    public static string Signature(string value) => SanitizePlainText(value).Normalize(NormalizationForm.FormKC);

    private static void AppendSpace(StringBuilder builder, ref bool previousWasSpace)
    {
        if (builder.Length > 0 && !previousWasSpace)
            builder.Append(' ');
        previousWasSpace = true;
    }
}
