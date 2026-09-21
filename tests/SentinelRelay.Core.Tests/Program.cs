using System.Net;
using System.Text.Json;
using SentinelRelay;
using SentinelRelay.Core;
using SentinelRelay.Models;
using SentinelRelay.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("all chat filters default off", Sync(FiltersAreOptIn)),
    ("enabled chat is forwarded", Sync(EnabledChatIsForwarded)),
    ("disabled chat is never forwarded", Sync(DisabledChatIsNotForwarded)),
    ("pause blocks forwarding and resume restores it", Sync(PauseResumeWorks)),
    ("character profiles keep independent webhook routes", Sync(CharacterProfilesAreIsolated)),
    ("configuration selects the correct character profile", Sync(ConfigurationSelectsCharacterProfile)),
    ("chat labels distinguish tell and cross-world party", Sync(ChatLabelsAreExplicit)),
    ("SeString plain text sanitation removes control data", Sync(SanitizerRemovesControlData)),
    ("Discord mention sanitation neutralizes mentions", Sync(MentionSanitizationWorks)),
    ("keyword matching is case-insensitive and channel-scoped", Sync(KeywordMatchingWorks)),
    ("explicit keyword alert can ping only configured user", Sync(KeywordPingIsExplicit)),
    ("duplicate filter suppresses immediate duplicates", Sync(DuplicateFilterWorks)),
    ("bounded queue preserves order", Sync(QueuePreservesOrder)),
    ("bounded queue rejects overflow", Sync(QueueRejectsOverflow)),
    ("formatter splits long Discord messages safely", Sync(LongMessagesAreSplit)),
    ("embed formatter includes channel color and timestamp", Sync(EmbedFormattingWorks)),
    ("malformed and non-Discord webhooks are rejected", Sync(MalformedWebhooksAreRejected)),
    ("valid Discord webhook is normalized with wait=true", Sync(ValidWebhookIsNormalized)),
    ("Discord allowed_mentions is always empty", Sync(AllowedMentionsAreDisabled)),
    ("429 response is delayed and retried", RateLimitIsRetried),
    ("webhook test reports successful delivery", WebhookTestSucceeds),
    ("configuration model survives serialization", Sync(ConfigurationModelPersists)),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {test.Name}: {ex.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static Func<Task> Sync(Action action) => () =>
{
    action();
    return Task.CompletedTask;
};

static void FiltersAreOptIn()
{
    var profile = new CharacterProfile();
    Assert(profile.EnabledInboundChannels.Count == 0, "new filter set was not empty");
    Assert(!profile.EnabledInboundChannels.Contains(RelayChatType.FreeCompany), "FC defaulted on");
    Assert(!profile.EnabledInboundChannels.Contains(RelayChatType.IncomingTell), "incoming Tell defaulted on");
    Assert(!profile.EnabledInboundChannels.Contains(RelayChatType.OutgoingTell), "outgoing Tell defaulted on");
}

static void EnabledChatIsForwarded()
{
    var profile = ConfiguredProfile(RelayChatType.FreeCompany);
    Assert(RelayFilter.ShouldForward(profile, RelayChatType.FreeCompany), "enabled FC was blocked");
}

static void DisabledChatIsNotForwarded()
{
    var profile = ConfiguredProfile(RelayChatType.FreeCompany);
    Assert(!RelayFilter.ShouldForward(profile, RelayChatType.Shout), "disabled Shout was forwarded");
}

static void PauseResumeWorks()
{
    var profile = ConfiguredProfile(RelayChatType.Say);
    profile.Paused = true;
    Assert(!RelayFilter.ShouldForward(profile, RelayChatType.Say), "paused profile forwarded chat");
    profile.Paused = false;
    Assert(RelayFilter.ShouldForward(profile, RelayChatType.Say), "resumed profile remained blocked");
}

static void CharacterProfilesAreIsolated()
{
    var profiles = new Dictionary<string, CharacterProfile>
    {
        ["cid:wrothy"] = new() { CharacterKey = "cid:wrothy", ProtectedWebhookUrl = "protected-wrothy" },
        ["cid:elektra"] = new() { CharacterKey = "cid:elektra", ProtectedWebhookUrl = "protected-elektra" },
    };
    profiles["cid:wrothy"].EnabledInboundChannels.Add(RelayChatType.FreeCompany);
    profiles["cid:elektra"].EnabledInboundChannels.Add(RelayChatType.Party);

    Assert(profiles["cid:wrothy"].ProtectedWebhookUrl == "protected-wrothy", "Wrothy route changed");
    Assert(profiles["cid:elektra"].ProtectedWebhookUrl == "protected-elektra", "Elektra route changed");
    Assert(!profiles["cid:elektra"].EnabledInboundChannels.Contains(RelayChatType.FreeCompany), "filters leaked across profiles");
}

static void ConfigurationSelectsCharacterProfile()
{
    var configuration = new Configuration();
    var wrothy = configuration.GetOrCreateProfile("cid:wrothy", "Wrothy Minioa", "Example World");
    var elektra = configuration.GetOrCreateProfile("cid:elektra", "Elektra Minoa", "Example World");
    wrothy.ProtectedWebhookUrl = "protected-wrothy";
    elektra.ProtectedWebhookUrl = "protected-elektra";
    wrothy.EnabledInboundChannels.Add(RelayChatType.FreeCompany);
    elektra.EnabledInboundChannels.Add(RelayChatType.Party);

    var selectedWrothy = configuration.GetOrCreateProfile("cid:wrothy", "Wrothy Minioa", "Example World");
    var selectedElektra = configuration.GetOrCreateProfile("cid:elektra", "Elektra Minoa", "Example World");
    Assert(ReferenceEquals(wrothy, selectedWrothy), "Wrothy profile was not selected by content ID");
    Assert(ReferenceEquals(elektra, selectedElektra), "Elektra profile was not selected by content ID");
    Assert(selectedWrothy.ProtectedWebhookUrl == "protected-wrothy", "Wrothy webhook route changed");
    Assert(selectedElektra.ProtectedWebhookUrl == "protected-elektra", "Elektra webhook route changed");
    Assert(!selectedElektra.EnabledInboundChannels.Contains(RelayChatType.FreeCompany), "Wrothy filter leaked to Elektra");
}

static void ChatLabelsAreExplicit()
{
    Assert(ChannelPolicy.GetLabel(RelayChatType.IncomingTell) == "Incoming Tell", "incoming Tell label");
    Assert(ChannelPolicy.GetLabel(RelayChatType.OutgoingTell) == "Outgoing Tell", "outgoing Tell label");
    Assert(ChannelPolicy.GetLabel(RelayChatType.CrossWorldParty) == "Cross-world Party", "cross-party label");
    Assert(ChannelPolicy.GetShortLabel(RelayChatType.FreeCompany) == "FC", "FC short label");
}

static void SanitizerRemovesControlData()
{
    var sanitized = MessageSanitizer.SanitizePlainText(" hello\r\n\tworld\0  again ");
    Assert(sanitized == "hello world again", $"unexpected sanitized value: {sanitized}");
}

static void MentionSanitizationWorks()
{
    var sanitized = MessageSanitizer.SanitizeForDiscord("@everyone @HERE <@123> <@&456> <#789>");
    Assert(!sanitized.Contains("@everyone", StringComparison.OrdinalIgnoreCase), "everyone mention survived");
    Assert(!sanitized.Contains("@here", StringComparison.OrdinalIgnoreCase), "here mention survived");
    Assert(!sanitized.Contains("<@123>", StringComparison.Ordinal), "user mention survived");
    Assert(!sanitized.Contains("<@&456>", StringComparison.Ordinal), "role mention survived");
    Assert(!sanitized.Contains("<#789>", StringComparison.Ordinal), "channel mention survived");
}

static void KeywordMatchingWorks()
{
    var rules = new[]
    {
        new KeywordRule
        {
            Keyword = "Wrothy",
            Channels = [RelayChatType.FreeCompany],
        },
        new KeywordRule
        {
            Keyword = "raid",
            WholeWord = true,
            Channels = [RelayChatType.Party],
        },
    };
    Assert(KeywordMatcher.FindMatches(rules, RelayChatType.FreeCompany, "Has anyone seen WROTHY?").Count == 1,
        "case-insensitive keyword did not match");
    Assert(KeywordMatcher.FindMatches(rules, RelayChatType.Shout, "Wrothy").Count == 0,
        "keyword ignored channel scope");
    Assert(!KeywordMatcher.IsMatch(rules[1], "raider"), "whole-word rule matched a partial word");
}

static void KeywordPingIsExplicit()
{
    var rule = new KeywordRule
    {
        Keyword = "Wrothy",
        PingDiscordUser = true,
        Channels = [RelayChatType.FreeCompany],
    };
    var payload = WebhookMessageFormatter.Format(
        SampleChat("Wrothy are you coming?"),
        includeWorld: true,
        useEmbeds: false,
        [rule],
        "123456789012345678").First();
    Assert(payload.Content?.Contains("<@123456789012345678>", StringComparison.Ordinal) == true, "explicit ping missing");
    Assert(payload.AllowedMentions.Users?.SequenceEqual(["123456789012345678"]) == true, "wrong allowed user");
    Assert(payload.AllowedMentions.Parse.Count == 0, "broad mentions were enabled");
}

static void DuplicateFilterWorks()
{
    var filter = new DuplicateMessageFilter(TimeSpan.FromSeconds(2));
    var chat = SampleChat("same message");
    var now = DateTime.UtcNow;
    Assert(!filter.IsDuplicate(chat, now), "first message was rejected");
    Assert(filter.IsDuplicate(chat, now.AddSeconds(1)), "immediate duplicate was accepted");
    Assert(!filter.IsDuplicate(chat, now.AddSeconds(3)), "expired duplicate was rejected");
}

static void QueuePreservesOrder()
{
    var queue = new BoundedOrderedQueue<int>(3);
    Assert(queue.TryEnqueue(1) && queue.TryEnqueue(2) && queue.TryEnqueue(3), "enqueue failed");
    Assert(queue.TryDequeue(out var first) && first == 1, "first item was reordered");
    Assert(queue.TryDequeue(out var second) && second == 2, "second item was reordered");
    Assert(queue.TryDequeue(out var third) && third == 3, "third item was reordered");
}

static void QueueRejectsOverflow()
{
    var queue = new BoundedOrderedQueue<int>(2);
    Assert(queue.TryEnqueue(1), "first enqueue failed");
    Assert(queue.TryEnqueue(2), "second enqueue failed");
    Assert(!queue.TryEnqueue(3), "overflow was accepted");
    Assert(queue.Count == 2, "overflow changed queue size");
}

static void LongMessagesAreSplit()
{
    var chat = SampleChat(new string('a', 5000));
    var payloads = WebhookMessageFormatter.Format(chat, includeWorld: true, useEmbeds: false);
    Assert(payloads.Count > 1, "long message was not split");
    Assert(payloads.All(payload => payload.Content?.Length <= ChannelPolicy.DiscordContentLimit), "content exceeded Discord limit");
}

static void EmbedFormattingWorks()
{
    var chat = SampleChat("maps tonight?");
    var payload = WebhookMessageFormatter.Format(chat, includeWorld: true, useEmbeds: true).Single();
    var embed = payload.Embeds?.Single() ?? throw new InvalidOperationException("embed missing");
    Assert(embed.Title.Contains("【FC】", StringComparison.Ordinal), "channel label missing");
    Assert(embed.Title.Contains("Example World", StringComparison.Ordinal), "world missing");
    Assert(embed.Color == ChannelPolicy.GetDiscordColor(RelayChatType.FreeCompany), "channel color mismatch");
    Assert(embed.Timestamp.Kind == DateTimeKind.Utc, "timestamp was not UTC");
}

static void MalformedWebhooksAreRejected()
{
    Assert(!WebhookEndpoint.TryCreate("https://example.com/api/webhooks/123/token", out _, out _), "foreign host accepted");
    Assert(!WebhookEndpoint.TryCreate("http://discord.com/api/webhooks/123456789012345678/token", out _, out _), "HTTP accepted");
    Assert(!WebhookEndpoint.TryCreate("https://discord.com/channels/1/2", out _, out _), "channel URL accepted");
}

static void ValidWebhookIsNormalized()
{
    Assert(WebhookEndpoint.TryCreate(FakeWebhook(), out var endpoint, out var error), error);
    Assert(endpoint.Query == "?wait=true", "wait query missing");
    Assert(WebhookEndpoint.Mask(endpoint).Contains("…", StringComparison.Ordinal), "masked label missing");
    Assert(!WebhookEndpoint.Mask(endpoint).Contains("ABCDEFGHIJKLMNOPQRSTUVWXYZ", StringComparison.Ordinal), "token leaked in mask");
}

static void AllowedMentionsAreDisabled()
{
    var payload = WebhookMessageFormatter.Format(SampleChat("hello"), true, false).Single();
    var json = JsonSerializer.Serialize(payload);
    using var document = JsonDocument.Parse(json);
    var parse = document.RootElement.GetProperty("allowed_mentions").GetProperty("parse");
    Assert(parse.GetArrayLength() == 0, "allowed mention parse list was not empty");
}

static async Task RateLimitIsRetried()
{
    var handler = new SequenceHandler(
        () => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"retry_after\":0.25}"),
        },
        () => new HttpResponseMessage(HttpStatusCode.NoContent));
    using var client = new HttpClient(handler);
    var delays = new List<TimeSpan>();
    var sender = new WebhookHttpSender(client, (duration, _) =>
    {
        delays.Add(duration);
        return Task.CompletedTask;
    });
    WebhookEndpoint.TryCreate(FakeWebhook(), out var endpoint, out _);
    var result = await sender.SendAsync(endpoint, [WebhookMessageFormatter.TestMessage("Wrothy Minioa")], CancellationToken.None);

    Assert(result.Success, result.Error ?? "retry failed");
    Assert(result.Attempts == 2, "unexpected retry count");
    Assert(result.WasRateLimited, "rate-limit flag missing");
    Assert(delays.Count == 1 && delays[0] >= TimeSpan.FromMilliseconds(100), "retry delay was not honored");
}

