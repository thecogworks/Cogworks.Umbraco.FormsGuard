using System.Text.Json;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Settings;
using Umbraco.Forms.Core.Models;
using FormsConstants = Umbraco.Forms.Core.Constants;
using FormsField = Umbraco.Forms.Core.Models.Field;
using FormsForm = Umbraco.Forms.Core.Models.Form;
using FormsRecord = Umbraco.Forms.Core.Persistence.Dtos.Record;
using FormsRecordField = Umbraco.Forms.Core.Persistence.Dtos.RecordField;

namespace Cogworks.Umbraco.FormsGuard.Tests.Processing;

public class DecisionStateBuilderTests
{
    private static readonly Guid ShortAnswer = ToGuid(FormsConstants.FieldTypes.Textfield);
    private static readonly Guid LongAnswer = ToGuid(FormsConstants.FieldTypes.Textarea);
    private static readonly Guid Upload = ToGuid(FormsConstants.FieldTypes.Upload);

    private static readonly StateField Name = Field("Name", ShortAnswer, "Bobby Tables");
    private static readonly StateField Email = Field("Email", ShortAnswer, "Bob@Example.co.uk", email: true);
    private static readonly StateField Phone = Field("Phone", ShortAnswer, "07700 900123");
    private static readonly StateField Subject = Field("Subject", ShortAnswer, "Quote request");
    private static readonly StateField Company = Field("Company", ShortAnswer, "Acme Ltd");
    private static readonly StateField Message = Field("Message", LongAnswer, "Please call me back.");
    private static readonly StateField Cv = Field("CV", Upload, "bobby-cv-secret.pdf");

    private static readonly StateField[] ContactForm = { Name, Email, Phone, Subject, Company, Message, Cv };

    private static readonly string[] Disallowed = { "Bobby", "Bob", "07700", "bobby-cv-secret" };

    [Fact]
    public void DefaultAllowlist_KeepsSubjectCompanyAndLongAnswer()
    {
        var state = DecisionStateBuilder.Build(Settings(), "Contact", ContactForm);

        Assert.Equal(new[] { "Subject", "Company", "Message" }, state.Fields.Keys);
        Assert.Equal("Quote request", state.Fields["Subject"]);
        Assert.Equal("Acme Ltd", state.Fields["Company"]);
        Assert.Equal("Please call me back.", state.Fields["Message"]);
        Assert.Equal("Contact", state.Form);
        Assert.Equal("A UK charity.", state.Organisation);
        AssertNoDisallowed(state);
    }

    [Fact]
    public void ExplicitAllowlist_KeepsOnlyListed_NeverUpload()
    {
        var state = DecisionStateBuilder.Build(Settings(allowed: new[] { Name.Id, Cv.Id }), "Contact", ContactForm);

        Assert.Equal(new[] { "Name" }, state.Fields.Keys);
        Assert.Equal("Bobby Tables", state.Fields["Name"]);
        AssertDoesNotContain(state, "07700", "bobby-cv-secret", "Bob@");
    }

    [Fact]
    public void EmptyExplicitAllowlist_GivesNoFields()
    {
        var state = DecisionStateBuilder.Build(Settings(allowed: Array.Empty<Guid>()), "Contact", ContactForm);

        Assert.Empty(state.Fields);
        AssertNoDisallowed(state);
    }

    [Fact]
    public void EmailDomainOff_GivesNoDomain()
    {
        var state = DecisionStateBuilder.Build(Settings(sendDomain: false), "Contact", ContactForm);

        Assert.Null(state.EmailDomain);
        AssertNoDisallowed(state);
    }

