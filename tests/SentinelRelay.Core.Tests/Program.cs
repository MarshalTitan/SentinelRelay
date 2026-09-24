using System.Net;
using System.Buffers.Binary;
using System.Text.Json;
using SentinelRelay;
using SentinelRelay.Core;
using SentinelRelay.Models;
using SentinelRelay.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("all chat filters default off", Sync(FiltersAreOptIn)),
    ("Rewards / Hunt Results defaults off and is inbound-only", Sync(RewardsAreOptInAndInboundOnly)),
    ("reward classifier requires both system LogKind and reward text", Sync(RewardClassifierIsNarrow)),
    ("consecutive reward lines batch in original order", Sync(RewardBatchingPreservesOrder)),
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
    ("embed formatter is compact and omits a duplicate timestamp", Sync(EmbedFormattingWorks)),
    ("reward embed is compact, multiline, sender-free, and timestamp-free", Sync(RewardEmbedFormattingWorks)),
    ("/fc parser preserves message text", Sync(FreeCompanyCommandParses)),
    ("explicit chat command allowlist maps every supported destination", Sync(OutboundCommandsParse)),
    ("Tell reply requires a recent incoming Tell", Sync(TellReplyRequiresRecentTarget)),
    ("empty and unknown Discord commands are rejected", Sync(InvalidReplyCommandsAreRejected)),
    ("authorized /fc command is accepted", Sync(AuthorizedReplyIsAccepted)),
    ("wrong channel is rejected", Sync(WrongReplyChannelIsRejected)),
    ("wrong Discord user is rejected", Sync(WrongReplyUserIsRejected)),
    ("bot and webhook messages are rejected", Sync(AutomatedReplySourcesAreRejected)),
    ("wrong FFXIV character is rejected", Sync(WrongCharacterIsRejected)),
    ("disabled replies are rejected", Sync(DisabledRepliesAreRejected)),
    ("paused replies are rejected", Sync(PausedRepliesAreRejected)),
    ("duplicate and stale commands are rejected", Sync(ReplayAndStaleRepliesAreRejected)),
    ("outbound allowlist blocks unapproved destinations", Sync(OutboundAllowlistIsRequired)),
    ("local reply rate limit is enforced", Sync(ReplyRateLimitIsEnforced)),
    ("/screenshot is an isolated exact control command", Sync(ScreenshotCommandIsIsolated)),
    ("authorized screenshot command is accepted", Sync(AuthorizedScreenshotIsAccepted)),
    ("screenshot authorization rejects wrong routes and automated authors", Sync(ScreenshotAuthorizationIsIsolated)),
    ("screenshots require explicit opt-in and obey cooldown", Sync(ScreenshotOptInAndCooldownAreEnforced)),
    ("screenshot replay and stale protection match Discord replies", Sync(ScreenshotReplayAndStaleAreRejected)),
    ("in-memory PNG encoder produces bounded valid dimensions", Sync(PngEncodingWorks)),
    ("malformed and non-Discord webhooks are rejected", Sync(MalformedWebhooksAreRejected)),
    ("valid Discord webhook is normalized with wait=true", Sync(ValidWebhookIsNormalized)),
    ("Discord allowed_mentions is always empty", Sync(AllowedMentionsAreDisabled)),
    ("429 response is delayed and retried", RateLimitIsRetried),
    ("Discord reader honors 429 and preserves message order data", DiscordReaderRateLimitIsRetried),
    ("webhook test reports successful delivery", WebhookTestSucceeds),
    ("screenshot webhook upload is multipart and mention-safe", ScreenshotUploadIsMultipartAndSafe),
    ("screenshot webhook honors Discord rate limits", ScreenshotUploadRateLimitIsRetried),
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
    Assert(!profile.EnabledInboundChannels.Contains(RelayChatType.RewardsHuntResults), "rewards defaulted on");
    Assert(profile.EnabledOutboundChannels.Count == 0, "outbound destinations defaulted on");
    Assert(!profile.RemoteScreenshotsEnabled, "remote screenshots defaulted on");
}

static void RewardsAreOptInAndInboundOnly()
{
    var profile = ConfiguredProfile(RelayChatType.RewardsHuntResults);
    Assert(RelayFilter.ShouldForward(profile, RelayChatType.RewardsHuntResults), "enabled rewards were blocked");
    Assert(!ChannelPolicy.ImplementedOutboundChannels.Contains(RelayChatType.RewardsHuntResults),
        "rewards appeared in the outbound allowlist");
    var threw = false;
    try
    {
        _ = ChannelPolicy.GetCommandPrefix(RelayChatType.RewardsHuntResults);
    }
    catch (InvalidOperationException)
    {
        threw = true;
    }
    Assert(threw, "rewards received an outbound game command prefix");
}

