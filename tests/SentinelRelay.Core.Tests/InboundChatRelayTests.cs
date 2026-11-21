using System.Net;
using System.Text.Json;
using SentinelRelay.Core;
using SentinelRelay.Models;
using SentinelRelay.Services;

internal static class InboundChatRelayTests
{
    private const string UserId = "123456789012345678";
    private static readonly DateTime Now = new(2026, 11, 21, 18, 0, 0, DateTimeKind.Utc);

    public static async Task Run()
    {
        var tests = new (string Name, Func<bool, Task> Run)[]
        {
            ("keyword-only FC delivers with its alert and one configured-user ping", KeywordOnly),
            ("nonmatching keyword-only FC stays blocked", Nonmatching),
            ("normal FC without a keyword still delivers", Normal),
            ("normal FC plus keyword produces exactly one HTTP delivery", NormalAndKeyword),
            ("keyword cannot monitor a channel absent from its own saved list", WrongChannel),
            ("pause blocks matching chat and already-admitted reward batches", Paused),
            ("multiple matching rules enhance one HTTP delivery and ping once", MultipleKeywords),
            ("missing profile or webhook blocks keyword-only forwarding", MissingRoute),
            ("keyword-only monitoring works for every exposed inbound channel", EveryInboundChannel),
            ("keyword-only rewards batch matching lines and suppress duplicates", KeywordRewards),
            ("normal rewards preserve all lines, order, and the keyword alert", NormalRewards),
            ("reward batch precedes ordinary chat and does not flush twice", RewardOrder),
            ("clearing on a character change drops rewards and duplicate history", CharacterChange),
            ("saved keyword modes, channel lists, IDs and pings survive migration", ConfigurationRoundTrip),
        };

        foreach (var embeds in new[] { false, true })
        foreach (var (name, run) in tests)
        {
            await run(embeds);
            Console.WriteLine($"PASS inbound relay ({(embeds ? "embed" : "text")}): {name}");
        }
        Console.WriteLine($"{tests.Length * 2}/{tests.Length * 2} inbound relay cases passed.");
    }

    private static CharacterProfile Profile(bool embeds, bool normallyEnabled = false) => new()
    {
        CharacterKey = "cid:one",
        ProtectedWebhookUrl = "protected-test-route",
        UseDiscordEmbeds = embeds,
        DiscordMentionUserId = UserId,
        EnabledInboundChannels = normallyEnabled ? [RelayChatType.FreeCompany] : [],
        Keywords = [Rule("ready", RelayChatType.FreeCompany)],
    };

    private static KeywordRule Rule(string keyword, RelayChatType channel, bool wholeWord = false) => new()
    {
        Keyword = keyword, WholeWord = wholeWord, PingDiscordUser = true, Channels = [channel],
    };

    private static CapturedChat Chat(string message, RelayChatType channel = RelayChatType.FreeCompany) =>
        new(channel, "Example Sender", "Example World", message, Now);

    // Exercise the same admission/batching and preparation methods used by Plugin,
    // then count real WebhookHttpSender requests against an in-memory HTTP handler.
    private sealed class Harness : IDisposable
    {
        public InboundChatRelay Relay { get; } = new();
        public RecordingHandler Handler { get; } = new();
        private readonly HttpClient client;
        private readonly WebhookHttpSender sender;

        public Harness()
        {
            client = new HttpClient(Handler);
            sender = new WebhookHttpSender(client);
        }

        public async Task Capture(CharacterProfile? profile, CapturedChat chat, DateTime? now = null)
        {
            foreach (var admitted in Relay.Capture(profile, chat, now ?? Now))
                await Queue(profile, admitted);
        }

        public Task Flush(CharacterProfile? profile, DateTime? now = null) =>
            Queue(profile, Relay.FlushIfDue(now ?? Now.AddSeconds(2)));

        public async Task Queue(CharacterProfile? profile, CapturedChat? chat)
        {
            if (!InboundChatRelay.TryPrepare(profile, chat, out var payloads))
                return;
            var result = await sender.SendAsync(
                new Uri("https://discord.com/api/webhooks/123456789012345678/test-only"),
                payloads, CancellationToken.None);
            Check(result.Success, "fake HTTP delivery failed");
        }

        public void Dispose() => client.Dispose();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckAlert(Harness h, params string[] keywords)
    {
        Check(h.Handler.Bodies.Count == 1, "expected exactly one Discord HTTP request");
        using var json = JsonDocument.Parse(h.Handler.Bodies.Single());
        var content = json.RootElement.GetProperty("content").GetString()!;
        Check(content.Contains("Keyword alert:", StringComparison.Ordinal), "keyword highlighting missing");
        foreach (var keyword in keywords)
            Check(content.Contains($"\"{keyword}\"", StringComparison.Ordinal), "matching keyword missing from alert");
        CheckOnePing(h);
    }

