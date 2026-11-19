using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SentinelRelay.RemoteInput;

sealed class CheckWindow : Form
{
    private readonly Button run = new() { Text = "Run gamepad check", AutoSize = true, Padding = new Padding(14, 8, 14, 8) };
    private readonly Button copy = new() { Text = "Copy result", AutoSize = true, Enabled = false, Padding = new Padding(12, 8, 12, 8) };
    private readonly Button driver = new() { Text = "Open official driver download", AutoSize = true, Padding = new Padding(10, 7, 10, 7) };
    private readonly Label status = new() { Text = "Not tested", AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
    private readonly TextBox details = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
        BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9)
    };
    private readonly int elevationType;
    private readonly List<string> report = [];
    private string phase = "not_started";
    private bool running;

    public CheckWindow(int elevationType)
    {
        this.elevationType = elevationType;
        Text = "Sentinel Remote - Gamepad Check";
        ClientSize = new Size(760, 710); MinimumSize = new Size(700, 650);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(247, 246, 251);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 9 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.Controls.Add(Label("Check gamepad support", 21, true), 0, 0);
        layout.Controls.Add(Label("Close FFXIV before running this check. It tests Windows controller input and automatic release; it does not connect to your phone or change Relay settings.", 10), 0, 1);
        layout.Controls.Add(driver, 0, 2);
        layout.Controls.Add(Label("First time on this PC? Install the official USBip 0.9.8.1 x64 driver, then restart Windows if its installer asks. The check itself runs normally, without administrator access.", 9), 0, 3);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(run); buttons.Controls.Add(copy); layout.Controls.Add(buttons, 0, 4);
        layout.Controls.Add(status, 0, 5);
        layout.Controls.Add(details, 0, 6);
        layout.Controls.Add(Label("The first check downloads the signed VIIPER controller component from its official release. Keep other games closed while the check runs.", 9), 0, 7);
        var licenses = new LinkLabel { Text = "Controller component: VIIPER 0.9.0 (GPL-3.0) - license and source", AutoSize = true, LinkColor = Color.FromArgb(75, 48, 145) };
        licenses.LinkClicked += (_, _) => Open("https://github.com/Alia5/VIIPER/tree/v0.9.0");
        layout.Controls.Add(licenses, 0, 8); Controls.Add(layout);
        run.BackColor = Color.FromArgb(94, 67, 153); run.ForeColor = Color.White; run.FlatStyle = FlatStyle.Flat;
        run.Click += async (_, _) => await RunCheck();
        copy.Click += (_, _) => Clipboard.SetText(string.Join(Environment.NewLine, report));
        driver.Click += (_, _) => Open("https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.8.1");
        details.Text = "No check has run yet. Your installed Sentinel Remote app keeps its current pairing and settings.";
    }

    private static Label Label(string text, int size, bool bold = false) => new()
    {
        Text = text, Dock = DockStyle.Fill, AutoSize = false,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = Color.FromArgb(43, 38, 57)
    };
    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { MessageBox.Show("The download page could not open. Check your browser and connection.", "Sentinel Remote"); }
    }
    private static bool GamesClosed()
    {
        try
        {
            var games = Process.GetProcessesByName("ffxiv_dx11").Concat(Process.GetProcessesByName("ffxiv")).ToArray();
            try { return games.Length == 0; }
            finally { foreach (var game in games) game.Dispose(); }
        }
        catch { return false; }
    }
    private void Add(string value)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { Invoke(() => Add(value)); } catch (InvalidOperationException) { }
            return;
        }
        report.Add(value);
        details.Text = string.Join(Environment.NewLine, report);
    }

    private async Task RunCheck()
    {
        if (running) return;
        if (!GamesClosed())
        {
            status.Text = "Close FFXIV, then run the check.";
            status.ForeColor = Color.FromArgb(151, 87, 15);
            return;
        }
        running = true; run.Enabled = driver.Enabled = copy.Enabled = false;
        status.Text = "Preparing the controller check...";
        status.ForeColor = Color.FromArgb(75, 48, 145);
        report.Clear();
        report.Add("Sentinel Remote Gamepad Check 0.1.0");
        report.Add("Windows: " + RuntimeInformation.OSDescription);
        report.Add("Normal-session token type: " + elevationType);
        var passed = 0;
        try
        {
            phase = "download_or_verify";
            var backend = await DownloadBackend();
            phase = "native_input";
            status.Text = "Checking Windows input and automatic release...";
            var worker = Path.Combine(AppContext.BaseDirectory, "worker", "SentinelRelay.RemoteInput.exe");
            await Task.Run(() => NativeProof.Run(backend, worker, true, (ok, name) =>
            {
                Add((ok ? "PASS " : "FAIL ") + name);
                if (!ok) throw new InvalidOperationException("native_check_failed");
                Interlocked.Increment(ref passed);
            }, GamesClosed));
            report.Add("Result: PASS");
            status.Text = "Gamepad support check passed";
            status.ForeColor = Color.FromArgb(25, 112, 67);
            report.Add("Paste this result back into the setup conversation. Phone controls still require the next app integration.");
        }
        catch (Exception error)
        {
            report.Add("Result: NOT VERIFIED");
            report.Add("Phase: " + phase);
            report.Add("Completed checks: " + passed);
            report.Add("Error kind: " + error.GetType().Name);
            if (error is InputBackendException failure) report.Add("Code: " + failure.Code);
            status.Text = "Gamepad support is not verified yet";
            status.ForeColor = Color.FromArgb(158, 74, 28);
            report.Add("Copy this result into the setup conversation. If you just installed the driver and it requested a restart, restart Windows before checking again.");
        }
        finally
        {
            details.Text = string.Join(Environment.NewLine, report);
            running = false; run.Enabled = driver.Enabled = copy.Enabled = true;
        }
    }

    private static async Task<string> DownloadBackend()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sentinel", "GamepadCheck", "backend");
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("backend_cache_link");
        var path = Path.Combine(directory, "viiper-0.9.0.exe");
        if (File.Exists(path) && await Task.Run(() => ValidBackend(path))) return path;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var response = await client.GetAsync("https://github.com/Alia5/VIIPER/releases/download/v0.9.0/viiper-windows-amd64.exe",
            HttpCompletionOption.ResponseHeadersRead, cancel.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 32 * 1024 * 1024) throw new InvalidDataException("backend_size");
        var temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".download");
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var input = await response.Content.ReadAsStreamAsync(cancel.Token))
            {
                var buffer = new byte[65536]; long total = 0;
                while (true)
                {
                    var count = await input.ReadAsync(buffer, cancel.Token); if (count == 0) break;
                    total += count; if (total > 32 * 1024 * 1024) throw new InvalidDataException("backend_size");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancel.Token);
                }
            }
            if (!await Task.Run(() => ValidBackend(temp))) throw new InvalidDataException("backend_verification");
            File.Move(temp, path, true);
            return path;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static bool ValidBackend(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(ViiperBackend.WindowsSha256, StringComparison.OrdinalIgnoreCase) &&
            SignatureCheck.Valid(path);
    }

    public void Preview(string state)
    {
        if (state == "passed")
        {
            status.Text = "Gamepad support check passed"; status.ForeColor = Color.FromArgb(25, 112, 67);
            details.Text = "Preview only\r\nWindows controller recognized\r\nBoth sticks, triggers and buttons verified\r\nAutomatic release verified";
            copy.Enabled = true;
        }
        else if (state == "failed")
        {
            status.Text = "Gamepad support is not verified yet"; status.ForeColor = Color.FromArgb(158, 74, 28);
            details.Text = "Preview only\r\nResult: NOT VERIFIED\r\nCopy the result into the setup conversation.";
        }
    }
}
