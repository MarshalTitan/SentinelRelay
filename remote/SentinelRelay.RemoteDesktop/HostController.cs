using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using SentinelRelay.Remote;
using SentinelRelay.RemoteHost;

namespace SentinelRelay.RemoteDesktop;

public sealed record DetectedGame(GameBinding Binding, string Character, string? World)
{
    public override string ToString() => Character + (string.IsNullOrWhiteSpace(World) ? "" : " @ " + World) + " (game " + Binding.ProcessId + ")";
}

public sealed class HostController : IAsyncDisposable
{
    private WebApplication? app;
    public bool Running => app is not null;
    public async Task Start(TailnetIdentity identity, IStateStore store)
    {
        if (app is not null) return;
        var candidate = RemoteWeb.Create(new RemoteAuthority(store, gameBridge: new PipeGameBridge()), identity.Host, identity.Login);
        try { await candidate.StartAsync(); app = candidate; }
        catch
        {
            await candidate.DisposeAsync();
            throw new SetupException("The host could not start. Close the old PowerShell host window or another Sentinel Remote copy, then try again.");
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (app is null) return;
        var current = app; app = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await current.StopAsync(timeout.Token); }
        finally { await current.DisposeAsync(); }
    }
    public static DetectedGame[] Discover()
    {
        var found = new List<DetectedGame>();
        foreach (var process in Process.GetProcessesByName("ffxiv_dx11").Take(16))
        {
            using (process)
            {
                try
                {
                    var binding = PipeGameBridge.Enroll("", process.Id);
                    var snapshot = new PipeGameBridge().Read(binding);
                    if (snapshot is not null) found.Add(new(binding, snapshot.Character, snapshot.World));
                }
                catch { /* Not opted in, different user, paused, or logging in. */ }
            }
        }
        return found.ToArray();
    }
}
