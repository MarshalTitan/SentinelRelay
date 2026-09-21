using System.Text;
using SentinelRelay.Core;
using SentinelRelay.Models;

var tests = new (string Name, Action Run)[]
{
    ("all filters default to opt-in by model convention", FiltersAreOptIn),
    ("outbound whitelist excludes tell and non-chat types", OutboundWhitelistIsStrict),
    ("outbound commands map to explicit safe prefixes", OutboundPrefixesAreExplicit),
    ("sanitizer removes line breaks and control characters", SanitizerRemovesControlData),
    ("sanitizer rejects empty and oversized messages", SanitizerValidatesLimits),
    ("contains matching is case-insensitive", ContainsMatchIsCaseInsensitive),
    ("whole-word matching respects word boundaries", WholeWordMatchUsesBoundaries),
    ("keyword matching is channel-scoped and deduplicated", KeywordMatchesAreScoped),
    ("sliding-window limiter allows five then blocks", RateLimiterBlocksBurst),
    ("sliding-window limiter recovers after window", RateLimiterRecovers),
    ("replay guard rejects duplicate event IDs", ReplayGuardRejectsDuplicates),
    ("replay guard accepts IDs after retention expires", ReplayGuardExpires),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
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

static void FiltersAreOptIn()
{
    var profile = new CharacterProfile();
    Assert(profile.EnabledInboundChannels.Count == 0, "new filter sets must be empty");
    Assert(!profile.EnabledInboundChannels.Contains(RelayChatType.FreeCompany), "FC must not default on");
    Assert(!profile.EnabledInboundChannels.Contains(RelayChatType.Tell), "Tell must not default on");
}

static void OutboundWhitelistIsStrict()
{
    Assert(ChannelPolicy.OutboundChannels.Contains(RelayChatType.FreeCompany), "FC should be outbound-capable");
    Assert(ChannelPolicy.OutboundChannels.Contains(RelayChatType.Say), "Say should be outbound-capable");
    Assert(!ChannelPolicy.OutboundChannels.Contains(RelayChatType.Tell), "Tell must be deferred");
    Assert(!ChannelPolicy.OutboundChannels.Contains(RelayChatType.NoviceNetwork), "NN must not be outbound-capable");
    Assert(!ChannelPolicy.OutboundChannels.Contains(RelayChatType.StandardEmote), "emotes must not be outbound-capable");
}

static void OutboundPrefixesAreExplicit()
{
    Assert(ChannelPolicy.GetCommandPrefix(RelayChatType.FreeCompany) == "/freecompany", "FC prefix");
    Assert(ChannelPolicy.GetCommandPrefix(RelayChatType.Say) == "/say", "Say prefix");
    Assert(ChannelPolicy.GetCommandPrefix(RelayChatType.CrossWorldLinkshell8) == "/cwlinkshell8", "CWLS8 prefix");
    AssertThrows<InvalidOperationException>(() => ChannelPolicy.GetCommandPrefix(RelayChatType.Tell));
}

static void SanitizerRemovesControlData()
{
    var sanitized = MessageSanitizer.SanitizePlainText(" hello\r\n\tworld\0  again ");
    Assert(sanitized == "hello world again", $"unexpected sanitized value: {sanitized}");
}

static void SanitizerValidatesLimits()
{
    Assert(!MessageSanitizer.IsValidOutbound(" ", out _), "empty message accepted");
    Assert(MessageSanitizer.IsValidOutbound("hello", out _), "normal message rejected");
    Assert(!MessageSanitizer.IsValidOutbound(new string('a', 181), out _), "character limit not enforced");
    Assert(!MessageSanitizer.IsValidOutbound(string.Concat(Enumerable.Repeat("😀", 101)), out _), "UTF-8 limit not enforced");
}

static void ContainsMatchIsCaseInsensitive()
{
    var rule = Rule("S rank", false, RelayChatType.Shout);
    Assert(KeywordMatcher.IsMatch(rule, "There is an S RANK up"), "case-insensitive match failed");
}

static void WholeWordMatchUsesBoundaries()
{
    var rule = Rule("raid", true, RelayChatType.FreeCompany);
    Assert(KeywordMatcher.IsMatch(rule, "Raid tonight?"), "whole word did not match");
    Assert(!KeywordMatcher.IsMatch(rule, "raider wanted"), "partial word incorrectly matched");
}

static void KeywordMatchesAreScoped()
{
    var rules = new[]
    {
        Rule("Wrothy", false, RelayChatType.FreeCompany),
        Rule("wrothy", false, RelayChatType.FreeCompany),
        Rule("maps", false, RelayChatType.Party),
    };
    var matches = KeywordMatcher.FindMatches(rules, RelayChatType.FreeCompany, "Wrothy, maps tonight?");
    Assert(matches.Count == 1, "matches were not channel-scoped/deduplicated");
}

static void RateLimiterBlocksBurst()
{
    var limiter = new SlidingWindowRateLimiter(5, TimeSpan.FromSeconds(30));
    var now = DateTime.UtcNow;
    for (var i = 0; i < 5; i++)
        Assert(limiter.TryAcquire(now.AddMilliseconds(i), out _), $"attempt {i + 1} rejected");
    Assert(!limiter.TryAcquire(now.AddSeconds(1), out var retry), "sixth attempt accepted");
    Assert(retry > TimeSpan.Zero, "retry-after missing");
}

static void RateLimiterRecovers()
{
    var limiter = new SlidingWindowRateLimiter(1, TimeSpan.FromSeconds(5));
    var now = DateTime.UtcNow;
    Assert(limiter.TryAcquire(now, out _), "first attempt rejected");
    Assert(limiter.TryAcquire(now.AddSeconds(5), out _), "limiter did not recover");
}

static void ReplayGuardRejectsDuplicates()
{
    var guard = new ReplayGuard();
    var now = DateTime.UtcNow;
    Assert(guard.TryAccept("abc", now), "first event rejected");
    Assert(!guard.TryAccept("abc", now.AddSeconds(1)), "duplicate accepted");
}

static void ReplayGuardExpires()
{
    var guard = new ReplayGuard(retention: TimeSpan.FromSeconds(2));
    var now = DateTime.UtcNow;
    Assert(guard.TryAccept("abc", now), "first event rejected");
    Assert(guard.TryAccept("abc", now.AddSeconds(3)), "expired event not accepted");
}

static KeywordRule Rule(string keyword, bool wholeWord, params RelayChatType[] channels) => new()
{
    Keyword = keyword,
    WholeWord = wholeWord,
    Channels = [.. channels],
};

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertThrows<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
