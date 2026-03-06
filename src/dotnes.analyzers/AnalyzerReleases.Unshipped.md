; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NES001 | NES | Error | NES programs must end with an infinite loop
NES002 | NES | Error | Classes and objects are not supported
NES003 | NES | Error | String manipulation is not supported
NES004 | NES | Warning | Unsupported allocation type
NES005 | NES | Warning | Unsupported type
NES006 | NES | Info | Consider using static extern