static void RewardClassifierIsNarrow()
{
    const string credit = "You have been rewarded for your contribution in slaying the mark.";
    Assert(RewardMessageClassifier.TryClassify(57, credit, out var kind)
           && kind == RewardLineKind.HuntCredit, "SystemMessage hunt credit was not recognized");
    Assert(RewardMessageClassifier.TryClassify(62, "You obtain 60 Allied Seals.", out kind)
           && kind == RewardLineKind.ObtainedReward, "LootNotice reward was not recognized");
    Assert(RewardMessageClassifier.TryClassify(58, "You cannot carry any more Allagan tomestones of poetics.", out kind)
           && kind == RewardLineKind.CurrencyCapped, "capped currency line was not recognized");
    Assert(!RewardMessageClassifier.TryClassify(10, credit, out _),
        "player chat was allowed to impersonate a reward system line");
    Assert(!RewardMessageClassifier.TryClassify(57, "The weather has changed.", out _),
        "unrelated system text was recognized as a reward");
}

static void RewardBatchingPreservesOrder()
{
    var batcher = new RewardMessageBatcher(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
    var now = DateTime.SpecifyKind(new DateTime(2026, 9, 22, 12, 0, 0), DateTimeKind.Utc);
    var first = RewardChat("You have been rewarded for your contribution in slaying the mark.");
    var second = RewardChat("You obtain 60 Allied Seals.");
    Assert(batcher.Add(first, now) is null, "batch flushed after its first line");
    Assert(batcher.Add(second, now.AddMilliseconds(200)) is null, "batch flushed before the quiet window");
    Assert(batcher.FlushIfDue(now.AddMilliseconds(900)) is null, "batch flushed too early");
    var combined = batcher.FlushIfDue(now.AddSeconds(2));
    Assert(combined is not null, "batch did not flush after the quiet window");
    Assert(combined.Message == first.Message + "\n" + second.Message, "reward line order changed");
    Assert(batcher.Count == 0, "flushed reward lines remained queued");
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
        ["cid:one"] = new() { CharacterKey = "cid:one", ProtectedWebhookUrl = "protected-one" },
        ["cid:two"] = new() { CharacterKey = "cid:two", ProtectedWebhookUrl = "protected-two" },
    };
    profiles["cid:one"].EnabledInboundChannels.Add(RelayChatType.FreeCompany);
    profiles["cid:two"].EnabledInboundChannels.Add(RelayChatType.Party);

    Assert(profiles["cid:one"].ProtectedWebhookUrl == "protected-one", "first route changed");
    Assert(profiles["cid:two"].ProtectedWebhookUrl == "protected-two", "second route changed");
    Assert(!profiles["cid:two"].EnabledInboundChannels.Contains(RelayChatType.FreeCompany), "filters leaked across profiles");
}

static void ConfigurationSelectsCharacterProfile()
{
    var configuration = new Configuration();
    var first = configuration.GetOrCreateProfile("cid:one", "First Character", "Example World");
    var second = configuration.GetOrCreateProfile("cid:two", "Second Character", "Example World");
    first.ProtectedWebhookUrl = "protected-one";
    second.ProtectedWebhookUrl = "protected-two";
    first.EnabledInboundChannels.Add(RelayChatType.FreeCompany);
    second.EnabledInboundChannels.Add(RelayChatType.Party);

    var selectedFirst = configuration.GetOrCreateProfile("cid:one", "First Character", "Example World");
    var selectedSecond = configuration.GetOrCreateProfile("cid:two", "Second Character", "Example World");
    Assert(ReferenceEquals(first, selectedFirst), "first profile was not selected by content ID");
    Assert(ReferenceEquals(second, selectedSecond), "second profile was not selected by content ID");
    Assert(selectedFirst.ProtectedWebhookUrl == "protected-one", "first webhook route changed");
    Assert(selectedSecond.ProtectedWebhookUrl == "protected-two", "second webhook route changed");
    Assert(!selectedSecond.EnabledInboundChannels.Contains(RelayChatType.FreeCompany), "first filter leaked to second profile");
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
            Keyword = "ready",
            Channels = [RelayChatType.FreeCompany],
        },
        new KeywordRule
        {
            Keyword = "raid",
            WholeWord = true,
            Channels = [RelayChatType.Party],
        },
    };
    Assert(KeywordMatcher.FindMatches(rules, RelayChatType.FreeCompany, "Is everyone READY?").Count == 1,
        "case-insensitive keyword did not match");
    Assert(KeywordMatcher.FindMatches(rules, RelayChatType.Shout, "ready").Count == 0,
        "keyword ignored channel scope");
    Assert(!KeywordMatcher.IsMatch(rules[1], "raider"), "whole-word rule matched a partial word");
}

