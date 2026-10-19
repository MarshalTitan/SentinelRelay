using System.Diagnostics;
using System.Text.RegularExpressions;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteDesktop;

public sealed class SetupException(string message, string? consentUrl = null) : Exception(message)
{
    public string? ConsentUrl { get; } = consentUrl;
}

public sealed class TailscaleClient
{
    private static string Executable()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
        if (!File.Exists(path)) throw new SetupException("Install Tailscale, sign in, then click Connect securely again.");
        return path;
    }
    private static async Task<(int ExitCode, string Output, string Error)> Run(params string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(Executable())
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        }};
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            await process.WaitForExitAsync();
            // Preserve a web-consent link already printed by a waiting CLI, without logging it.
            return (-1, await output, await error);
        }
    }
    public async Task<TailnetIdentity> Identity()
    {
        var result = await Run("status", "--json");
        if (result.ExitCode != 0) throw new SetupException("Open Tailscale and sign in on this PC, then try again.");
        try { return DesktopSetupPolicy.ReadIdentity(result.Output.Trim()); }
        catch { throw new SetupException("Tailscale is not connected with a personal login. Open Tailscale, connect, then try again."); }
    }
    public async Task EnsureServe(TailnetIdentity identity, bool configure = true)
    {
        var before = await Run("serve", "status", "--json");
        if (before.ExitCode != 0) throw new SetupException("Tailscale could not read its Serve configuration. Open Tailscale and try again.");
        var state = DesktopSetupPolicy.ServeState(before.Output.Trim(), identity.Host);
        if (state == "conflict") throw new SetupException("This PC already has another Serve route or Funnel configured. Keep that setup unchanged and review it before connecting Sentinel Remote.");
        if (state == "ready") return;
        if (!configure) throw new SetupException("Private Serve needs setup. Click Connect securely to finish it.");
        var result = await Run("serve", "--bg", "32179");
        var consent = Regex.Match(result.Output + "\n" + result.Error, @"https://login\.tailscale\.com/f/serve\?[^\s]+").Value.TrimEnd('.', ')', '"');
        if (consent.Length > 0)
            throw new SetupException("Approve private HTTPS Serve in your browser, then click Connect securely again.", consent);
        if (result.ExitCode != 0) throw new SetupException("Tailscale could not enable private Serve. Open Tailscale and try again.");
        var after = await Run("serve", "status", "--json");
        if (after.ExitCode != 0 || DesktopSetupPolicy.ServeState(after.Output.Trim(), identity.Host) != "ready")
            throw new SetupException("Private Serve is not ready yet. Finish Tailscale setup, then try again.");
    }
}