static async Task WebhookTestSucceeds()
{
    var handler = new SequenceHandler(() => new HttpResponseMessage(HttpStatusCode.NoContent));
    using var client = new HttpClient(handler);
    var sender = new WebhookHttpSender(client, (_, _) => Task.CompletedTask);
    WebhookEndpoint.TryCreate(FakeWebhook(), out var endpoint, out _);
    var result = await sender.SendAsync(endpoint, [WebhookMessageFormatter.TestMessage("Wrothy Minioa")], CancellationToken.None);

    Assert(result.Success, result.Error ?? "test failed");
    Assert(handler.Bodies.Single().Contains("connected successfully for Wrothy Minioa", StringComparison.Ordinal), "test content missing");
}

static void ConfigurationModelPersists()
{
    var original = new Configuration();
    var wrothy = original.GetOrCreateProfile("cid:ABC", "Wrothy Minioa", "Example World");
    wrothy.ProtectedWebhookUrl = "dpapi-ciphertext-wrothy";
    wrothy.IncludeSenderWorld = false;
    wrothy.UseDiscordEmbeds = true;
    wrothy.Paused = true;
    wrothy.EnabledInboundChannels = [RelayChatType.FreeCompany, RelayChatType.Shout];
    wrothy.DiscordMentionUserId = "123456789012345678";
    wrothy.Keywords = [new KeywordRule { Keyword = "Wrothy", Channels = [RelayChatType.FreeCompany] }];
    var elektra = original.GetOrCreateProfile("cid:DEF", "Elektra Minoa", "Example World");
    elektra.ProtectedWebhookUrl = "dpapi-ciphertext-elektra";
    elektra.EnabledInboundChannels = [RelayChatType.Party];

    var json = JsonSerializer.Serialize(original);
    var restored = JsonSerializer.Deserialize<Configuration>(json)
        ?? throw new InvalidOperationException("deserialization failed");
    var restoredWrothy = restored.CharacterProfiles["cid:ABC"];
    var restoredElektra = restored.CharacterProfiles["cid:DEF"];
    Assert(restoredWrothy.ProtectedWebhookUrl == wrothy.ProtectedWebhookUrl, "Wrothy protected webhook was lost");
    Assert(restoredElektra.ProtectedWebhookUrl == elektra.ProtectedWebhookUrl, "Elektra protected webhook was lost");
    Assert(restoredWrothy.EnabledInboundChannels.SetEquals(wrothy.EnabledInboundChannels), "Wrothy filters were lost");
    Assert(restoredElektra.EnabledInboundChannels.SetEquals(elektra.EnabledInboundChannels), "Elektra filters were lost");
    Assert(restoredWrothy.Paused && restoredWrothy.UseDiscordEmbeds && !restoredWrothy.IncludeSenderWorld, "preferences were lost");
    Assert(restoredWrothy.Keywords.Single().Keyword == "Wrothy", "keyword was lost");
}

static CharacterProfile ConfiguredProfile(params RelayChatType[] channels) => new()
{
    ProtectedWebhookUrl = "protected-value",
    EnabledInboundChannels = [.. channels],
};

static string FakeWebhook() =>
    $"https://discord.com/api/webhooks/{new string('1', 18)}/{new string('A', 64)}";

static CapturedChat SampleChat(string message) => new(
    RelayChatType.FreeCompany,
    "Example Player",
    "Example World",
    message,
    DateTime.SpecifyKind(new DateTime(2026, 9, 21, 12, 0, 0), DateTimeKind.Utc));

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class SequenceHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> responses = new(responses);

    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Bodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));
        if (!responses.TryDequeue(out var response))
            throw new InvalidOperationException("No mock response remains.");
        return response();
    }
}