static void KeywordPingIsExplicit()
{
    var rule = new KeywordRule
    {
        Keyword = "ready",
        PingDiscordUser = true,
        Channels = [RelayChatType.FreeCompany],
    };
    var payload = WebhookMessageFormatter.Format(
        SampleChat("Are you ready?"),
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
    Assert(embed.Description == "maps tonight?", "message body changed");
    Assert(embed.Color == ChannelPolicy.GetDiscordColor(RelayChatType.FreeCompany), "channel color mismatch");
    var json = JsonSerializer.Serialize(payload);
    Assert(!json.Contains("\"timestamp\"", StringComparison.OrdinalIgnoreCase), "embed timestamp was serialized");
    Assert(!json.Contains("\"footer\"", StringComparison.OrdinalIgnoreCase), "timestamp footer was substituted");
}

static void RewardEmbedFormattingWorks()
{
    var chat = RewardChat(
        "You have been rewarded for your contribution in slaying the mark.\n"
        + "You obtain 60 Allied Seals.\n"
        + "You cannot carry any more Allagan tomestones of poetics.");
    var payload = WebhookMessageFormatter.Format(chat, includeWorld: true, useEmbeds: true).Single();
    var embed = payload.Embeds?.Single() ?? throw new InvalidOperationException("reward embed missing");
    Assert(embed.Title == "【REWARD】", "reward title included sender noise");
    Assert(embed.Description.Contains('\n'), "reward line breaks were removed");
    Assert(!embed.Title.Contains("FFXIV", StringComparison.Ordinal), "reward sender was displayed");
    var json = JsonSerializer.Serialize(payload);
    Assert(!json.Contains("\"timestamp\"", StringComparison.OrdinalIgnoreCase), "reward timestamp was serialized");
    Assert(!json.Contains("\"footer\"", StringComparison.OrdinalIgnoreCase), "reward timestamp footer was substituted");
}

static void FreeCompanyCommandParses()
{
    Assert(DiscordReplyCommandParser.TryParse(
        "/fc I'll be there in about 5 minutes!",
        out var destination,
        out var message), "valid /fc command was rejected");
    Assert(destination == RelayChatType.FreeCompany, "wrong destination");
    Assert(message == "I'll be there in about 5 minutes!", "spaces or punctuation changed");
}

static void OutboundCommandsParse()
{
    var expected = new Dictionary<string, RelayChatType>
    {
        ["/say hello"] = RelayChatType.Say,
        ["/s hello"] = RelayChatType.Say,
        ["/yell hello"] = RelayChatType.Yell,
        ["/y hello"] = RelayChatType.Yell,
        ["/shout hello"] = RelayChatType.Shout,
        ["/sh hello"] = RelayChatType.Shout,
        ["/r hello"] = RelayChatType.IncomingTell,
        ["/party hello"] = RelayChatType.Party,
        ["/p hello"] = RelayChatType.Party,
        ["/alliance hello"] = RelayChatType.Alliance,
        ["/a hello"] = RelayChatType.Alliance,
        ["/fc hello"] = RelayChatType.FreeCompany,
        ["/pvpteam hello"] = RelayChatType.PvPTeam,
        ["/novice hello"] = RelayChatType.NoviceNetwork,
        ["/n hello"] = RelayChatType.NoviceNetwork,
    };
    for (var slot = 1; slot <= 8; slot++)
    {
        expected[$"/ls{slot} hello"] = (RelayChatType)((int)RelayChatType.Linkshell1 + slot - 1);
        expected[$"/cwls{slot} hello"] = (RelayChatType)((int)RelayChatType.CrossWorldLinkshell1 + slot - 1);
    }

    Assert(expected.Values.ToHashSet().SetEquals(ChannelPolicy.ImplementedOutboundChannels),
        "parser and implemented outbound destination sets differ");

    foreach (var pair in expected)
    {
        Assert(DiscordReplyCommandParser.TryParse(pair.Key, out var destination, out var message),
            $"supported command was rejected: {pair.Key}");
        Assert(destination == pair.Value, $"wrong destination for {pair.Key}: {destination}");
        Assert(message == "hello", $"message changed for {pair.Key}");
        Assert(ChannelPolicy.ImplementedOutboundChannels.Contains(destination), $"{destination} missing from implementation allowlist");
        Assert(ChannelPolicy.GetCommandPrefix(destination).StartsWith("/", StringComparison.Ordinal),
            $"{destination} has no fixed game prefix");
    }
}

static void TellReplyRequiresRecentTarget()
{
    var now = DateTime.UtcNow;
    var profile = ConfiguredReplyProfile();
    profile.EnabledOutboundChannels.Add(RelayChatType.IncomingTell);
    var source = ReplyMessage("100000000000000002", "/r yes, one moment", now);

    var accepted = DiscordReplyPolicy.TryAuthorize(
        profile,
        ActiveIdentity(),
        source,
        "100000000000000001",
        now,
        now.Subtract(TimeSpan.FromMinutes(1)),
        out var command,
        out var rejection);
    Assert(accepted, $"recent Tell reply was rejected: {rejection}");
    Assert(command?.Destination == RelayChatType.IncomingTell, "Tell reply used the wrong destination");

    accepted = DiscordReplyPolicy.TryAuthorize(
        profile,
        ActiveIdentity(),
        source,
        "100000000000000001",
        now,
        now.Subtract(TimeSpan.FromMinutes(31)),
        out _,
        out rejection);
    Assert(!accepted && rejection == DiscordReplyRejection.NoRecentTellTarget,
        "stale Tell target was accepted");
}

static void InvalidReplyCommandsAreRejected()
{
    Assert(!DiscordReplyCommandParser.TryParse("/fc", out _, out _), "empty /fc was accepted");
    Assert(!DiscordReplyCommandParser.TryParse("/fc   ", out _, out _), "blank /fc was accepted");
    Assert(!DiscordReplyCommandParser.TryParse("/fcwhatever hello", out _, out _), "prefix extension was accepted");
    Assert(!DiscordReplyCommandParser.TryParse("/logout now", out _, out _), "arbitrary command was accepted");
    Assert(!DiscordReplyCommandParser.TryParse("/tell Someone Else hello", out _, out _), "arbitrary Tell target was accepted");
    Assert(!DiscordReplyCommandParser.TryParse(" /fc hello", out _, out _), "non-prefix command was accepted");
}

static void AuthorizedReplyIsAccepted()
{
    var now = DateTime.UtcNow;
    var profile = ConfiguredReplyProfile();
    var accepted = DiscordReplyPolicy.TryAuthorize(
        profile,
        ActiveIdentity(),
        ReplyMessage("100000000000000002", "/fc hi", now),
        "100000000000000001",
        now,
        null,
        out var command,
        out var rejection);
    Assert(accepted, $"authorized command was rejected: {rejection}");
    Assert(command?.Destination == RelayChatType.FreeCompany && command.Message == "hi", "wrong parsed command");
}

static void WrongReplyChannelIsRejected()
{
    var now = DateTime.UtcNow;
    var source = ReplyMessage("100000000000000002", "/fc hi", now);
    source.ChannelId = "999999999999999999";
    AssertReplyRejected(source, now, DiscordReplyRejection.WrongChannel);
}

static void WrongReplyUserIsRejected()
{
    var now = DateTime.UtcNow;
    var source = ReplyMessage("100000000000000002", "/fc hi", now);
    source.Author.Id = "999999999999999999";
    AssertReplyRejected(source, now, DiscordReplyRejection.WrongUser);
}

static void AutomatedReplySourcesAreRejected()
{
    var now = DateTime.UtcNow;
    var bot = ReplyMessage("100000000000000002", "/fc hi", now);
    bot.Author.Bot = true;
    AssertReplyRejected(bot, now, DiscordReplyRejection.BotAuthor);

    var webhook = ReplyMessage("100000000000000003", "/fc hi", now);
    webhook.WebhookId = "777777777777777777";
    AssertReplyRejected(webhook, now, DiscordReplyRejection.WebhookAuthor);
}

static void WrongCharacterIsRejected()
{
    var now = DateTime.UtcNow;
    var profile = ConfiguredReplyProfile();
    var accepted = DiscordReplyPolicy.TryAuthorize(
        profile,
        new CharacterIdentity("cid:other", "Other Character", "Example World", 2),
        ReplyMessage("100000000000000002", "/fc hi", now),
        "100000000000000001",
        now,
        null,
        out _,
        out var rejection);
    Assert(!accepted && rejection == DiscordReplyRejection.WrongCharacter, "wrong character was accepted");
}

static void DisabledRepliesAreRejected()
{
    var now = DateTime.UtcNow;
    var profile = ConfiguredReplyProfile();
    profile.DiscordRepliesEnabled = false;
    var accepted = DiscordReplyPolicy.TryAuthorize(
        profile,
        ActiveIdentity(),
        ReplyMessage("100000000000000002", "/fc hi", now),
        "100000000000000001",
        now,
        null,
        out _,
        out var rejection);
    Assert(!accepted && rejection == DiscordReplyRejection.Disabled, "disabled feature accepted a command");
}

static void PausedRepliesAreRejected()
{
    var now = DateTime.UtcNow;
    var profile = ConfiguredReplyProfile();
    profile.Paused = true;
    var accepted = DiscordReplyPolicy.TryAuthorize(
        profile,
        ActiveIdentity(),
        ReplyMessage("100000000000000002", "/fc hi", now),
        "100000000000000001",
        now,
        null,
        out _,
        out var rejection);
    Assert(!accepted && rejection == DiscordReplyRejection.Paused, "paused feature accepted a command");
}

static void ReplayAndStaleRepliesAreRejected()
{
    var now = DateTime.UtcNow;
    AssertReplyRejected(
        ReplyMessage("100000000000000001", "/fc duplicate", now),
        now,
        DiscordReplyRejection.Duplicate);
    AssertReplyRejected(
        ReplyMessage("100000000000000002", "/fc old", now.Subtract(TimeSpan.FromMinutes(3))),
        now,
        DiscordReplyRejection.Stale);
}

static void OutboundAllowlistIsRequired()
{
    var now = DateTime.UtcNow;
    var profile = ConfiguredReplyProfile();
    profile.EnabledOutboundChannels.Clear();
    var accepted = DiscordReplyPolicy.TryAuthorize(
        profile,
        ActiveIdentity(),
        ReplyMessage("100000000000000002", "/fc hi", now),
        "100000000000000001",
        now,
        null,
        out _,
        out var rejection);
    Assert(!accepted && rejection == DiscordReplyRejection.DestinationNotAllowed, "disabled destination was accepted");
}

static void ReplyRateLimitIsEnforced()
{
    var limiter = new SlidingWindowRateLimiter(2, TimeSpan.FromSeconds(30));
    var now = DateTime.UtcNow;
    Assert(limiter.TryAcquire(now, out _), "first message blocked");
    Assert(limiter.TryAcquire(now.AddSeconds(1), out _), "second message blocked");
    Assert(!limiter.TryAcquire(now.AddSeconds(2), out var retry) && retry > TimeSpan.Zero, "limit was not enforced");
    Assert(limiter.TryAcquire(now.AddSeconds(31), out _), "expired rate-limit entry was not released");
}

static void ScreenshotCommandIsIsolated()
{
    Assert(DiscordControlCommandParser.TryParse("/screenshot", out var command)
           && command == DiscordControlCommand.Screenshot, "exact screenshot command was rejected");
    Assert(DiscordControlCommandParser.TryParse("  /SCREENSHOT  ", out command)
           && command == DiscordControlCommand.Screenshot, "case-insensitive screenshot command was rejected");
    Assert(!DiscordControlCommandParser.TryParse("/screenshot now", out _), "screenshot arguments were accepted");
    Assert(!DiscordControlCommandParser.TryParse("/screenshot.exe", out _), "screenshot prefix extension was accepted");
    Assert(!DiscordReplyCommandParser.TryParse("/screenshot", out _, out _),
        "screenshot leaked into the FFXIV chat-command parser");
}

static void AuthorizedScreenshotIsAccepted()
{
    var now = DateTime.UtcNow;
    var accepted = RemoteScreenshotPolicy.TryAuthorize(
        ConfiguredScreenshotProfile(),
        ActiveIdentity(),
        ReplyMessage("100000000000000002", "/screenshot", now),
        "100000000000000001",
        now,
        null,
        out var rejection);
    Assert(accepted, $"authorized screenshot was rejected: {rejection}");
}

static void ScreenshotAuthorizationIsIsolated()
{
    var now = DateTime.UtcNow;
    var wrongChannel = ReplyMessage("100000000000000002", "/screenshot", now);
    wrongChannel.ChannelId = "999999999999999999";
    AssertScreenshotRejected(wrongChannel, now, null, DiscordReplyRejection.WrongChannel);

    var wrongUser = ReplyMessage("100000000000000003", "/screenshot", now);
    wrongUser.Author.Id = "999999999999999999";
    AssertScreenshotRejected(wrongUser, now, null, DiscordReplyRejection.WrongUser);

    var bot = ReplyMessage("100000000000000004", "/screenshot", now);
    bot.Author.Bot = true;
    AssertScreenshotRejected(bot, now, null, DiscordReplyRejection.BotAuthor);

    var webhook = ReplyMessage("100000000000000005", "/screenshot", now);
    webhook.WebhookId = "777777777777777777";
    AssertScreenshotRejected(webhook, now, null, DiscordReplyRejection.WebhookAuthor);

    var accepted = RemoteScreenshotPolicy.TryAuthorize(
        ConfiguredScreenshotProfile(),
        new CharacterIdentity("cid:other", "Other Character", "Example World", 2),
        ReplyMessage("100000000000000006", "/screenshot", now),
        "100000000000000001",
        now,
        null,
        out var rejection);
    Assert(!accepted && rejection == DiscordReplyRejection.WrongCharacter,
        "screenshot crossed to the wrong FFXIV character");
}

static void ScreenshotOptInAndCooldownAreEnforced()
{
    var now = DateTime.UtcNow;
    var profile = ConfiguredScreenshotProfile();
    profile.RemoteScreenshotsEnabled = false;
    var accepted = RemoteScreenshotPolicy.TryAuthorize(
        profile,
        ActiveIdentity(),
        ReplyMessage("100000000000000002", "/screenshot", now),
        "100000000000000001",
        now,
        null,
        out var rejection);
    Assert(!accepted && rejection == DiscordReplyRejection.ScreenshotDisabled,
        "screenshot was accepted without explicit opt-in");

    AssertScreenshotRejected(
        ReplyMessage("100000000000000003", "/screenshot", now.AddSeconds(5)),
        now.AddSeconds(5),
        now,
        DiscordReplyRejection.ScreenshotCooldown);
    var afterCooldown = RemoteScreenshotPolicy.TryAuthorize(
        ConfiguredScreenshotProfile(),
        ActiveIdentity(),
        ReplyMessage("100000000000000004", "/screenshot", now.AddSeconds(16)),
        "100000000000000001",
        now.AddSeconds(16),
        now,
        out rejection);
    Assert(afterCooldown, $"screenshot remained blocked after cooldown: {rejection}");
}

static void ScreenshotReplayAndStaleAreRejected()
{
    var now = DateTime.UtcNow;
    AssertScreenshotRejected(
        ReplyMessage("100000000000000001", "/screenshot", now),
        now,
        null,
        DiscordReplyRejection.Duplicate);
    AssertScreenshotRejected(
        ReplyMessage("100000000000000002", "/screenshot", now.Subtract(TimeSpan.FromMinutes(3))),
        now,
        null,
        DiscordReplyRejection.Stale);
}

static void PngEncodingWorks()
{
    const int width = 2;
    const int height = 1;
    byte[] bgra = [0, 0, 255, 0, 0, 255, 0, 0];
    var png = PngEncoder.EncodeBgra(bgra, width, height);
    Assert(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "PNG signature was invalid");
    Assert(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) == width, "PNG width was invalid");
    Assert(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) == height, "PNG height was invalid");
    Assert(png.Length < 1_000_000, "tiny PNG output was unexpectedly large");
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
    var result = await sender.SendAsync(endpoint, [WebhookMessageFormatter.TestMessage("Example Character")], CancellationToken.None);

    Assert(result.Success, result.Error ?? "retry failed");
    Assert(result.Attempts == 2, "unexpected retry count");
    Assert(result.WasRateLimited, "rate-limit flag missing");
    Assert(delays.Count == 1 && delays[0] >= TimeSpan.FromMilliseconds(100), "retry delay was not honored");
}

