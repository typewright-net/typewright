; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TW1001 | Typewright | Error | [DomainValue] type must be a non-generic, non-nested, partial readonly record struct
TW1002 | Typewright | Error | Rule attribute not applicable to the raw type
TW1003 | Typewright | Error | Comparable = true requires a comparable raw type
