using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SentinelRelay.Remote;
using SentinelRelay.RemoteInput;

static class NativeProof
{
    public static async Task Run(string backend, string worker, bool native, Action<bool, string> check)
    {
        if (native && !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var baseline = native ? Enumerable.Range(0, 4).Where(i => Read(i) is not null).ToHashSet() : [];
        check(!native || baseline.Count == 0, "native proof runner has no preexisting XInput controller");
        // No hardware proof is claimed by --protocol. It explicitly disables USB auto-attach.
        var failures = OperatingSystem.IsWindows() ? new[] { "eof", "silence", "kill", "stale", "replay" } :
            new[] { "eof", "silence", "stale", "replay" }; // Windows job semantics are tested on Windows only.
        foreach (var failure in failures)
        {
            using var process = Start(worker, backend, !native);
            var readyText = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            using var ready = JsonDocument.Parse(readyText ?? "{}");
            if (!ready.RootElement.TryGetProperty("ready", out var flag) || !flag.GetBoolean())
                throw new IOException("worker readiness failed: " + readyText);
            var pid = ready.RootElement.GetProperty("backendPid").GetInt32();
            var apiPort = ready.RootElement.GetProperty("apiPort").GetInt32();
            var usbPort = ready.RootElement.GetProperty("usbPort").GetInt32();
            try
            {
                long sequence = 0;
                async Task Send(ControllerFrame value) =>
                    await WorkerWire.Write(process.StandardInput.BaseStream, WorkerPacket.Create(Interlocked.Increment(ref sequence), value));
                using (var warmupStop = new CancellationTokenSource())
                {
                    await Send(ControllerFrame.Neutral);
                    var warmup = Task.Run(async () =>
                    {
                        try
                        {
                            while (true)
                            {
                                await Task.Delay(100, warmupStop.Token);
                                await Send(ControllerFrame.Neutral);
                            }
                        }
                        catch (OperationCanceledException) { }
                    });
                    try
                    {
                        if (OperatingSystem.IsWindows())
                            check(await LoopbackOnly(pid, apiPort, usbPort), "worker backend listens only on IPv4 loopback");
                    }
                    finally { warmupStop.Cancel(); await warmup; }
                }
                var frame = new ControllerFrame(0.5, -0.5, 0.25, -0.25, 0.75, 0.25, 0x1201);
                await Send(frame);
                var slot = -1;
                var wait = Stopwatch.GetTimestamp();
                if (native)
                {
                    while (Stopwatch.GetElapsedTime(wait) < TimeSpan.FromSeconds(6))
                    {
                        for (var i = 0; i < 4; i++)
                        {
                            var value = Read(i);
                            if (!baseline.Contains(i) && value is { } state && state.Gamepad.Buttons == frame.Buttons)
                            {
                                slot = i;
                                check(state.Gamepad.LeftX == 16384 && state.Gamepad.LeftY == -16384 &&
                                    state.Gamepad.RightX == 8192 && state.Gamepad.RightY == -8192 &&
                                    state.Gamepad.LeftTrigger == 191 && state.Gamepad.RightTrigger == 64,
                                    "Windows XInput reads submitted buttons, both sticks and independent triggers");
                                break;
                            }
                        }
                        if (slot >= 0) break;
                        await Task.Delay(50); await Send(frame);
                    }
                    check(slot >= 0, "real Windows XInput controller enumerated");
                    await Send(ControllerFrame.Neutral);
                    await Wait(() => Read(slot) is { } state && state.Gamepad.Buttons == 0 &&
                        state.Gamepad.LeftX == 0 && state.Gamepad.LeftY == 0 && state.Gamepad.RightX == 0 &&
                        state.Gamepad.RightY == 0 && state.Gamepad.LeftTrigger == 0 && state.Gamepad.RightTrigger == 0,
                        TimeSpan.FromMilliseconds(500));
                    check(Read(slot) is { } neutral && neutral.Gamepad.Buttons == 0 && neutral.Gamepad.LeftX == 0 &&
                        neutral.Gamepad.LeftY == 0 && neutral.Gamepad.RightX == 0 && neutral.Gamepad.RightY == 0 &&
                        neutral.Gamepad.LeftTrigger == 0 && neutral.Gamepad.RightTrigger == 0, "complete neutral frame releases real native controls");
                    await Send(frame);
                    await Wait(() => Read(slot) is { } state && state.Gamepad.Buttons == frame.Buttons, TimeSpan.FromMilliseconds(500));
                    check(Read(slot) is { } held && held.Gamepad.Buttons == frame.Buttons, "held native input established before failure");
                }
                var started = Stopwatch.GetTimestamp();
                switch (failure)
                {
                    case "eof": process.StandardInput.Close(); break;
                    case "kill": process.Kill(); break; // Not entire tree: Windows job must kill VIIPER.
                    case "stale":
                        var stale = WorkerPacket.Create(++sequence, frame) with { IssuedAt = Stopwatch.GetTimestamp() - Stopwatch.Frequency };
                        await WorkerWire.Write(process.StandardInput.BaseStream, stale); break;
                    case "replay": await WorkerWire.Write(process.StandardInput.BaseStream, WorkerPacket.Create(sequence, frame)); break;
                    // Silence leaves the pipe OPEN. The independent worker watchdog must act.
                }
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                await Wait(() => !IsAlive(pid), TimeSpan.FromMilliseconds(500));
                check(!IsAlive(pid), "backend exits after worker " + failure);
                if (native)
                {
                    await Wait(() => Read(slot) is null, TimeSpan.FromSeconds(2));
                    check(Read(slot) is null, "native held controller disconnected after worker " + failure);
                    Console.WriteLine("NATIVE_RELEASE " + failure + " " + (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds + "ms");
                }
                using var server = new System.Net.Sockets.TcpClient();
                try { await server.ConnectAsync(System.Net.IPAddress.Loopback, apiPort); check(false, "orphan listener removed"); }
                catch (System.Net.Sockets.SocketException) { check(true, "no orphan backend API after worker " + failure); }
            }
            finally
            {
                // Ensure a failed test does not leave native devices or listeners on the disposable runner.
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                try { using var child = Process.GetProcessById(pid); if (!child.HasExited) child.Kill(true); }
                catch (ArgumentException) { } catch (InvalidOperationException) { }
            }
        }
    }

    private static Process Start(string worker, string backend, bool protocol)
    {
        var isDll = worker.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(isDll ? "dotnet" : Path.GetFullPath(worker))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (isDll) start.ArgumentList.Add(Path.GetFullPath(worker));
        start.ArgumentList.Add("--worker"); start.ArgumentList.Add(Path.GetFullPath(backend));
        if (protocol) start.ArgumentList.Add("--protocol-only");
        return Process.Start(start) ?? throw new IOException("worker start");
    }
    private static bool IsAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
    private static async Task Wait(Func<bool> condition, TimeSpan deadline)
    {
        var now = Stopwatch.GetTimestamp();
        while (!condition() && Stopwatch.GetElapsedTime(now) < deadline) await Task.Delay(20);
    }
    private static async Task<bool> LoopbackOnly(int pid, int api, int usb)
    {
        // CI-only inspection. Fixed numeric IDs/ports, no user-provided script or shell command.
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$listeners = @(Get-NetTCPConnection -State Listen -OwningProcess " + pid +
            " -ErrorAction Stop); if ($listeners.Count -eq 2 -and @($listeners | Where-Object { $_.LocalAddress -ne '127.0.0.1' }).Count -eq 0" +
            " -and @($listeners | Where-Object { $_.LocalPort -eq " + api + " }).Count -eq 1" +
            " -and @($listeners | Where-Object { $_.LocalPort -eq " + usb + " }).Count -eq 1) { exit 0 } else { exit 1 }");
        using var process = Process.Start(start) ?? throw new IOException("listener inspection");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        return process.ExitCode == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct State { public uint PacketNumber; public Gamepad Gamepad; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState(uint index, out State state);
    private static State? Read(int index) => GetState((uint)index, out var state) == 0 ? state : null;
}