static async Task DiscordReaderRateLimitIsRetried()
{
    var handler = new SequenceHandler(
        () => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"retry_after\":0.25}"),
        },
        () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[{\"id\":\"100000000000000003\",\"channel_id\":\"123456789012345678\",\"content\":\"/fc hi\",\"timestamp\":\"2026-09-21T12:00:00+00:00\",\"author\":{\"id\":\"234567890123456789\",\"bot\":false}}]"),
        });
    using var http = new HttpClient(handler);
    var delays = new List<TimeSpan>();
    var client = new DiscordRestMessageClient(http, (duration, _) =>
    {
        delays.Add(duration);
        return Task.CompletedTask;
    });
    var result = await client.GetAfterAsync(
        "a-valid-looking-token-that-is-never-real",
        "123456789012345678",
        "100000000000000001",
        CancellationToken.None);

    Assert(result.Success, result.Error ?? "reader retry failed");
    Assert(result.WasRateLimited, "reader rate-limit flag missing");
    Assert(result.Messages.Single().Content == "/fc hi", "message content changed");
    Assert(delays.Count == 1 && delays[0] >= TimeSpan.FromMilliseconds(100), "reader retry delay was not honored");
}

static async Task WebhookTestSucceeds()
{
    var handler = new SequenceHandler(() => new HttpResponseMessage(HttpStatusCode.NoContent));
    using var client = new HttpClient(handler);
    var sender = new WebhookHttpSender(client, (_, _) => Task.CompletedTask);
    WebhookEndpoint.TryCreate(FakeWebhook(), out var endpoint, out _);
    var result = await sender.SendAsync(endpoint, [WebhookMessageFormatter.TestMessage("Example Character")], CancellationToken.None);

    Assert(result.Success, result.Error ?? "test failed");
    Assert(handler.Bodies.Single().Contains("connected successfully for Example Character", StringComparison.Ordinal), "test content missing");
}