    [Fact]
    public void EmailDomainOn_GivesLowercasedDomainOnly()
    {
        var state = DecisionStateBuilder.Build(Settings(sendDomain: true), "Contact", ContactForm);

        Assert.Equal("example.co.uk", state.EmailDomain);
        AssertNoDisallowed(state);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bob")]
    [InlineData("bob@localhost")]
    [InlineData("bob@")]
    [InlineData("bob smith@example.com")]
    public void NoOrBadEmail_GivesNoDomain(string? value)
    {
        var fields = new[] { Name, Field("Email", ShortAnswer, value, email: true), Message };

        var state = DecisionStateBuilder.Build(Settings(sendDomain: true), "Contact", fields);

        Assert.Null(state.EmailDomain);
        AssertDoesNotContain(state, "Bobby", "Bob@", "07700", "bobby-cv-secret");
        if (!string.IsNullOrEmpty(value))
        {
            AssertDoesNotContain(state, value);
        }
    }

    [Fact]
    public void NoEmailField_GivesNoDomain()
    {
        var state = DecisionStateBuilder.Build(Settings(sendDomain: true), "Contact", new[] { Name, Message });

        Assert.Null(state.EmailDomain);
        AssertNoDisallowed(state);
    }

    [Fact]
    public void EmailInMessage_LocalPartMasked()
    {
        var fields = new[] { Name, Field("Message", LongAnswer, "mail me at bob@x.com") };

        var state = DecisionStateBuilder.Build(Settings(), "Contact", fields);

        Assert.Equal("mail me at [email]@x.com", state.Fields["Message"]);
        AssertNoDisallowed(state);
    }

    [Fact]
    public void EmailOverride_UsesThatFieldOnly()
    {
        var first = Field("Your email", ShortAnswer, "bob@first.com", email: true);
        var second = Field("Work email", ShortAnswer, "bob@second.org", email: true);

        var state = DecisionStateBuilder.Build(
            Settings(sendDomain: true, emailFieldId: second.Id), "Contact", new[] { Name, first, second, Message });

        Assert.Equal("second.org", state.EmailDomain);
        AssertNoDisallowed(state);
    }

    [Fact]
    public void EmailOverride_InvalidValue_GivesNoDomain()
    {
        var first = Field("Your email", ShortAnswer, "bob@first.com", email: true);
        var second = Field("Work email", ShortAnswer, "not an email", email: true);

        var state = DecisionStateBuilder.Build(
            Settings(sendDomain: true, emailFieldId: second.Id), "Contact", new[] { first, second, Phone, Cv });

        Assert.Null(state.EmailDomain);
        AssertDoesNotContain(state, "Bob@", "first.com", "not an email", "07700", "bobby-cv-secret");
    }

    [Fact]
    public void AllowlistedEmailField_IsMasked()
    {
        var state = DecisionStateBuilder.Build(
            Settings(allowed: new[] { Email.Id, Message.Id }, sendDomain: true), "Contact", ContactForm);

        Assert.Equal("[email]@Example.co.uk", state.Fields["Email"]); // Masking keeps the domain as typed.
        Assert.Equal("example.co.uk", state.EmailDomain);
        AssertNoDisallowed(state);
    }

    [Theory]
    [InlineData("a@b@c.com", "a@b")]
    [InlineData("bob@localhost", "bob")]
    [InlineData("bob@ex\u00e4mple.com", "bob")]
    [InlineData("bob@my_host.com", "bob")]
    public void UnusualAddresses_LocalPartMasked(string address, string localPart)
    {
        address = System.Text.RegularExpressions.Regex.Unescape(address);
        var fields = new[] { Field("Message", LongAnswer, $"write to {address} please") };

        var state = DecisionStateBuilder.Build(Settings(), "Contact", fields);

        Assert.StartsWith("write to [email]@", state.Fields["Message"]);
        Assert.DoesNotContain(localPart + "@", state.Fields["Message"]);
        Assert.DoesNotContain(" " + localPart, state.Fields["Message"]);
    }

    [Fact]
    public void AddressStraddlingFirstCut_LocalPartNotSent()
    {
        // Each address masks from 1,000+ chars down to ~14, so the text shrinks by far more than the slack.
        var longLocal = new string('x', 1000);
        var prefix = string.Join(" ", Enumerable.Repeat(longLocal + "@a.com", 4)) + " ";
        var straddleLocal = new string('s', 2000);
        var body = prefix + straddleLocal + "@b.com tail";
        Assert.True(body.IndexOf("@b.com", StringComparison.Ordinal)
            > DecisionStateBuilder.MaxFieldLength + 512);

        var state = DecisionStateBuilder.Build(Settings(), "Contact", new[] { Field("Message", LongAnswer, body) });

        var message = state.Fields["Message"];
        Assert.DoesNotContain("xxx", message);
        Assert.DoesNotContain("sss", message);
    }

    [Fact]
    public void AmbiguousEmail_GivesNoDomain()
    {
        var fields = new[]
        {
            Name,
            Field("What is your email?", ShortAnswer, "bob@mine.com", email: true),
            Field("What is your customer's email?", ShortAnswer, "bob@theirs.com", email: true),
            Message,
        };

        var state = DecisionStateBuilder.Build(Settings(sendDomain: true), "Contact", fields);

        Assert.Null(state.EmailDomain);
        AssertNoDisallowed(state);
    }

    [Fact]
    public void ConfirmEmail_SameValueIgnoringCase_GivesDomain()
    {
        var fields = new[]
        {
            Name,
            Field("Email", ShortAnswer, "Bob@Example.com", email: true),
            Field("Confirm email", ShortAnswer, "bob@example.COM", email: true),
            Message,
        };

        var state = DecisionStateBuilder.Build(Settings(sendDomain: true), "Contact", fields);

        Assert.Equal("example.com", state.EmailDomain);
        AssertNoDisallowed(state);
    }

    [Fact]
    public void LongValue_TrimmedThenTruncated()
    {
        var body = new string('a', 5000);
        var fields = new[] { Name, Field("Message", LongAnswer, "   \n" + body + "\t  ") };

        var state = DecisionStateBuilder.Build(Settings(), "Contact", fields);

        Assert.Equal(new string('a', DecisionStateBuilder.MaxFieldLength), state.Fields["Message"]);
        AssertNoDisallowed(state);
    }

    [Fact]
    public void LongValue_DoesNotSplitSurrogatePair()
    {
        var body = new string('a', DecisionStateBuilder.MaxFieldLength - 1) + "\U0001F600" + "tail";
        var fields = new[] { Field("Message", LongAnswer, body) };

        var state = DecisionStateBuilder.Build(Settings(), "Contact", fields);

        Assert.Equal(new string('a', DecisionStateBuilder.MaxFieldLength - 1), state.Fields["Message"]);
    }

    [Fact]
    public void BlankAndDuplicateFields_SkippedAndSuffixed()
    {
        var fields = new[]
        {
            Name,
            Field("Subject", ShortAnswer, "   "),
            Field("Message", LongAnswer, "first"),
            Field("Message", LongAnswer, "second"),
            Field("  ", LongAnswer, "by alias", alias: "notes"),
        };

        var state = DecisionStateBuilder.Build(Settings(), "Contact", fields);

        Assert.Equal(new[] { "Message", "Message (2)", "notes" }, state.Fields.Keys);
        Assert.Equal("first", state.Fields["Message"]);
        Assert.Equal("second", state.Fields["Message (2)"]);
        AssertNoDisallowed(state);
    }

    [Fact]
    public void FromRecord_MatchesRecordFieldsByFieldId_NotByKey()
    {
        var name = FormField("Name", ShortAnswer);
        var message = FormField("Message", LongAnswer);
        var form = Form(name, message);

        // Keyed by fresh Guids, as Forms does; only FieldId links a value to its form field.
        var record = Record(RecordField(message, "Hello"), RecordField(name, "Bobby"));

        var fields = DecisionStateBuilder.FromRecord(form, record);

        Assert.Equal(new[] { name.Id, message.Id }, fields.Select(f => f.Id));
        Assert.Equal(new[] { "Bobby", "Hello" }, fields.Select(f => f.Value));
    }

    [Fact]
    public void FromRecord_FieldWithNoRecordValue_IsNull()
    {
        var name = FormField("Name", ShortAnswer);
        var message = FormField("Message", LongAnswer);
        var form = Form(name, message);
        var record = Record(RecordField(name, "Bobby"));

        var fields = DecisionStateBuilder.FromRecord(form, record);

        Assert.Equal(2, fields.Count);
        Assert.Equal("Bobby", fields[0].Value);
        Assert.Null(fields[1].Value);
    }

    [Fact]
    public void FromRecord_FeedsHardRules_BlockedPhraseAndBlockedDomainHit()
    {
        var email = FormField("Email", ShortAnswer);
        var message = FormField("Message", LongAnswer);
        var form = Form(email, message);
        var record = Record(RecordField(email, "bob@mail.seo-spam.io"), RecordField(message, "Can I write a Guest  Post?"));

        var fields = DecisionStateBuilder.FromRecord(form, record);

        var phraseHit = HardRules.Evaluate(
            new[] { new HardRule(7, HardRuleType.BlockedPhrase, "guest post") }, fields, email.Id);
        Assert.Equal("BlockedPhrase:7", phraseHit?.ToString());
        Assert.Equal(DecisionStatus.Quarantined, phraseHit!.Status);

        var domainHit = HardRules.Evaluate(
            new[] { new HardRule(3, HardRuleType.BlockedDomain, "seo-spam.io") }, fields, email.Id);
        Assert.Equal("BlockedDomain:3", domainHit?.ToString());
    }

    [Fact]
    public void StaleEmailOverride_FallsBackToAutoDetect()
    {
        var state = DecisionStateBuilder.Build(
            Settings(sendDomain: true, emailFieldId: Guid.NewGuid()), "Contact", ContactForm);

        Assert.Equal("example.co.uk", state.EmailDomain);
    }

    private static FormGuardSettings Settings(
        IReadOnlyList<Guid>? allowed = null, bool sendDomain = false, Guid? emailFieldId = null) =>
        DefaultFormSettings.Create() with
        {
            Organisation = "A UK charity.",
            AllowedFieldIds = allowed,
            SendEmailDomain = sendDomain,
            EmailFieldId = emailFieldId,
        };

    private static StateField Field(string caption, Guid type, string? value, bool email = false, string? alias = null) =>
        new(Guid.NewGuid(), caption, alias ?? caption.ToLowerInvariant(), type, email, value);

    private static FormsField FormField(string caption, Guid type) =>
        new() { Id = Guid.NewGuid(), Caption = caption, Alias = caption.ToLowerInvariant(), FieldTypeId = type };

    private static FormsForm Form(params FormsField[] fields)
    {
        var container = new FieldsetContainer();
        container.Fields.AddRange(fields);
        var fieldSet = new FieldSet();
        fieldSet.Containers.Add(container);
        var page = new Page();
        page.FieldSets.Add(fieldSet);
        var form = new FormsForm { Id = Guid.NewGuid(), Name = "Contact" };
        form.Pages.Add(page);
        return form;
    }

    private static FormsRecordField RecordField(FormsField field, string value)
    {
        var recordField = new FormsRecordField { Key = Guid.NewGuid(), FieldId = field.Id };
        recordField.Values.Add(value);
        return recordField;
    }

    private static FormsRecord Record(params FormsRecordField[] recordFields)
    {
        var record = new FormsRecord { UniqueId = Guid.NewGuid() };
        foreach (var recordField in recordFields)
        {
            record.RecordFields.Add(recordField.Key, recordField);
        }

        return record;
    }

    private static void AssertNoDisallowed(DecisionState state) => AssertDoesNotContain(state, Disallowed);

    private static void AssertDoesNotContain(DecisionState state, params string[] values)
    {
        var json = JsonSerializer.Serialize(state);
        foreach (var value in values)
        {
            Assert.DoesNotContain(value, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Guid ToGuid(object id) => id is Guid g ? g : Guid.Parse(id.ToString()!);
}