    private static void CheckOnePing(Harness h)
    {
        Check(h.Handler.Bodies.Count == 1, "expected exactly one Discord HTTP request");
        using var json = JsonDocument.Parse(h.Handler.Bodies.Single());
        var content = json.RootElement.GetProperty("content").GetString()!;
        Check(content.Split($"<@{UserId}>", StringSplitOptions.None).Length == 2, "expected one configured-user mention");
        var mentions = json.RootElement.GetProperty("allowed_mentions");
        Check(mentions.GetProperty("parse").GetArrayLength() == 0, "broad mentions enabled");
        Check(mentions.GetProperty("users").EnumerateArray().Select(x => x.GetString()).SequenceEqual([UserId]),
            "wrong allowed user");
    }

    private static async Task KeywordOnly(bool embeds)
    {
        using var h = new Harness();
        await h.Capture(Profile(embeds), Chat("Are you READY?"));
        CheckAlert(h, "ready");
    }

    private static async Task Nonmatching(bool embeds)
    {
        using var h = new Harness();
        await h.Capture(Profile(embeds), Chat("good morning FC"));
        Check(h.Handler.Bodies.Count == 0, "nonmatching disabled-channel chat leaked");
    }

    private static async Task Normal(bool embeds)
    {
        using var h = new Harness();
        await h.Capture(Profile(embeds, true), Chat("good morning FC"));
        Check(h.Handler.Bodies.Count == 1, "normal forwarding failed");
        Check(!h.Handler.Bodies.Single().Contains("Keyword alert:", StringComparison.Ordinal), "unexpected keyword alert");
    }

    private static async Task NormalAndKeyword(bool embeds)
    {
        using var h = new Harness();
        var profile = Profile(embeds, true);
        await h.Capture(profile, Chat("Ready for the hunt?"));
        await h.Capture(profile, Chat("Ready for the hunt?"), Now.AddMilliseconds(100));
        CheckAlert(h, "ready");
    }

    private static async Task WrongChannel(bool embeds)
    {
        using var h = new Harness();
        var profile = Profile(embeds);
        profile.Keywords.Single().Channels = [RelayChatType.Say];
        await h.Capture(profile, Chat("ready"));
        Check(h.Handler.Bodies.Count == 0, "keyword escaped its saved channel list");
    }

    private static async Task Paused(bool embeds)
    {
        using var h = new Harness();
        var profile = Profile(embeds);
        profile.Paused = true;
        await h.Capture(profile, Chat("ready"));
        Check(!InboundChatRelay.TryPrepare(profile, Chat("ready"), out _), "queue bypassed pause");
        profile.Paused = false;
        profile.Keywords.Single().Channels.Add(RelayChatType.RewardsHuntResults);
        await h.Capture(profile, Chat("ready", RelayChatType.RewardsHuntResults));
        profile.Paused = true;
        await h.Flush(profile);
        Check(h.Handler.Bodies.Count == 0, "reward batch bypassed pause at flush");
        profile.Paused = false;
        await h.Capture(profile, Chat("ready"));
        CheckAlert(h, "ready");
    }

    private static async Task MultipleKeywords(bool embeds)
    {
        using var h = new Harness();
        var profile = Profile(embeds);
        profile.Keywords.Add(Rule("bot", RelayChatType.FreeCompany, wholeWord: true));
        await h.Capture(profile, Chat("READY, is the bot working?"));
        CheckAlert(h, "ready", "bot");
    }

    private static async Task MissingRoute(bool embeds)
    {
        using var h = new Harness();
        var profile = Profile(embeds);
        profile.ProtectedWebhookUrl = string.Empty;
        await h.Capture(profile, Chat("ready"));
        await h.Capture(null, Chat("ready"));
        Check(!InboundChatRelay.TryPrepare(profile, Chat("ready"), out _), "queue bypassed webhook requirement");
        Check(!InboundChatRelay.TryPrepare(null, Chat("ready"), out _), "queue accepted a missing profile");
        Check(h.Handler.Bodies.Count == 0, "missing route delivered chat");
    }

    private static async Task EveryInboundChannel(bool embeds)
    {
        foreach (var channel in ChannelPolicy.InboundChannels)
        {
            using var h = new Harness();
            var profile = Profile(embeds);
            profile.Keywords.Single().Channels = [channel];
            await h.Capture(profile, Chat("good morning", channel));
            await h.Capture(profile, Chat("READY?", channel));
            await h.Flush(profile);
            // Stable Relay already alerts on every forwarded incoming tell.
            // Keep that formatter behavior while testing keyword-only admission.
            if (channel == RelayChatType.IncomingTell)
                CheckOnePing(h);
            else
                CheckAlert(h, "ready");
        }
    }