static async Task ScreenshotUploadIsMultipartAndSafe()
{
    var handler = new SequenceHandler(() => new HttpResponseMessage(HttpStatusCode.NoContent));
    using var client = new HttpClient(handler);
    var sender = new ScreenshotWebhookSender(client, (_, _) => Task.CompletedTask);
    WebhookEndpoint.TryCreate(FakeWebhook(), out var endpoint, out _);
    var result = await sender.SendAsync(endpoint, "Example @everyone", new byte[] { 137, 80, 78, 71 }, CancellationToken.None);

    Assert(result.Success, result.Error ?? "screenshot upload failed");
    var body = handler.Bodies.Single();
    Assert(body.Contains("payload_json", StringComparison.Ordinal), "multipart payload_json field missing");
    Assert(body.Contains("files[0]", StringComparison.Ordinal), "multipart file field missing");
    Assert(body.Contains("sentinel-relay.png", StringComparison.Ordinal), "safe attachment filename missing");
    Assert(body.Contains("【SCREENSHOT】 Example @\u200Beveryone", StringComparison.Ordinal), "screenshot title was not mention-safe");
    Assert(body.Contains("\"parse\":[]", StringComparison.Ordinal), "screenshot allowed_mentions was not empty");
}

static async Task ScreenshotUploadRateLimitIsRetried()
{
    var handler = new SequenceHandler(
        () => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"retry_after\":0.25}"),
        },
        () => new HttpResponseMessage(HttpStatusCode.NoContent));
    using var client = new HttpClient(handler);
    var delays = new List<TimeSpan>();
    var sender = new ScreenshotWebhookSender(client, (duration, _) =>
    {
        delays.Add(duration);
        return Task.CompletedTask;
    });
    WebhookEndpoint.TryCreate(FakeWebhook(), out var endpoint, out _);
    var result = await sender.SendAsync(endpoint, "Example Character", new byte[] { 137, 80, 78, 71 }, CancellationToken.None);

    Assert(result.Success && result.Attempts == 2, result.Error ?? "screenshot retry failed");
    Assert(result.WasRateLimited, "screenshot rate-limit flag missing");
    Assert(delays.Count == 1 && delays[0] >= TimeSpan.FromMilliseconds(100),
        "screenshot retry delay was not honored");
}

