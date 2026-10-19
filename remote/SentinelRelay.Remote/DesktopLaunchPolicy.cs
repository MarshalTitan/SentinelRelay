namespace SentinelRelay.Remote;

/// <summary>Windows token types: Default=1 has no linked token, Full=2, Limited=3.
/// Administrator role membership alone does not tell us how the user launched an app.</summary>
public static class DesktopLaunchPolicy
{
    public static bool RequiresStandardToken(int elevationType, bool serviceAccount, bool alreadyRetried)
    {
        if (serviceAccount) throw new InvalidOperationException("interactive_user_required");
        return elevationType switch
        {
            1 or 3 => false,
            2 when !alreadyRetried => true,
            _ => throw new InvalidOperationException("standard_user_launch_unavailable"),
        };
    }

    // CreateProcess receives an explicit application path; this quotes argv for that executable, never a shell.
    public static string QuoteArgument(string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("invalid_argument");
        var result = new System.Text.StringBuilder("\"");
        int slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        return result.Append('"').ToString();
    }
}
