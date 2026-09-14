; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TW0001 | Typewright | Error | new/default of a [DomainValue] type bypasses validation
TW0002 | Typewright | Error | 'with' on a [DomainModel] with cross-field validation bypasses the rule
TW0003 | Typewright | Error | [DomainValue]/[DomainModel] type is not partial
TW0004 | Typewright | Warning | Domain model property is not a domain value, domain model, or collection of them
TW0005 | Typewright | Error | Validate hook reports an error type not declared in the domain type's error set
TW0006 | Typewright | Warning | [Reports<TError>] declared on a type with no Validate hook
TW1001 | Typewright | Error | [DomainValue] type must be a non-generic, non-nested, partial readonly record struct
TW1002 | Typewright | Error | Rule attribute not applicable to the raw type
TW1003 | Typewright | Error | Comparable = true requires a comparable raw type
TW1004 | Typewright | Error | [DomainModel] type must be a non-generic, non-nested, partial record class
