; Shipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.3.1

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NES001 | NES | Error | NES programs must end with an infinite loop
NES002 | NES | Error | Class declarations are not supported
NES003 | NES | Error | String manipulation is not supported
NES004 | NES | Warning | Unsupported allocation type
NES005 | NES | Warning | Unsupported type
NES006 | NES | Info | Consider using static extern
NES007 | NES | Warning | Recursive functions are not supported
NES008 | NES | Error | LINQ is not supported
NES009 | NES | Error | Delegates and lambdas are not supported
NES010 | NES | Error | foreach loops are not supported
NES011 | NES | Error | Exception handling is not supported
NES012 | NES | Error | Property declarations are not supported
