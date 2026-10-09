using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Tests.Settings;

public class RuleValidatorTests
{
    private static readonly RuleDto[] None = Array.Empty<RuleDto>();

    [Theory]
    [InlineData("BlockedDomain", "@spam.example")]
    [InlineData("AllowedDomain", "*.partner.example")]
    [InlineData("blockeddomain", "  spam.example  ")]
    [InlineData("BlockedPhrase", "cheap SEO services")]
    public void ValidRules_HaveNoErrors(string type, string pattern)
    {
        Assert.Empty(RuleValidator.Validate(type, pattern, None));
    }

    [Theory]
    [InlineData("Whitelist", "spam.example")]
    [InlineData("1", "spam.example")]
    [InlineData(null, "spam.example")]
    [InlineData("BlockedDomain", "")]
    [InlineData("BlockedDomain", "   ")]
    [InlineData("BlockedDomain", null)]
    [InlineData("BlockedPhrase", "bad\u0007phrase")]
    [InlineData("BlockedDomain", "not a domain")]
    [InlineData("BlockedDomain", "spam..example")]
    [InlineData("BlockedDomain", "@")]
    [InlineData("AllowedDomain", "user@spam.example")]
    public void InvalidRules_AreErrors(string? type, string? pattern)
    {
        Assert.NotEmpty(RuleValidator.Validate(type, pattern, None));
    }

    [Fact]
    public void LengthLimits()
    {
        var label = new string('a', 60);
        var domain253 = string.Join('.', label, label, label, label) + ".abcdefghi";
        Assert.Equal(253, domain253.Length);
        Assert.Empty(RuleValidator.Validate("BlockedDomain", "@" + domain253, None));
        Assert.NotEmpty(RuleValidator.Validate("BlockedDomain", domain253 + "a", None));

        Assert.Empty(RuleValidator.Validate("BlockedPhrase", new string('a', 200), None));
        Assert.NotEmpty(RuleValidator.Validate("BlockedPhrase", new string('a', 201), None));
    }

    [Theory]
    [InlineData("BlockedDomain", "spam.example", "BlockedDomain", "@SPAM.example")]
    [InlineData("BlockedDomain", "*.spam.example", "BlockedDomain", "spam.example")]
    [InlineData("BlockedPhrase", "Cheap SEO", "BlockedPhrase", "  cheap   seo ")]
    public void Duplicate_IsError(string existingType, string existingPattern, string type, string pattern)
    {
        var existing = new[] { new RuleDto { Id = 1, RuleType = existingType, Pattern = existingPattern } };
        Assert.Single(RuleValidator.Validate(type, pattern, existing));
    }

    [Fact]
    public void SamePatternDifferentType_IsNotDuplicate()
    {
        var existing = new[] { new RuleDto { Id = 1, RuleType = "BlockedDomain", Pattern = "spam.example" } };
        Assert.Empty(RuleValidator.Validate("AllowedDomain", "spam.example", existing));
    }

    [Fact]
    public void FormAtRuleLimit_IsError()
    {
        var existing = Enumerable.Range(1, RuleValidator.MaxRulesPerForm)
            .Select(i => new RuleDto { Id = i, RuleType = "BlockedDomain", Pattern = $"d{i}.example" })
            .ToArray();
        Assert.Single(RuleValidator.Validate("BlockedDomain", "new.example", existing));
        Assert.Empty(RuleValidator.Validate("BlockedDomain", "new.example", existing[..^1]));
    }
}
