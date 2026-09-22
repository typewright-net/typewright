using Typewright.Validation;
using Typewright.Validation.Fluent;
using Xunit;

namespace Typewright.Validation.Tests;

public sealed record TooPlainError : IDomainError;

public readonly record struct FluentCode : IDomainValue<FluentCode, string>
{
    private static readonly DomainValueDefinition<FluentCode, string> Definition =
        DomainValue.Define<FluentCode, string>(static value => new(value))
            .Normalize(trim: true, casing: Casing.Upper)
            .Pattern("^[A-Z]{3}$")
            .Length(3, 3)
            .Must(static value => value != "AAA", static _ => new TooPlainError())
            .Build();

    private readonly string _value;

    private FluentCode(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException("Default instance.");

    public static ValidationResult<FluentCode> TryCreate(string? value) => Definition.TryCreate(value);

    public static FluentCode Create(string? value) => Definition.Create(value);

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;
}

public class FluentValueDefinitionTests
{
    [Fact]
    public void Normalizes_then_validates_then_constructs()
        => Assert.Equal("XYZ", FluentCode.Create("  xyz  ").Value);

    [Fact]
    public void Null_is_required()
        => Assert.IsType<RequiredError>(Assert.Single(FluentCode.TryCreate(null).Errors).Error);

    [Fact]
    public void Rules_accumulate_and_hooks_wait_for_them()
    {
        // Pattern and Length both fail; the Must hook does not run on rule-invalid input.
        var result = FluentCode.TryCreate("no");

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Contains<PatternError>());
        Assert.True(result.Errors.Contains<LengthError>());
        Assert.False(result.Errors.Contains<TooPlainError>());
    }

    [Fact]
    public void Must_runs_after_rules_pass_and_is_declared_by_construction()
    {
        var result = FluentCode.TryCreate("aaa");

        Assert.IsType<TooPlainError>(Assert.Single(result.Errors).Error);
        Assert.Contains(typeof(TooPlainError), FluentCode.DomainTypeInfo.ErrorTypes);
    }

    [Fact]
    public void Descriptor_matches_the_declaration()
    {
        var info = FluentCode.DomainTypeInfo;

        Assert.Equal("FluentCode", info.Name);
        Assert.Equal(typeof(string), info.RawType);
        Assert.False(info.IsModel);
        Assert.Equal(["pattern", "length"], info.Rules.Select(rule => rule.Kind));
        Assert.Equal(
            ["LengthError", "PatternError", "RequiredError", "TooPlainError"],
            info.ErrorTypes.Select(type => type.Name));
    }

    private readonly record struct Sneaky(string Raw) : IDomainValue<Sneaky, string>
    {
        private static readonly DomainValueDefinition<Sneaky, string> Definition =
            DomainValue.Define<Sneaky, string>(static value => new(value))
                .Validate(static (_, errors) => errors.Add(new TooPlainError()))   // never declared
                .Build();

        public string Value => Raw;

        public static ValidationResult<Sneaky> TryCreate(string? value) => Definition.TryCreate(value);

        public static Sneaky Create(string? value) => Definition.Create(value);
    }

    [Fact]
    public void Undeclared_hook_errors_fail_loudly()
    {
        // The runtime equivalent of analyzer TW0005 for fluent definitions.
        var exception = Assert.Throws<InvalidOperationException>(() => Sneaky.TryCreate("anything"));

        Assert.Contains("TooPlainError", exception.Message);
        Assert.Contains("Reports<TooPlainError>()", exception.Message);
    }
}

public sealed record BadPairError : IDomainError;

public sealed record FluentPair : IDomainModel<FluentPair, FluentPair.Dto>
{
    public required FluentCode First { get; init; }

    public FluentCode? Second { get; init; }

    public required IReadOnlyList<FluentCode> Rest { get; init; }

    public required string Tag { get; init; }

    public sealed record Dto
    {
        public string? First { get; init; }

        public string? Second { get; init; }

