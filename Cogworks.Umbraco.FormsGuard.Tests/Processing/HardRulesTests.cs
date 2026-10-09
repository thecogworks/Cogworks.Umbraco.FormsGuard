using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Processing;
using FormsConstants = Umbraco.Forms.Core.Constants;

namespace Cogworks.Umbraco.FormsGuard.Tests.Processing;

public class HardRulesTests
{
    private static readonly Guid ShortAnswer = ToGuid(FormsConstants.FieldTypes.Textfield);
    private static readonly Guid LongAnswer = ToGuid(FormsConstants.FieldTypes.Textarea);
    private static readonly Guid Password = ToGuid(FormsConstants.FieldTypes.Password);

    [Fact]
    public void BlockedPhrase_MatchesAcrossCaseAndWhitespace()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(7, HardRuleType.BlockedPhrase, "guest post") },
            new[] { Field("Message", LongAnswer, "Can I write a Guest  Post?") },
            null);

        Assert.NotNull(hit);
        Assert.Equal(DecisionStatus.Quarantined, hit!.Status);
        Assert.Equal("BlockedPhrase:7", hit.ToString());
    }

    [Fact]
    public void BlockedPhrase_InsideWord_NoHit()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(1, HardRuleType.BlockedPhrase, "sex") },
            new[] { Field("Message", LongAnswer, "Office in Sussex") },
            null);

        Assert.Null(hit);
    }

    [Fact]
    public void BlockedDomain_MatchesSubdomain()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(3, HardRuleType.BlockedDomain, "seo-spam.io") },
            new[] { Email("a@mail.seo-spam.io") },
            null);

        Assert.Equal(DecisionStatus.Quarantined, hit!.Status);
        Assert.Equal("BlockedDomain:3", hit.ToString());
    }

    [Fact]
    public void BlockedDomain_Lookalike_NoHit()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(1, HardRuleType.BlockedDomain, "example.com") },
            new[] { Email("a@badexample.com") },
            null);

        Assert.Null(hit);
    }

    [Fact]
    public void AllowedDomain_SingleMatchingDomain_Approves()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(4, HardRuleType.AllowedDomain, "client.co.uk") },
            new[] { Email("a@client.co.uk") },
            null);

        Assert.Equal(DecisionStatus.Approved, hit!.Status);
        Assert.Equal("AllowedDomain:4", hit.ToString());
    }

    [Fact]
    public void AllowedDomain_MixedEmailDomains_NoHit()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(1, HardRuleType.AllowedDomain, "client.co.uk") },
            new[] { Email("a@client.co.uk"), Email("b@other.com", "Work email") },
            null);

        Assert.Null(hit);
    }

    [Fact]
    public void DomainRules_NoEmailField_NoHit()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(1, HardRuleType.BlockedDomain, "seo-spam.io"), Rule(2, HardRuleType.AllowedDomain, "client.co.uk") },
            new[] { Field("Name", ShortAnswer, "a@seo-spam.io"), Field("Message", LongAnswer, "Hello") },
            null);

        Assert.Null(hit);
    }

    [Fact]
    public void EmailFieldId_RestrictsCandidates()
    {
        var chosen = Field("Contact", ShortAnswer, "a@client.co.uk");
        var hit = HardRules.Evaluate(
            new[] { Rule(1, HardRuleType.AllowedDomain, "client.co.uk") },
            new[] { chosen, Email("b@other.com") },
            chosen.Id);

        Assert.Equal(DecisionStatus.Approved, hit!.Status);
    }

    [Fact]
    public void BlockedPhrase_InNonAllowlistedField_Quarantines()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(1, HardRuleType.BlockedPhrase, "crypto") },
            new[] { Field("Name", ShortAnswer, "Crypto King"), Field("Message", LongAnswer, "Hello") },
            null);

        Assert.Equal(DecisionStatus.Quarantined, hit!.Status);
    }

    [Fact]
    public void BlockedPhrase_OnlyInPassword_NoHit()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(1, HardRuleType.BlockedPhrase, "crypto") },
            new[] { Field("Password", Password, "crypto"), Field("Message", LongAnswer, "Hello") },
            null);

        Assert.Null(hit);
    }

    [Fact]
    public void BlankPattern_Skipped()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(1, HardRuleType.BlockedPhrase, " "), Rule(2, HardRuleType.BlockedDomain, "  ") },
            new[] { Email("a@client.co.uk"), Field("Message", LongAnswer, "Hello there") },
            null);

        Assert.Null(hit);
    }

    [Fact]
    public void TryParse_KnownTypes_UnknownRejected()
    {
        Assert.True(HardRules.TryParse("BlockedDomain", out var a));
        Assert.Equal(HardRuleType.BlockedDomain, a);
        Assert.True(HardRules.TryParse("AllowedDomain", out var b));
        Assert.Equal(HardRuleType.AllowedDomain, b);
        Assert.True(HardRules.TryParse("BlockedPhrase", out var c));
        Assert.Equal(HardRuleType.BlockedPhrase, c);
        Assert.False(HardRules.TryParse("Honeypot", out _));
        Assert.False(HardRules.TryParse("", out _));
        Assert.False(HardRules.TryParse(null, out _));
        Assert.False(HardRules.TryParse("1", out _));
        Assert.False(HardRules.TryParse("BlockedDomain,AllowedDomain", out _));
        Assert.False(HardRules.TryParse("BlockedDomain, 2", out _));
    }

    [Fact]
    public void Precedence_BlockedPhraseBeatsAllowedDomain()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(1, HardRuleType.AllowedDomain, "client.co.uk"), Rule(9, HardRuleType.BlockedPhrase, "guest post") },
            new[] { Email("a@client.co.uk"), Field("Message", LongAnswer, "A guest post please") },
            null);

        Assert.Equal(DecisionStatus.Quarantined, hit!.Status);
        Assert.Equal("BlockedPhrase:9", hit.ToString());
    }

    [Theory]
    [InlineData("@seo-spam.io")]
    [InlineData("*.seo-spam.io")]
    [InlineData("  SEO-Spam.IO ")]
    public void DomainPattern_Normalised(string pattern)
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(1, HardRuleType.BlockedDomain, pattern) },
            new[] { Email("a@seo-spam.io") },
            null);

        Assert.Equal(DecisionStatus.Quarantined, hit!.Status);
    }

    [Fact]
    public void Domain_SubdomainMatches_ParentDoesNot()
    {
        var rules = new[] { Rule(1, HardRuleType.BlockedDomain, "mail.example.com") };

        Assert.NotNull(HardRules.Evaluate(rules, new[] { Email("a@x.mail.example.com") }, null));
        Assert.Null(HardRules.Evaluate(rules, new[] { Email("a@example.com") }, null));
    }

    [Fact]
    public void StaleEmailFieldId_FallsBackToAutoDetect()
    {
        var hit = HardRules.Evaluate(
            new[] { Rule(1, HardRuleType.BlockedDomain, "seo-spam.io") },
            new[] { Email("a@seo-spam.io"), Field("Message", LongAnswer, "Hello") },
            Guid.NewGuid());

        Assert.Equal("BlockedDomain:1", hit?.ToString());
    }

    private static HardRule Rule(int id, HardRuleType type, string pattern) => new(id, type, pattern);

    private static StateField Email(string value, string caption = "Email") =>
        new(Guid.NewGuid(), caption, caption.ToLowerInvariant(), ShortAnswer, true, value);

    private static StateField Field(string caption, Guid type, string? value) =>
        new(Guid.NewGuid(), caption, caption.ToLowerInvariant(), type, false, value);

    private static Guid ToGuid(object id) => id is Guid g ? g : Guid.Parse(id.ToString()!);
}
