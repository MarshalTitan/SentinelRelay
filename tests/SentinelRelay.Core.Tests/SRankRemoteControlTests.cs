using System.Text.Json;
using SentinelRelay.Core;
using SentinelRelay.Models;

internal static class SRankRemoteControlTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 3, 0, 0, DateTimeKind.Utc);
    private const string Checkpoint = "100000000000000000";
    public static void Run()
    {
        var tests = new (string, Action)[]
        {
            ("exact parser never allows raw, multiline, parameters or arbitrary commands", Parser),
            ("permission defaults OFF and is independent from chat replies", Permission),
            ("status and skip recognized without confirmation", Basic),
            ("reset needs fresh matching one-shot confirmation", Reset),
            ("expiry uses Discord request time, not a delayed batch processing time", Expiry),
            ("other user, channel, character and pending binding cannot confirm", Isolation),
            ("duplicate message, replayed batch and old checkpoints cannot execute twice", Replay),
            ("stale, bot, webhook, paused and wrong-character messages rejected", Sources),
            ("clear/reload/permission changes drop pending confirmation", Lifecycle),
            ("missing, malformed and incompatible IPC replies are unavailable", Response),
            ("compact replies omit timestamps and disable all mentions", Formatting),
        };
        foreach (var (name, test) in tests) { test(); Console.WriteLine("PASS SRank remote: " + name); }
        Console.WriteLine(tests.Length + "/" + tests.Length + " SRank remote control groups passed");
    }
    private static CharacterProfile Profile() => new()
    {
        CharacterKey = "cid:0000000000000001", CharacterName = "Test Character",
        RemoteSRankControlsEnabled = true, DiscordRepliesEnabled = false,
        DiscordRelayChannelId = "123456789012345678", AuthorizedDiscordUserId = "234567890123456789",
    };
    private static CharacterIdentity Identity(string key = "cid:0000000000000001") => new(key, "Test Character", "World", 1);
    private static DiscordChannelMessage Message(int id, string content, DateTime? time = null) => new()
    {
        Id = (100000000000000000UL + (ulong)id).ToString(), Content = content,
        Timestamp = new DateTimeOffset(time ?? Now), ChannelId = "123456789012345678",
        Author = new() { Id = "234567890123456789" },
    };
    private static SRankRemoteAuthorization Accept(SRankRemoteControlSession s, CharacterProfile p,
        DiscordChannelMessage m, DateTime? now = null, CharacterIdentity? identity = null, string checkpoint = Checkpoint)
        => s.Accept(p, identity ?? Identity(), m, checkpoint, now ?? Now);
    private static void Check(bool value) { if (!value) throw new Exception("SRank remote policy assertion failed"); }
    private static void Parser()
    {
        foreach (var text in new[] { "/srank status", "/srank skip", "/srank reset", "/srank reset confirm" })
            Check(SRankRemoteCommandParser.TryParse(text, out _));
        foreach (var text in new[] { "/srank raw", "/srank", "/execute", "/xl", "/srank skip extra", "/srank action:skip",
            "/srank reset confirm extra", "/srank status\n/xlplugins", "/srank  skip", "/srank reset\tconfirm" })
            Check(!SRankRemoteCommandParser.TryParse(text, out _));
        Check(!DiscordReplyCommandParser.TryParse("/srank skip", out _, out _));
    }
    private static void Permission()
    {
        Check(!new CharacterProfile().RemoteSRankControlsEnabled);
        var legacy = JsonSerializer.Deserialize<CharacterProfile>("{\"DiscordRepliesEnabled\":true}")!;
        Check(!legacy.RemoteSRankControlsEnabled && legacy.DiscordRepliesEnabled);
        var p = Profile(); p.RemoteSRankControlsEnabled = false; p.DiscordRepliesEnabled = true;
        Check(!Accept(new(), p, Message(1, "/srank skip")).Accepted);
        p.RemoteSRankControlsEnabled = true; p.DiscordRepliesEnabled = false;
        Check(Accept(new(), p, Message(1, "/srank skip")).Decision == SRankRemoteDecision.Skip);
    }
    private static void Basic()
    {
        var s = new SRankRemoteControlSession(); var p = Profile();
        Check(Accept(s, p, Message(1, "/srank status")).Decision == SRankRemoteDecision.Status);
        Check(Accept(s, p, Message(2, "/srank skip")).Decision == SRankRemoteDecision.Skip);
        Check(!Accept(s, p, Message(3, "/srank reset confirm")).Accepted);
    }
    private static void Reset()
    {
        var s = new SRankRemoteControlSession(); var p = Profile();
        Check(Accept(s, p, Message(1, "/srank reset")).Decision == SRankRemoteDecision.ConfirmationRequired);
        Check(Accept(s, p, Message(2, "/srank status"), Now.AddSeconds(5)).Decision == SRankRemoteDecision.Status);
        Check(Accept(s, p, Message(3, "/srank reset confirm", Now.AddSeconds(20)), Now.AddSeconds(20)).Decision == SRankRemoteDecision.Reset);
        Check(!Accept(s, p, Message(4, "/srank reset confirm", Now.AddSeconds(21)), Now.AddSeconds(21)).Accepted);
    }
    private static void Expiry()
    {
        foreach (var delay in new[] { 30, 31, 120 })
        {
            var s = new SRankRemoteControlSession(); var p = Profile();
            Accept(s, p, Message(1, "/srank reset"));
            Check(!Accept(s, p, Message(2, "/srank reset confirm", Now.AddSeconds(delay)), Now.AddSeconds(delay)).Accepted);
        }
        var delayed = new SRankRemoteControlSession();
        Check(!Accept(delayed, Profile(), Message(1, "/srank reset", Now.AddSeconds(-40))).Accepted);
        var sameBatch = new SRankRemoteControlSession();
        Accept(sameBatch, Profile(), Message(1, "/srank reset", Now.AddSeconds(-20)));
        Check(!Accept(sameBatch, Profile(), Message(2, "/srank reset confirm", Now.AddSeconds(11)), Now.AddSeconds(11)).Accepted);
    }
    private static void Isolation()
    {
        foreach (var mode in new[] { "user", "channel", "character", "profile-user", "profile-channel" })
        {
            var s = new SRankRemoteControlSession(); var p = Profile();
            Accept(s, p, Message(1, "/srank reset"));
            var confirm = Message(2, "/srank reset confirm"); var identity = Identity();
            if (mode == "user") confirm.Author.Id = "345678901234567890";
            if (mode == "channel") confirm.ChannelId = "345678901234567890";
            if (mode == "character") identity = Identity("cid:0000000000000002");
            if (mode == "profile-user") { p.AuthorizedDiscordUserId = "345678901234567890"; confirm.Author.Id = p.AuthorizedDiscordUserId; }
            if (mode == "profile-channel") { p.DiscordRelayChannelId = "345678901234567890"; confirm.ChannelId = p.DiscordRelayChannelId; }
            Check(!Accept(s, p, confirm, identity: identity).Accepted);
        }
    }
    private static void Replay()
    {
        var p = Profile(); var s = new SRankRemoteControlSession(); var executions = 0;
        foreach (var message in new[] { Message(2, "/srank skip"), Message(2, "/srank skip"), Message(1, "/srank skip") })
            if (Accept(s, p, message).Decision == SRankRemoteDecision.Skip) executions++;
        Check(executions == 1);
        Check(!Accept(new(), p, Message(2, "/srank skip"), checkpoint: Message(2, "").Id).Accepted);
        s.Clear();
        Check(!Accept(s, p, Message(2, "/srank skip")).Accepted);
    }
    private static void Sources()
    {
        foreach (var mode in new[] { "stale", "future", "bot", "webhook", "paused", "invalid-id" })
        {
            var p = Profile(); var m = Message(1, "/srank skip");
            if (mode == "stale") m.Timestamp = new(Now.AddMinutes(-3));
            if (mode == "future") m.Timestamp = new(Now.AddMinutes(1));
            if (mode == "bot") m.Author.Bot = true;
            if (mode == "webhook") m.WebhookId = "345678901234567890";
            if (mode == "paused") p.Paused = true;
            if (mode == "invalid-id") m.Id = "no-id";
            Check(!Accept(new(), p, m).Accepted);
        }
        var noCharacter = new SRankRemoteControlSession().Accept(Profile(), null, Message(1, "/srank status"), Checkpoint, Now);
        Check(!noCharacter.Accepted);
    }
    private static void Lifecycle()
    {
        var p = Profile(); var s = new SRankRemoteControlSession();
        Accept(s, p, Message(1, "/srank reset")); s.Clear();
        Check(!Accept(s, p, Message(2, "/srank reset confirm")).Accepted);
        Accept(s, p, Message(3, "/srank reset"));
        p.RemoteSRankControlsEnabled = false;
        Check(!Accept(s, p, Message(4, "/srank status")).Accepted);
        p.RemoteSRankControlsEnabled = true;
        Check(!Accept(s, p, Message(5, "/srank reset confirm")).Accepted);
        Check(!Accept(new(), p, Message(6, "/srank reset confirm")).Accepted);
    }
    private static string StatusJson() => JsonSerializer.Serialize(new
    {
        Schema = "sentinel.srank.remote.v1", Version = "0.7.62.0", Success = true, Reason = "Status",
        Status = new { Loaded = true, Enabled = true, Character = "Test Character", State = "SafeWait",
            StatusText = "Holding at safe point", Territory = 1191u, ActiveHunt = "Tyger", QueueCount = 2 },
    });
    private static void Response()
    {
        Check(SRankRemoteResponse.Decode(StatusJson()).Success);
        foreach (var input in new[] { "", "{}", "not json", StatusJson().Replace("sentinel.srank.remote.v1", "wrong"),
            new string('x', 8193), StatusJson().Replace("\"Loaded\":true", "\"Loaded\":false") })
            Check(!SRankRemoteResponse.Decode(input).Success);
        var rejected = SRankRemoteResponse.Decode(StatusJson().Replace("\"Reason\":\"Status\"", "\"Reason\":\"private-secret\""));
        Check(!rejected.Success && rejected.Reason == "Unavailable" && !rejected.Reason.Contains("private-secret"));
        Check(SRankRemoteResponse.Decode(StatusJson().Replace("\"ActiveHunt\":\"Tyger\"", "\"ActiveHunt\":null")).Success);
    }
    private static void Formatting()
    {
        var payload = SRankRemoteResponseFormatter.Format("@everyone", SRankRemoteDecision.Status, SRankRemoteResponse.Decode(StatusJson()));
        var json = JsonSerializer.Serialize(payload);
        Check(payload.AllowedMentions.Parse.Count == 0 && payload.Embeds!.Count == 1 &&
            payload.Embeds[0].Description.Contains("Queue: 2") && !json.Contains("\"timestamp\"") &&
            !payload.Embeds[0].Title.Contains("@everyone"));
    }
}
