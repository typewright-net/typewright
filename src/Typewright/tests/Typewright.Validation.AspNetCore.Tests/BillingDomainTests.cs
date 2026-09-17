using System.Text.Json;
using Billing.Api.Domain;
using Typewright.Validation;
using Xunit;

namespace Typewright.Validation.AspNetCore.Tests;

/// <summary>Unit tests for the hand-written Phase 0 domain types (the generator's reference target).</summary>
public class BillingDomainTests
{
    private static BillingDocument.Dto ValidDocumentDto() => new()
    {
        InvoiceNumber = "I123456789",
        InvoiceDate = new DateOnly(2026, 1, 15),
        AccountNumber = "79927398713",
        LineItems =
        [
            new LineItem.Dto { Description = "Widget", Quantity = 2, UnitPrice = 9.99m },
        ],
    };

    [Fact]
    public void Valid_dto_round_trips_through_the_domain_model()
    {
        var document = BillingDocument.Create(ValidDocumentDto());

        Assert.Equal("I123456789", document.InvoiceNumber.Value);
        Assert.Null(document.Memo);

        var json = JsonSerializer.Serialize(document.ToDto());
        Assert.Equal(JsonSerializer.Serialize(ValidDocumentDto()), json);
    }

    [Fact]
    public void All_field_errors_accumulate_with_json_paths()
    {
        var dto = new BillingDocument.Dto
        {
            InvoiceNumber = "X123",
            InvoiceDate = null,
            AccountNumber = " 79927398714 ",
            Memo = "",
            LineItems =
            [
                new LineItem.Dto { Description = "ok", Quantity = 1, UnitPrice = 1m },
                new LineItem.Dto { Quantity = 0, UnitPrice = -5m },
            ],
        };

        var result = BillingDocument.TryCreate(dto);

        Assert.False(result.IsValid);
        var pathsAndCodes = result.Errors.Select(failure => (failure.Path, failure.Error.Code)).ToHashSet();
        var expected = new HashSet<(string, string)>
        {
            ("invoiceNumber", "pattern"),
            ("invoiceDate", "required"),
            ("accountNumber", "checksum"),
            ("memo", "length"),
            ("lineItems[1].description", "required"),
            ("lineItems[1].quantity", "range"),
            ("lineItems[1].unitPrice", "range"),
        };
        Assert.Equal(expected, pathsAndCodes);

        // The errors are types carrying the facts of the failure, not strings.
        var lengthFailure = Assert.Single(result.Errors.OfType<LengthError>());
        Assert.Equal(("memo", 0), (lengthFailure.Path, ((LengthError)lengthFailure.Error).Actual));
        Assert.True(result.Errors.Contains<ChecksumError>());
    }

    [Fact]
    public void Missing_line_items_collection_is_required()
    {
        var result = BillingDocument.TryCreate(ValidDocumentDto() with { LineItems = null });

        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Errors);
        Assert.Equal("lineItems", failure.Path);
        Assert.IsType<RequiredError>(failure.Error);
    }

    [Fact]
    public void Account_number_is_normalized_before_validation()
    {
        var account = AccountNumber.Create("  79927398713  ");

        Assert.Equal("79927398713", account.Value);
    }

    [Fact]
    public void Search_cross_field_rule_runs_only_after_fields_are_valid()
    {
        // Inverted dates AND a bad account: only the field error surfaces, because the
        // cross-field rule never runs on a partially valid candidate.
        var partiallyInvalid = BillingDocumentSearch.TryCreate(new BillingDocumentSearch.Dto
        {
            BeginDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 1, 1),
            AccountNumber = "not-luhn",
        });

        Assert.False(partiallyInvalid.IsValid);
        var fieldFailure = Assert.Single(partiallyInvalid.Errors);
        Assert.Equal("accountNumber", fieldFailure.Path);
        Assert.IsType<ChecksumError>(fieldFailure.Error);

        // With valid fields, the cross-field rule reports at the object level (empty path).
        var invalidRange = BillingDocumentSearch.TryCreate(new BillingDocumentSearch.Dto
        {
            BeginDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 1, 1),
            AccountNumber = "79927398713",
        });

        Assert.False(invalidRange.IsValid);
        var rangeFailure = Assert.Single(invalidRange.Errors);
        Assert.Equal("", rangeFailure.Path);
        var dateRange = Assert.IsType<DateRangeError>(rangeFailure.Error);
        Assert.Equal(new DateOnly(2026, 2, 1), dateRange.Begin);
    }

    [Fact]
    public void Typed_factory_enforces_the_cross_field_rule()
    {
        var begin = InvoiceDate.Create(new DateOnly(2026, 2, 1));
        var end = InvoiceDate.Create(new DateOnly(2026, 1, 1));
        var account = AccountNumber.Create("79927398713");

        var exception = Assert.Throws<DomainValidationException>(
            () => BillingDocumentSearch.Create(begin, end, account));

        Assert.IsType<DateRangeError>(Assert.Single(exception.Errors).Error);
    }

    [Fact]
    public void Domain_model_json_converter_round_trips_via_dto()
    {
        var document = BillingDocument.Create(ValidDocumentDto());

        var json = JsonSerializer.Serialize(document);
        var back = JsonSerializer.Deserialize<BillingDocument>(json);

        Assert.NotNull(back);
        Assert.Equal(document.InvoiceNumber, back.InvoiceNumber);
        Assert.Equal(document.InvoiceDate, back.InvoiceDate);
        Assert.Equal(document.AccountNumber, back.AccountNumber);
        Assert.Equal(document.LineItems, back.LineItems);
    }
}