    private static async Task KeywordRewards(bool embeds)
    {
        using var h = new Harness();
        var profile = Profile(embeds);
        profile.Keywords = [Rule("seals", RelayChatType.RewardsHuntResults)];
        await h.Capture(profile, Chat("You obtain 60 Allied Seals.", RelayChatType.RewardsHuntResults));
        await h.Capture(profile, Chat("You obtain 60 Allied Seals.", RelayChatType.RewardsHuntResults), Now.AddMilliseconds(100));
        await h.Capture(profile, Chat("You gain 100 experience points.", RelayChatType.RewardsHuntResults), Now.AddMilliseconds(200));
        await h.Capture(profile, Chat("You obtain 40 Centurio Seals.", RelayChatType.RewardsHuntResults), Now.AddMilliseconds(300));
        await h.Flush(profile, Now.AddSeconds(1));
        Check(h.Handler.Bodies.Count == 0, "reward batch flushed before quiet period");
        await h.Flush(profile);
        await h.Flush(profile);
        CheckAlert(h, "seals");
        using var json = JsonDocument.Parse(h.Handler.Bodies.Single());
        Check(json.RootElement.GetProperty("embeds")[0].GetProperty("description").GetString()
              == "You obtain 60 Allied Seals.\nYou obtain 40 Centurio Seals.", "reward filtering, deduplication or ordering failed");
    }

    private static async Task NormalRewards(bool embeds)
    {
        using var h = new Harness();
        var profile = Profile(embeds);
        profile.EnabledInboundChannels.Add(RelayChatType.RewardsHuntResults);
        profile.Keywords = [Rule("seals", RelayChatType.RewardsHuntResults)];
        await h.Capture(profile, Chat("You gain 100 experience points.", RelayChatType.RewardsHuntResults));
        await h.Capture(profile, Chat("You obtain 60 Allied Seals.", RelayChatType.RewardsHuntResults), Now.AddMilliseconds(100));
        await h.Flush(profile);
        CheckAlert(h, "seals");
        using var json = JsonDocument.Parse(h.Handler.Bodies.Single());
        Check(json.RootElement.GetProperty("embeds")[0].GetProperty("description").GetString()
              == "You gain 100 experience points.\nYou obtain 60 Allied Seals.", "normal reward batching changed");
    }

    private static async Task RewardOrder(bool embeds)
    {
        using var h = new Harness();
        var profile = Profile(embeds);
        profile.Keywords.Single().Channels.Add(RelayChatType.RewardsHuntResults);
        await h.Capture(profile, Chat("ready reward", RelayChatType.RewardsHuntResults));
        await h.Capture(profile, Chat("ready chat"), Now.AddMilliseconds(100));
        await h.Flush(profile);
        Check(h.Handler.Bodies.Count == 2, "batch or chat duplicated/dropped");
        Check(h.Handler.Bodies[0].Contains("ready reward", StringComparison.Ordinal)
              && h.Handler.Bodies[1].Contains("ready chat", StringComparison.Ordinal), "event order changed");
    }

    private static async Task CharacterChange(bool embeds)
    {
        using var h = new Harness();
        var first = Profile(embeds);
        first.Keywords.Single().Channels.Add(RelayChatType.RewardsHuntResults);
        await h.Capture(first, Chat("ready reward", RelayChatType.RewardsHuntResults));
        await h.Capture(first, Chat("ready"));
        h.Handler.Bodies.Clear();
        h.Relay.Clear();
        var second = Profile(embeds);
        second.CharacterKey = "cid:two";
        second.Keywords.Clear();
        await h.Flush(second);
        await h.Capture(second, Chat("ready"), Now.AddMilliseconds(200));
        Check(h.Handler.Bodies.Count == 0, "keyword or pending reward leaked across profiles");
        second.Keywords.Add(Rule("ready", RelayChatType.FreeCompany));
        await h.Capture(second, Chat("ready"), Now.AddMilliseconds(300));
        CheckAlert(h, "ready");
    }

    private static Task ConfigurationRoundTrip(bool embeds)
    {
        var config = new Configuration { Version = Configuration.CurrentVersion };
        var profile = Profile(embeds);
        profile.Keywords.Add(Rule("bot", RelayChatType.Say, wholeWord: true));
        profile.Keywords[0].Channels.Add(RelayChatType.RewardsHuntResults);
        profile.Keywords[0].PingDiscordUser = false;
        config.CharacterProfiles[profile.CharacterKey] = profile;
        var restored = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;
        restored.NormalizeAndMigrate();
        var saved = restored.CharacterProfiles[profile.CharacterKey];
        Check(saved.EnabledInboundChannels.Count == 0, "keyword channels enabled normal filters during migration");
        Check(saved.Keywords.Count == profile.Keywords.Count, "saved rules were lost");
        foreach (var rule in profile.Keywords)
        {
            var actual = saved.Keywords.Single(x => x.Id == rule.Id);
            Check(actual.Keyword == rule.Keyword && actual.WholeWord == rule.WholeWord
                  && actual.PingDiscordUser == rule.PingDiscordUser && actual.Channels.SetEquals(rule.Channels),
                "saved keyword settings changed");
        }
        return Task.CompletedTask;
    }
}