static void ConfigurationModelPersists()
{
    var original = new Configuration();
    var first = original.GetOrCreateProfile("cid:ABC", "First Character", "Example World");
    first.ProtectedWebhookUrl = "dpapi-ciphertext-one";
    first.IncludeSenderWorld = false;
    first.UseDiscordEmbeds = true;
    first.Paused = true;
    first.CaptureRewardDiagnostics = true;
    first.EnabledInboundChannels = [RelayChatType.FreeCompany, RelayChatType.Shout];
    first.DiscordMentionUserId = "123456789012345678";
    first.Keywords = [new KeywordRule { Keyword = "ready", Channels = [RelayChatType.FreeCompany] }];
    first.ProtectedDiscordBotToken = "dpapi-bot-ciphertext-one";
    first.DiscordRepliesEnabled = true;
    first.RemoteScreenshotsEnabled = true;
    first.DiscordRelayChannelId = "123456789012345678";
    first.AuthorizedDiscordUserId = "234567890123456789";
    first.LastProcessedDiscordMessageId = "345678901234567890";
    first.EnabledOutboundChannels =
    [
        RelayChatType.FreeCompany,
        RelayChatType.Say,
        RelayChatType.IncomingTell,
        RelayChatType.CrossWorldLinkshell8,
    ];
    var second = original.GetOrCreateProfile("cid:DEF", "Second Character", "Example World");
    second.ProtectedWebhookUrl = "dpapi-ciphertext-two";
    second.EnabledInboundChannels = [RelayChatType.Party];

    var json = JsonSerializer.Serialize(original);
    var restored = JsonSerializer.Deserialize<Configuration>(json)
        ?? throw new InvalidOperationException("deserialization failed");
    var restoredFirst = restored.CharacterProfiles["cid:ABC"];
    var restoredSecond = restored.CharacterProfiles["cid:DEF"];
    Assert(restoredFirst.ProtectedWebhookUrl == first.ProtectedWebhookUrl, "first protected webhook was lost");
    Assert(restoredSecond.ProtectedWebhookUrl == second.ProtectedWebhookUrl, "second protected webhook was lost");
    Assert(restoredFirst.EnabledInboundChannels.SetEquals(first.EnabledInboundChannels), "first filters were lost");
    Assert(restoredSecond.EnabledInboundChannels.SetEquals(second.EnabledInboundChannels), "second filters were lost");
    Assert(restoredFirst.Paused && restoredFirst.UseDiscordEmbeds && !restoredFirst.IncludeSenderWorld, "preferences were lost");
    Assert(restoredFirst.CaptureRewardDiagnostics, "reward diagnostic preference was lost");
    Assert(restoredFirst.Keywords.Single().Keyword == "ready", "keyword was lost");
    Assert(restoredFirst.ProtectedDiscordBotToken == first.ProtectedDiscordBotToken, "protected bot credential was lost");
    Assert(restoredFirst.DiscordRepliesEnabled, "reply enabled state was lost");
    Assert(restoredFirst.RemoteScreenshotsEnabled, "remote screenshot enabled state was lost");
    Assert(restoredFirst.DiscordRelayChannelId == first.DiscordRelayChannelId, "reply channel was lost");
    Assert(restoredFirst.AuthorizedDiscordUserId == first.AuthorizedDiscordUserId, "authorized user was lost");
    Assert(restoredFirst.LastProcessedDiscordMessageId == first.LastProcessedDiscordMessageId, "checkpoint was lost");
    Assert(restoredFirst.EnabledOutboundChannels.SetEquals(first.EnabledOutboundChannels), "outbound allowlist was lost");
}

