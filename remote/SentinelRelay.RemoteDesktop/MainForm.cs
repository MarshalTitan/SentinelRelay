using System.Diagnostics;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteDesktop;

public sealed class MainForm : Form
{
    private readonly string directory;
    private readonly FileStateStore store;
    private readonly LocalAdministration admin;
    private DesktopPreferences preferences;
    private readonly TailscaleClient tailscale = new();
    private readonly HostController host = new();
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 8000 };
    private readonly Label status = TextLabel("Set up your private phone connection.", 11);
    private readonly TextBox address = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly ListBox games = new() { Dock = DockStyle.Fill, Height = 95, DisplayMember = "" };
    private readonly CheckedListBox grants = new() { Dock = DockStyle.Fill, Height = 90, CheckOnClick = true };
    private readonly ListBox devices = new() { Dock = DockStyle.Fill, Height = 70 };
    private readonly CheckBox startup = new() { Text = "Start with Windows", AutoSize = true };
    private readonly TextBox phoneName = new() { Text = "My phone", Width = 200, MaxLength = 64 };
    private readonly CheckBox control = new() { Text = "Allow rescue controls", Checked = true, AutoSize = true };
    private readonly List<Button> workButtons = [];
    private readonly Button emergency;
    private TailnetIdentity? identity;
    private DetectedGame[] detected = [];
    private bool busy, exiting, closing;
    private int refreshCount, accessGeneration;

    public MainForm(string directory, bool minimized = false, bool smoke = false)
    {
        this.directory = directory;
        store = new FileStateStore(directory);
        admin = new LocalAdministration(store);
        preferences = DesktopPreferences.Load(directory);
        Text = "Sentinel Remote";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(860, 700);
        MinimumSize = new Size(720, 640);
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(245, 243, 251);
        ForeColor = Color.FromArgb(34, 33, 54);
        AutoScaleMode = AutoScaleMode.Dpi;

        var page = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(24), BackColor = BackColor };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(page); Controls.Add(scroll);
        Add(page, TextLabel("Sentinel Remote", 22, FontStyle.Bold));
        Add(page, status);
        Add(page, TextLabel("1  Connect this PC", 13, FontStyle.Bold));
        Add(page, TextLabel("Install Tailscale on this PC and your phone. Sign into the same account on both."));
        Add(page, Row(
            Work("Connect securely", () => Connect(false)),
            Link("Install Tailscale", "https://tailscale.com/download/windows"),
            Link("Open Tailscale account", "https://login.tailscale.com/admin/machines")));
        Add(page, address);
        startup.Checked = !smoke && DesktopPreferences.StartsWithWindows();
        startup.CheckedChanged += (_, _) =>
        {
            try { DesktopPreferences.SetStartsWithWindows(startup.Checked); }
            catch { ShowError("Windows could not save the startup preference."); }
        };
        Add(page, startup);

        Add(page, TextLabel("2  Select your game", 13, FontStyle.Bold));
        Add(page, TextLabel("In Relay's General page, click Enable all mobile features once for each character. Log in, then refresh here."));
        Add(page, games);
        Add(page, Row(Work("Refresh games", RefreshGames), Work("Connect selected character", ConnectGame)));
        Add(page, TextLabel("Approved characters - check the ones this phone may access"));
        Add(page, grants);

        Add(page, TextLabel("3  Pair your phone", 13, FontStyle.Bold));
        Add(page, Row(new Label { Text = "Phone name", AutoSize = true, Padding = new Padding(0, 6, 4, 0) }, phoneName, control,
            Work("Show pairing invitation", PairPhone)));
        Add(page, TextLabel("Open Sentinel Remote on Android, choose Pair a PC, and enter the address and invitation shown here."));

        Add(page, TextLabel("Paired phones", 13, FontStyle.Bold));
        Add(page, devices);
        emergency = new Button { Text = "Disable all remote access", AutoSize = true, BackColor = Color.FromArgb(176, 42, 65), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Padding = new Padding(8) };
        emergency.Click += async (_, _) => await Disable();
        Add(page, Row(Work("Revoke selected phone", Revoke)));
        Add(page, TextLabel("Right-click the tray icon beside the Windows clock to reopen this window or exit."));
        // Keep emergency access reachable even on short laptop displays and while the page scrolls.
        var safetyBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true,
            Padding = new Padding(16, 6, 16, 6), BackColor = Color.White,
        };
        safetyBar.Controls.Add(emergency);
        safetyBar.Controls.Add(new Label { Text = "Closing the window keeps the host running in the tray.", AutoSize = true, Padding = new Padding(10, 12, 0, 0) });
        Controls.Add(safetyBar);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Sentinel Remote", null, (_, _) => Restore());
        menu.Items.Add("Disable all remote access", null, async (_, _) => await Disable());
        menu.Items.Add("Exit", null, async (_, _) => await Exit());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Sentinel Remote", ContextMenuStrip = menu, Visible = !smoke };
        tray.DoubleClick += (_, _) => Restore();
        FormClosing += (_, e) =>
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        refreshTimer.Tick += async (_, _) =>
        {
            if (busy || closing) return;
            await WorkSafely(async () =>
            {
                if (!host.Running && preferences.ResumeOnLaunch && admin.Snapshot().Enabled) await Connect(true);
                await RefreshGames();
            }, false);
        };
        Shown += async (_, _) =>
        {
            if (smoke) return;
            if (minimized) Hide();
            await WorkSafely(async () =>
            {
                if (preferences.ResumeOnLaunch && admin.Snapshot().Enabled) await Connect(true);
                await RefreshGames();
            }, false);
            refreshTimer.Start();
        };
        Populate(admin.Snapshot());
    }

    private static Label TextLabel(string text, float size = 10, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(1040, 0),
        Font = new Font("Segoe UI", size, style), Margin = new Padding(0, 5, 0, 6),
    };
    private static void Add(TableLayoutPanel parent, Control child)
    {
        child.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        child.Margin = new Padding(0, 4, 0, 4);
        parent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        parent.Controls.Add(child, 0, parent.RowCount++);
        // Wrapped labels follow available width when resized.
        if (child is Label label) parent.SizeChanged += (_, _) => label.MaximumSize = new Size(Math.Max(240, parent.ClientSize.Width - parent.Padding.Horizontal), 0);
    }
    private static FlowLayoutPanel Row(params Control[] children)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        foreach (var child in children) { child.Margin = new Padding(0, 3, 10, 3); row.Controls.Add(child); }
        return row;
    }
    private Button Work(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(5) };
        button.Click += async (_, _) => await WorkSafely(action);
        workButtons.Add(button);
        return button;
    }
    private static Button Link(string text, string url)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(5) };
        button.Click += (_, _) => OpenLink(url);
        return button;
    }
    private static void OpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host is not ("tailscale.com" or "login.tailscale.com")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
    }
    private async Task WorkSafely(Func<Task> action, bool dialog = true)
    {
        if (busy || closing) return;
        busy = true;
        foreach (var button in workButtons) button.Enabled = false;
        try { await action(); }
        catch (SetupException e)
        {
            status.Text = e.Message;
            if (e.ConsentUrl is not null) OpenLink(e.ConsentUrl);
            else if (dialog) ShowError(e.Message);
        }
        catch
        {
            status.Text = "Setup needs attention. Check Tailscale, Relay permissions, and the selected character, then try again.";
            if (dialog) ShowError(status.Text);
        }
        finally
        {
            busy = false;
            if (!IsDisposed) foreach (var button in workButtons) button.Enabled = true;
        }
    }
    private void ShowError(string message) => MessageBox.Show(this, message, "Sentinel Remote", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private async Task Connect(bool resume)
    {
        var generation = accessGeneration;
        var current = await tailscale.Identity();
        if (generation != accessGeneration || closing) return;
        if (resume && (current.Host != preferences.Host || current.Login != preferences.Login))
            throw new SetupException("Your Tailscale account or PC address changed. Click Connect securely to review the new connection.");
        await tailscale.EnsureServe(current, configure: !resume);
        if (generation != accessGeneration || closing) return;
        if (host.Running && identity != current)
        {
            admin.SetEnabled(false);
            await host.DisposeAsync();
        }
        if (generation != accessGeneration || closing) return;
        await host.Start(current, store);
        if (generation != accessGeneration || closing) { await host.DisposeAsync(); return; }
        // Automatic resume never enables authority. A disable from another local tool must win too.
        if (resume && !admin.Snapshot().Enabled) { await host.DisposeAsync(); return; }
        if (!resume && !admin.Snapshot().Enabled) admin.SetEnabled(true);
        identity = current;
        preferences = new(current.Host, current.Login, true);
        preferences.Save(directory);
        address.Text = current.Url;
        status.Text = "Connected privately. Your paired phones can access approved characters.";
        Populate(admin.Snapshot());
    }

    private async Task RefreshGames()
    {
        detected = await Task.Run(HostController.Discover);
        if (closing) return;
        admin.ReconnectKnown(detected.Select(g => g.Binding).ToArray());
        var selectedPid = (games.SelectedItem as DetectedGame)?.Binding.ProcessId;
        games.BeginUpdate();
        games.Items.Clear(); games.Items.AddRange(detected);
        if (detected.Length > 0)
            games.SelectedIndex = Math.Max(0, Array.FindIndex(detected, g => g.Binding.ProcessId == selectedPid));
        games.EndUpdate();
        var snapshot = admin.Snapshot();
        Populate(snapshot);
        if (!snapshot.Enabled && host.Running)
        {
            preferences = preferences with { ResumeOnLaunch = false };
            preferences.Save(directory);
            await host.DisposeAsync();
            identity = null;
            status.Text = "All remote access disabled. Click Connect securely to enable it again.";
            return;
        }
        if (detected.Length == 0 && !host.Running)
            status.Text = "Log into FFXIV and enable mobile features in Relay, then refresh games.";
        if (host.Running && ++refreshCount % 3 == 0)
        {
            try
            {
                var current = await tailscale.Identity();
                if (identity != current) throw new InvalidOperationException();
                status.Text = "Connected privately. " + detected.Length + " available game(s).";
            }
            catch
            {
                status.Text = "Tailscale is disconnected or changed. Reconnect Tailscale, then click Connect securely.";
                await host.DisposeAsync();
                identity = null;
            }
        }
    }
    private async Task ConnectGame()
    {
        if (games.SelectedItem is not DetectedGame selected) throw new SetupException("Select an available game first.");
        // Re-read the actual process and pipe before approving it; never trust an old list entry.
        var current = await Task.Run(() => SentinelRelay.RemoteHost.PipeGameBridge.Enroll("", selected.Binding.ProcessId));
        if (current.CharacterKey != selected.Binding.CharacterKey || current.StartedUtcTicks != selected.Binding.StartedUtcTicks)
            throw new SetupException("That game changed. Refresh games and select the intended character again.");
        var instance = admin.ConnectGame(current, selected.Character);
        Populate(admin.Snapshot());
        status.Text = instance.Label + " approved. It will reconnect after a game restart when its identity matches.";
    }
    private void Populate(LocalHostSnapshot snapshot)
    {
        var selected = grants.CheckedItems.Cast<Slot>().Select(s => s.Id).ToHashSet();
        var known = grants.Items.Cast<Slot>().Select(s => s.Id).ToHashSet();
        grants.BeginUpdate(); grants.Items.Clear();
        foreach (var slot in snapshot.Instances)
        {
            var binding = snapshot.Bindings.SingleOrDefault(b => b.InstanceId == slot.InstanceId);
            var available = binding is not null && detected.Any(g => g.Binding.ProcessId == binding.ProcessId &&
                g.Binding.StartedUtcTicks == binding.StartedUtcTicks && g.Binding.CharacterKey == binding.CharacterKey);
            grants.Items.Add(new Slot(slot.InstanceId, slot.Label + (available ? " - available" : " - waiting for game")),
                selected.Contains(slot.InstanceId) || !known.Contains(slot.InstanceId));
        }
        grants.EndUpdate();
        var deviceId = (devices.SelectedItem as DeviceRow)?.Id;
        devices.BeginUpdate(); devices.Items.Clear();
        foreach (var device in snapshot.Devices.Where(d => !d.Revoked))
            devices.Items.Add(new DeviceRow(device.Id, device.Label, device.Control));
        if (deviceId is not null)
            for (int i = 0; i < devices.Items.Count; i++)
                if (((DeviceRow)devices.Items[i]).Id == deviceId) devices.SelectedIndex = i;
        devices.EndUpdate();
    }
    private Task PairPhone()
    {
        if (!host.Running || identity is null || !admin.Snapshot().Enabled)
            throw new SetupException("Click Connect securely before pairing a phone.");
        var ids = grants.CheckedItems.Cast<Slot>().Select(s => s.Id).ToArray();
        if (ids.Length == 0) throw new SetupException("Check at least one approved character for this phone.");
        var invitation = admin.Invite(phoneName.Text.Trim(), ids, control.Checked);
        using var dialog = new PairingForm(identity.Url, invitation);
        dialog.ShowDialog(this);
        Populate(admin.Snapshot());
        return Task.CompletedTask;
    }
    private Task Revoke()
    {
        if (devices.SelectedItem is not DeviceRow device) throw new SetupException("Select a paired phone first.");
        admin.Revoke(device.Id);
        Populate(admin.Snapshot());
        status.Text = "Phone access revoked.";
        return Task.CompletedTask;
    }
    private async Task Disable()
    {
        accessGeneration++;
        // Memory is disabled first so an in-progress connection or a later timer cannot resume it.
        preferences = preferences with { ResumeOnLaunch = false };
        try
        {
            var saved = await LocalEmergencyStop.Run(
                () => host.DisposeAsync().AsTask(),
                () => admin.SetEnabled(false),
                () => preferences.Save(directory));
            identity = null;
            address.Clear();
            status.Text = saved
                ? "All remote access disabled. Click Connect securely to enable it again."
                : "This host is stopped, but Windows could not save the disabled setting. Exit the app and resolve local settings before restarting.";
        }
        catch
        {
            // If stopping the listener itself fails, terminate this app rather than leave it available.
            exiting = true;
            closing = true;
            refreshTimer.Stop();
            tray.Visible = false;
            Close();
        }
    }
    private void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private async Task Exit()
    {
        if (closing) return;
        closing = true;
        accessGeneration++;
        refreshTimer.Stop();
        await host.DisposeAsync();
        exiting = true;
        tray.Visible = false;
        Close();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { refreshTimer.Dispose(); tray.Dispose(); }
        base.Dispose(disposing);
    }
    private sealed record Slot(string Id, string Label) { public override string ToString() => Label; }
    private sealed record DeviceRow(string Id, string Label, bool Control)
    {
        public override string ToString() => Label + (Control ? " - status, screenshots, rescue controls" : " - view only");
    }
}
