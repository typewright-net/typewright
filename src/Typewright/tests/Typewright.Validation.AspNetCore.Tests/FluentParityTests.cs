extern alias fluent;

using System.Text.Json;
using Typewright.Validation;
using Xunit;
using AttributeDocument = Billing.Api.Domain.BillingDocument;
using AttributeLineItem = Billing.Api.Domain.LineItem;
using AttributeSearch = Billing.Api.Domain.BillingDocumentSearch;
using FluentDocument = fluent::Billing.Fluent.Domain.BillingDocument;
using FluentLineItem = fluent::Billing.Fluent.Domain.LineItem;
using FluentSearch = fluent::Billing.Fluent.Domain.BillingDocumentSearch;

namespace Typewright.Validation.AspNetCore.Tests;

/// <summary>
/// The attribute+generator front end (Billing.Api) and the fluent front end
/// (Billing.Fluent) define the same domain and must produce the same implementation
/// contract: identical failures at identical paths, identical wire shapes, and
/// equivalent descriptors. This is what keeps the runtime capabilities decoupled from
/// the attributes and the source generator.
/// </summary>
public class FluentParityTests
{
    private static (AttributeDocument.Dto Attribute, FluentDocument.Dto Fluent) DocumentDtos(
        string? invoiceNumber, DateOnly? invoiceDate, string? accountNumber, string? memo,
        (string? Description, int? Quantity, decimal? UnitPrice)[]? lineItems)
        => (
            new AttributeDocument.Dto
            {
                InvoiceNumber = invoiceNumber,
                InvoiceDate = invoiceDate,
                AccountNumber = accountNumber,
                Memo = memo,
                LineItems = lineItems?
                    .Select(item => (AttributeLineItem.Dto?)new AttributeLineItem.Dto
                    {
                        Description = item.Description,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                    })
                    .ToList(),
            },
            new FluentDocument.Dto
            {
                InvoiceNumber = invoiceNumber,
                InvoiceDate = invoiceDate,
                AccountNumber = accountNumber,
                Memo = memo,
                LineItems = lineItems?
                    .Select(item => (FluentLineItem.Dto?)new FluentLineItem.Dto
                    {
                        Description = item.Description,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                    })
                    .ToList(),
            });

    private static HashSet<(string Path, string Code)> Failures<T>(ValidationResult<T> result)
        => result.Errors.Select(failure => (failure.Path, failure.Error.Code)).ToHashSet();

    [Fact]
    public void Same_invalid_input_produces_identical_failures()
    {
        var (attributeDto, fluentDto) = DocumentDtos(
            invoiceNumber: "X123",
            invoiceDate: null,
            accountNumber: " 79927398714 ",
            memo: "",
            lineItems:
            [
                ("ok", 1, 1m),
                (null, 0, -5m),
            ]);

        var attributeResult = AttributeDocument.TryCreate(attributeDto);
        var fluentResult = FluentDocument.TryCreate(fluentDto);

        Assert.False(attributeResult.IsValid);
        Assert.False(fluentResult.IsValid);
        Assert.Equal(Failures(attributeResult), Failures(fluentResult));
    }

    [Fact]
    public void Same_valid_input_produces_identical_wire_output()
    {
        var (attributeDto, fluentDto) = DocumentDtos(
            invoiceNumber: "I123456789",
            invoiceDate: new DateOnly(2026, 1, 15),
            accountNumber: "  79927398713  ",
            memo: null,
            lineItems: [("Widget", 2, 9.99m)]);

        var attributeJson = JsonSerializer.Serialize(AttributeDocument.Create(attributeDto));
        var fluentJson = JsonSerializer.Serialize(FluentDocument.Create(fluentDto));

        Assert.Equal(attributeJson, fluentJson);
    }

    [Fact]
    public void Cross_field_rule_behaves_identically()
    {
        var attributeResult = AttributeSearch.TryCreate(new AttributeSearch.Dto
        {
            BeginDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 1, 1),
            AccountNumber = "79927398713",
        });
        var fluentResult = FluentSearch.TryCreate(new FluentSearch.Dto
        {
            BeginDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 1, 1),
            AccountNumber = "79927398713",
        });

        Assert.Equal(Failures(attributeResult), Failures(fluentResult));
        Assert.Equal(("", "date_range"), Failures(fluentResult).Single());
    }

    [Fact]
    public void Descriptors_are_equivalent()
    {
        // Values: same raw types, rules, and error codes.
        AssertValueInfoParity(
            Billing.Api.Domain.AccountNumber.DomainTypeInfo,
            fluent::Billing.Fluent.Domain.AccountNumber.DomainTypeInfo);
        AssertValueInfoParity(
            Billing.Api.Domain.InvoiceNumber.DomainTypeInfo,
            fluent::Billing.Fluent.Domain.InvoiceNumber.DomainTypeInfo);
        AssertValueInfoParity(
            Billing.Api.Domain.Quantity.DomainTypeInfo,
            fluent::Billing.Fluent.Domain.Quantity.DomainTypeInfo);

        // Models: same fields (names, order, required-ness, collections) and error codes.
        var attributeInfo = AttributeDocument.DomainTypeInfo;
        var fluentInfo = FluentDocument.DomainTypeInfo;
        Assert.True(fluentInfo.IsModel);
        Assert.Equal(
            attributeInfo.Fields.Select(field => (field.Name, field.Required, field.IsCollection)),
            fluentInfo.Fields.Select(field => (field.Name, field.Required, field.IsCollection)));
        Assert.Equal(ErrorCodes(attributeInfo), ErrorCodes(fluentInfo));
    }

    private static void AssertValueInfoParity(DomainTypeInfo attribute, DomainTypeInfo fluentInfo)
    {
        Assert.Equal(attribute.RawType, fluentInfo.RawType);
        Assert.Equal(
            attribute.Rules.Select(rule => (rule.Kind, rule.Pattern, rule.MinLength, rule.MaxLength, rule.Minimum, rule.Maximum)),
            fluentInfo.Rules.Select(rule => (rule.Kind, rule.Pattern, rule.MinLength, rule.MaxLength, rule.Minimum, rule.Maximum)));
        Assert.Equal(ErrorCodes(attribute), ErrorCodes(fluentInfo));
    }

    private static string[] ErrorCodes(DomainTypeInfo info)
        => [.. info.ErrorTypes.Select(DomainErrorCode.For).OrderBy(code => code, StringComparer.Ordinal)];
}