static CharacterProfile ConfiguredReplyProfile() => new()
{
    CharacterKey = "cid:one",
    DiscordRepliesEnabled = true,
    DiscordRelayChannelId = "123456789012345678",
    AuthorizedDiscordUserId = "234567890123456789",
    EnabledOutboundChannels = [RelayChatType.FreeCompany],
};

static CharacterProfile ConfiguredScreenshotProfile() => new()
{
    CharacterKey = "cid:one",
    DiscordRepliesEnabled = true,
    RemoteScreenshotsEnabled = true,
    DiscordRelayChannelId = "123456789012345678",
    AuthorizedDiscordUserId = "234567890123456789",
};

static CharacterIdentity ActiveIdentity() =>
    new("cid:one", "Example Character", "Example World", 1);

static DiscordChannelMessage ReplyMessage(string id, string content, DateTime timestamp) => new()
{
    Id = id,
    ChannelId = "123456789012345678",
    Content = content,
    Timestamp = new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
    Author = new DiscordMessageAuthor
    {
        Id = "234567890123456789",
        Bot = false,
    },
};

static void AssertReplyRejected(
    DiscordChannelMessage source,
    DateTime now,
    DiscordReplyRejection expected)
{
    var accepted = DiscordReplyPolicy.TryAuthorize(
        ConfiguredReplyProfile(),
        ActiveIdentity(),
        source,
        "100000000000000001",
        now,
        null,
        out _,
        out var rejection);
    Assert(!accepted && rejection == expected, $"expected {expected}, got {rejection}");
}

static void AssertScreenshotRejected(
    DiscordChannelMessage source,
    DateTime now,
    DateTime? lastAcceptedScreenshotUtc,
    DiscordReplyRejection expected)
{
    var accepted = RemoteScreenshotPolicy.TryAuthorize(
        ConfiguredScreenshotProfile(),
        ActiveIdentity(),
        source,
        "100000000000000001",
        now,
        lastAcceptedScreenshotUtc,
        out var rejection);
    Assert(!accepted && rejection == expected, $"expected {expected}, got {rejection}");
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

static CapturedChat RewardChat(string message) => new(
    RelayChatType.RewardsHuntResults,
    "FFXIV",
    null,
    message,
    DateTime.SpecifyKind(new DateTime(2026, 9, 22, 12, 0, 0), DateTimeKind.Utc));

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
