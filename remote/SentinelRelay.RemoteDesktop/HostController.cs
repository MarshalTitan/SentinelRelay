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
    public ManualInput? Input { get; private set; }
    private string? backend;
    public DesktopStream? Desktop { get; private set; }
    public RecoveryActions Recovery { get; } = new();
    private Task? preparation;
    private readonly HashSet<string> trusted = [];
    public int TrustedPhoneCount => trusted.Count;
    public string PreparationStatus { get; private set; } = "Controller support will prepare when a phone is trusted.";
    public Task PrepareInput() => preparation is { IsCompleted: false } ? preparation : preparation = PrepareBackend();
    private async Task PrepareBackend()
    {
        PreparationStatus = "Preparing controller support...";
        try { backend = await BackendPackage.Prepare(); PreparationStatus = "Controller support ready."; }
        catch { PreparationStatus = "Controller setup needs attention. Open More options and retry."; throw; }
    }
    private async Task PrepareAutomatically()
    {
        try { await PrepareInput(); } catch { /* Show a stable local status; desktop access remains usable. */ }
    }
    public void SyncTrustedPhones(LocalHostSnapshot snapshot)
    {
        if (Input is null || Desktop is null) return;
        var wanted = snapshot.Enabled
            ? snapshot.Devices.Where(d => d.Trusted && d.Control && !d.Revoked).Select(d => d.Id).ToHashSet()
            : new HashSet<string>();
        foreach (var id in trusted.Except(wanted).ToArray())
        {
            Input.Permit(id, false); Desktop.Permit(id, false); Recovery.Permit(id, false); trusted.Remove(id);
        }
        foreach (var id in wanted.Except(trusted).ToArray())
        {
            Input.Permit(id, true); Desktop.Permit(id, true); Recovery.Permit(id, true); trusted.Add(id);
        }
        if (wanted.Count > 0 && backend is null && preparation is null) _ = PrepareAutomatically();
    }
    public void PermitInput(string device, bool enabled)
    {
        if (Input is null || backend is null) throw new SetupException("Connect securely and prepare gamepad support first.");
        Input.Permit(device, enabled);
    }
    public bool Running => app is not null;
    public async Task Start(TailnetIdentity identity, IStateStore store)
    {
        if (app is not null) return;
        var authority = new RemoteAuthority(store, gameBridge: new PipeGameBridge(), recovery: Recovery);
        var input = new ManualInput(authority, ManualTarget.Create,
            () => backend is null ? throw new SentinelRelay.RemoteInput.InputBackendException("input_not_prepared") : WorkerOutput.Start(backend));
        var desktop = new DesktopStream(authority, new DesktopAdapter());
        var candidate = RemoteWeb.Create(authority, identity.Host, identity.Login, input: input, desktop: desktop);
        try { await candidate.StartAsync(); app = candidate; Input = input; Desktop = desktop; }
        catch
        {
            input.Dispose(); desktop.Dispose();
            await candidate.DisposeAsync();
            throw new SetupException("The host could not start. Close the old PowerShell host window or another Sentinel Remote copy, then try again.");
        }
    }
    public async ValueTask DisposeAsync()
    {
        trusted.Clear();
        Desktop?.Dispose(); Desktop = null;
        Recovery.Clear();
        Input?.Dispose(); Input = null;
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
