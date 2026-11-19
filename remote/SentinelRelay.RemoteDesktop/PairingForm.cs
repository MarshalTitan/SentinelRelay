using System.Text;

namespace SentinelRelay.RemoteDesktop;

public sealed class PairingForm : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Label countdown = new() { AutoSize = true };
    private readonly TextBox invitationBox;
    private readonly Action? refresh;
    private readonly DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(2);

    public PairingForm(string url, string invitation, Action? refresh = null)
    {
        this.refresh = refresh;
        Text = "Pair your phone";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(650, 350);
        MinimumSize = new Size(560, 390);
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "On your phone: Sentinel Remote > Pair a PC. Paste the setup code below.", AutoSize = true }, 0, 0);
        layout.Controls.Add(new Label { Text = "Server URL", AutoSize = true }, 0, 1);
        layout.Controls.Add(new TextBox { Text = url, ReadOnly = true, Dock = DockStyle.Fill }, 0, 2);
        layout.Controls.Add(new Label { Text = "Setup code - expires in two minutes; keep private", AutoSize = true }, 0, 3);
        invitationBox = new TextBox { Text = "sentinel-remote:1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(url + "\n" + invitation)).TrimEnd('=').Replace('+', '-').Replace('/', '_'), ReadOnly = true, Multiline = true, Height = 80, Dock = DockStyle.Fill };
        layout.Controls.Add(invitationBox, 0, 4);
        layout.Controls.Add(countdown, 0, 5);
        var close = new Button { Text = "Done", AutoSize = true, DialogResult = DialogResult.OK };
        layout.Controls.Add(close, 0, 6);
        layout.SizeChanged += (_, _) =>
        {
            foreach (var label in layout.Controls.OfType<Label>())
                label.MaximumSize = new Size(Math.Max(240, layout.ClientSize.Width - layout.Padding.Horizontal), 0);
        };
        Controls.Add(layout);
        AcceptButton = close;
        UpdateCountdown();
        timer.Tick += (_, _) => UpdateCountdown();
        timer.Start();
    }
    private void UpdateCountdown()
    {
        try { refresh?.Invoke(); } catch { /* Retry a busy local state transaction on the next tick. */ }
        var seconds = Math.Max(0, (int)(expires - DateTimeOffset.UtcNow).TotalSeconds);
        countdown.Text = seconds > 0 ? "Expires in " + seconds + " seconds. Pair once; the phone remembers this PC." : "Invitation expired. Close this window and create another.";
        if (seconds == 0) { invitationBox.Clear(); timer.Stop(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); invitationBox.Clear(); }
        base.Dispose(disposing);
    }
}
