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

    public static bool IsValidOutbound(string value, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Message cannot be empty.";
            return false;
        }

        if (value.EnumerateRunes().Count() > ChannelPolicy.MaxMessageCharacters)
        {
            error = $"Message exceeds {ChannelPolicy.MaxMessageCharacters} characters.";
            return false;
        }

        if (Encoding.UTF8.GetByteCount(value) > ChannelPolicy.MaxMessageUtf8Bytes)
        {
            error = $"Message exceeds {ChannelPolicy.MaxMessageUtf8Bytes} UTF-8 bytes.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string Signature(string value) => SanitizePlainText(value).Normalize(NormalizationForm.FormKC);

    private static void AppendSpace(StringBuilder builder, ref bool previousWasSpace)
    {
        if (builder.Length > 0 && !previousWasSpace)
            builder.Append(' ');
        previousWasSpace = true;
    }
}

