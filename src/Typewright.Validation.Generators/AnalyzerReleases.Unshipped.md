; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TW1001 | Typewright | Error | [DomainValue] type must be a non-generic, non-nested, partial readonly record struct
TW1002 | Typewright | Error | Rule attribute not applicable to the raw type
TW1003 | Typewright | Error | Comparable = true requires a comparable raw type
TW1004 | Typewright | Error | [DomainModel] type must be a non-generic, non-nested, partial record class
TW1005 | Typewright | Error | Domain model property is not a domain value, domain model, or supported collection