        public List<string?>? Rest { get; init; }

        public string? Tag { get; init; }
    }

    private static readonly DomainModelDefinition<FluentPair, Dto> Definition = BuildDefinition();

    private static DomainModelDefinition<FluentPair, Dto> BuildDefinition()
    {
        var builder = DomainModel.Define<FluentPair, Dto>();
        var first = builder.Field(static dto => dto.First, FluentCode.TryCreate);
        var second = builder.OptionalValue(static dto => dto.Second, FluentCode.TryCreate);
        var rest = builder.Collection(static dto => dto.Rest, FluentCode.TryCreate);
        var tag = builder.Raw(static dto => dto.Tag, required: true);

        return builder
            .Must(
                static pair => pair.Second is null || pair.First != pair.Second,
                static _ => new BadPairError())
            .Construct(fields => new FluentPair
            {
                First = fields.Get(first),
                Second = fields.Get(second),
                Rest = fields.Get(rest),
                Tag = fields.Get(tag)!,   // required: true guarantees non-null here
            });
    }

    public static ValidationResult<FluentPair> TryCreate(Dto? dto) => Definition.TryCreate(dto);

    public static FluentPair Create(Dto? dto) => Definition.Create(dto);

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;

    public Dto ToDto() => new()
    {
        First = First.Value,
        Second = Second?.Value,
        Rest = Rest.Select(code => (string?)code.Value).ToList(),
        Tag = Tag,
    };
}

public class FluentModelDefinitionTests
{
    [Fact]
    public void Valid_dto_constructs_with_optionals_and_raw_passthrough()
    {
        var pair = FluentPair.Create(new FluentPair.Dto
        {
            First = "ABC",
            Rest = ["DEF", "GHI"],
            Tag = "kept-as-is",
        });

        Assert.Equal("ABC", pair.First.Value);
        Assert.Null(pair.Second);
        Assert.Equal(2, pair.Rest.Count);
        Assert.Equal("kept-as-is", pair.Tag);
    }

    [Fact]
    public void Field_failures_accumulate_under_json_paths()
    {
        var result = FluentPair.TryCreate(new FluentPair.Dto
        {
            First = null,
            Second = "nope",
            Rest = ["ABC", "??", null],
            Tag = null,
        });

        Assert.False(result.IsValid);
        var paths = result.Errors.Select(failure => (failure.Path, failure.Error.Code)).ToHashSet();
        var expected = new HashSet<(string, string)>
        {
            ("first", "required"),
            ("second", "pattern"),
            ("second", "length"),
            ("rest[1]", "pattern"),
            ("rest[1]", "length"),
            ("rest[2]", "required"),
            ("tag", "required"),
        };
        Assert.Equal(expected, paths);
    }

    [Fact]
    public void Cross_field_rule_runs_only_on_a_field_valid_candidate()
    {
        var invalid = FluentPair.TryCreate(new FluentPair.Dto
        {
            First = "ABC",
            Second = "ABC",
            Rest = [],
            Tag = "t",
        });

        var failure = Assert.Single(invalid.Errors);
        Assert.Equal("", failure.Path);
        Assert.IsType<BadPairError>(failure.Error);
    }

    [Fact]
    public void Descriptor_derives_fields_and_error_union()
    {
        var info = FluentPair.DomainTypeInfo;

        Assert.True(info.IsModel);
        Assert.Equal(typeof(FluentPair.Dto), info.RawType);
        Assert.Equal(
            [("first", true, false), ("second", false, false), ("rest", true, true), ("tag", true, false)],
            info.Fields.Select(field => (field.Name, field.Required, field.IsCollection)));

        // The union of the fields' sets (via the registry) plus the model's own errors.
        var errorNames = info.ErrorTypes.Select(type => type.Name).ToHashSet();
        Assert.Superset(
            new HashSet<string>
            {
                "BadPairError", "JsonInvalidError", "LengthError",
                "PatternError", "RequiredError", "TooPlainError",
            },
            errorNames);
    }
}
