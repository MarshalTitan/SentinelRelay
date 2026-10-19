namespace SentinelRelay.RemoteDesktop;

public sealed class PairingForm : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Label countdown = new() { AutoSize = true };
    private readonly TextBox invitationBox;
    private readonly DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(2);

    public PairingForm(string url, string invitation)
    {
        Text = "Pair your phone";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(650, 310);
        MinimumSize = new Size(560, 360);
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "On Android: Sentinel Remote > Pair a PC", AutoSize = true }, 0, 0);
        layout.Controls.Add(new Label { Text = "Server URL", AutoSize = true }, 0, 1);
        layout.Controls.Add(new TextBox { Text = url, ReadOnly = true, Dock = DockStyle.Fill }, 0, 2);
        layout.Controls.Add(new Label { Text = "One-time invitation - keep private", AutoSize = true }, 0, 3);
        invitationBox = new TextBox { Text = invitation, ReadOnly = true, Multiline = true, Height = 60, Dock = DockStyle.Fill };
        layout.Controls.Add(invitationBox, 0, 4);
        layout.Controls.Add(countdown, 0, 5);
        var close = new Button { Text = "Done", AutoSize = true, DialogResult = DialogResult.OK };
        layout.Controls.Add(close, 0, 6);
        Controls.Add(layout);
        AcceptButton = close;
        UpdateCountdown();
        timer.Tick += (_, _) => UpdateCountdown();
        timer.Start();
    }
    private void UpdateCountdown()
    {
        var seconds = Math.Max(0, (int)(expires - DateTimeOffset.UtcNow).TotalSeconds);
        countdown.Text = seconds > 0 ? "Expires in " + seconds + " seconds. PC label can be any name you prefer." : "Invitation expired. Close this window and create another.";
        if (seconds == 0) { invitationBox.Clear(); timer.Stop(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); invitationBox.Clear(); }
        base.Dispose(disposing);
    }
}
