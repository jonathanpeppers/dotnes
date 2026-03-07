using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace dotnes.analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NESAnalyzer : DiagnosticAnalyzer
{
    public const string NES001 = nameof(NES001);
    public const string NES002 = nameof(NES002);
    public const string NES003 = nameof(NES003);
    public const string NES004 = nameof(NES004);
    public const string NES005 = nameof(NES005);
    public const string NES006 = nameof(NES006);
    public const string NES007 = nameof(NES007);
    public const string NES008 = nameof(NES008);
    public const string NES009 = nameof(NES009);
    public const string NES010 = nameof(NES010);
    public const string NES011 = nameof(NES011);
    public const string NES012 = nameof(NES012);

    const string Category = "NES";

    static readonly DiagnosticDescriptor NES001Rule = new(
        NES001,
        "NES programs must end with an infinite loop",
        "NES programs must end with 'while (true)' (infinite loop required)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "NES programs have no operating system to return to, so they must end with an infinite loop.");

    static readonly DiagnosticDescriptor NES002Rule = new(
        NES002,
        "Class declarations are not supported",
        "Class '{0}' is not supported; the NES does not support class declarations",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The NES has no heap or garbage collector, so class declarations cannot be used. Use top-level statements, static methods, and structs instead.");

    static readonly DiagnosticDescriptor NES003Rule = new(
        NES003,
        "String manipulation is not supported",
        "String manipulation (concatenation, interpolation, formatting) is not supported on the NES",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The NES has no string manipulation support. Only string literals passed to NESLib methods are supported.");

    static readonly DiagnosticDescriptor NES004Rule = new(
        NES004,
        "Unsupported allocation type",
        "'new' allocations are limited to byte[], ushort[] arrays and structs",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The NES has very limited memory. Only byte[], ushort[] array allocations and struct allocations are supported.");

    static readonly DiagnosticDescriptor NES005Rule = new(
        NES005,
        "Unsupported type",
        "Type '{0}' is not supported; only byte, sbyte, ushort, int, bool, string, enums, arrays, and user-defined structs are supported",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The NES 6502 CPU only supports 8-bit and limited 16-bit operations. Only byte, sbyte, ushort, int, bool, string, enums, arrays, and user-defined structs are supported.");

    static readonly DiagnosticDescriptor NES006Rule = new(
        NES006,
        "Consider using static extern",
        "Consider using 'static extern' for external assembly functions instead of [DllImport]",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "NES projects can use 'static extern' methods without [DllImport] to link to assembly (.s) files.");

    static readonly DiagnosticDescriptor NES007Rule = new(
        NES007,
        "Recursive functions are not supported",
        "Method '{0}' calls itself recursively; the NES 6502 stack is only 256 bytes and recursion will overflow it",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The NES 6502 CPU has a hardware stack of only 256 bytes ($0100-$01FF). Recursive calls will quickly overflow this stack and crash. Use iterative loops instead.");

    static readonly DiagnosticDescriptor NES008Rule = new(
        NES008,
        "LINQ is not supported",
        "LINQ is not supported on the NES; remove 'using System.Linq' and use loops instead",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "LINQ requires delegates, generics, and heap allocation which are not available on the NES. Use simple while loops instead.");

    static readonly DiagnosticDescriptor NES009Rule = new(
        NES009,
        "Delegates and lambdas are not supported",
        "Delegates and lambda expressions are not supported on the NES",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The NES has no support for delegates, lambda expressions, or anonymous methods. Use static methods instead.");

    static readonly DiagnosticDescriptor NES010Rule = new(
        NES010,
        "foreach loops are not supported",
        "'foreach' is not supported; use 'while' loops instead",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The NES transpiler only supports 'while' loops. 'foreach' compiles to IEnumerator calls that cannot be transpiled.");

    static readonly DiagnosticDescriptor NES011Rule = new(
        NES011,
        "Exception handling is not supported",
        "try/catch/finally is not supported on the NES",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The NES 6502 CPU has no exception handling mechanism. Use conditional checks instead.");

    static readonly DiagnosticDescriptor NES012Rule = new(
        NES012,
        "Property declarations are not supported",
        "Property '{0}' is not supported; use fields instead",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Properties generate hidden getter/setter methods that the NES transpiler cannot handle. Use public fields instead.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(NES001Rule, NES002Rule, NES003Rule, NES004Rule, NES005Rule, NES006Rule,
            NES007Rule, NES008Rule, NES009Rule, NES010Rule, NES011Rule, NES012Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeStringExpression, SyntaxKind.AddExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInterpolatedString, SyntaxKind.InterpolatedStringExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ImplicitObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeArrayCreation, SyntaxKind.ArrayCreationExpression, SyntaxKind.ImplicitArrayCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeVariableDeclaration, SyntaxKind.VariableDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunctionStatement, SyntaxKind.LocalFunctionStatement);
        context.RegisterSemanticModelAction(AnalyzeInfiniteLoop);
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        context.RegisterSyntaxNodeAction(AnalyzeLambdaExpression, SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAnonymousMethod, SyntaxKind.AnonymousMethodExpression);
        context.RegisterSyntaxNodeAction(AnalyzeDelegateDeclaration, SyntaxKind.DelegateDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeForEach, SyntaxKind.ForEachStatement);
        context.RegisterSyntaxNodeAction(AnalyzeTryCatchFinally, SyntaxKind.TryStatement);
        context.RegisterSyntaxNodeAction(AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);
    }

    static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;

        // Static classes are just containers for static methods and don't imply instance allocations / object usage
        if (classDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword))
            return;

        context.ReportDiagnostic(Diagnostic.Create(NES002Rule, classDeclaration.Identifier.GetLocation(), classDeclaration.Identifier.Text));
    }

    static void AnalyzeStringExpression(SyntaxNodeAnalysisContext context)
    {
        var binaryExpression = (BinaryExpressionSyntax)context.Node;
        var typeInfo = context.SemanticModel.GetTypeInfo(binaryExpression, context.CancellationToken);
        if (typeInfo.Type?.SpecialType == SpecialType.System_String)
        {
            context.ReportDiagnostic(Diagnostic.Create(NES003Rule, binaryExpression.GetLocation()));
        }
    }

    static void AnalyzeInterpolatedString(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(NES003Rule, context.Node.GetLocation()));
    }

    static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Cheap syntax pre-filter: only consider calls where the member name is one we care about.
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var memberName = memberAccess.Name.Identifier.Text;
        if (memberName is not ("Format" or "Concat" or "Invariant"))
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var method = symbolInfo.Symbol as IMethodSymbol
                     ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (method is null)
            return;

        var containingType = method.ContainingType;
        if (containingType is null)
            return;

        // Detect string.Format(...) and string.Concat(...)
        if (containingType.SpecialType == SpecialType.System_String)
        {
            if (method.Name is "Format" or "Concat")
            {
                context.ReportDiagnostic(Diagnostic.Create(NES003Rule, invocation.GetLocation()));
                return;
            }
        }

        // FormattableString.Invariant(...)
        // Skip when the argument is an interpolated string — AnalyzeInterpolatedString already covers it.
        if (containingType.Name == "FormattableString" &&
            containingType.ContainingNamespace?.ToDisplayString() == "System" &&
            method.Name == "Invariant")
        {
            var hasInterpolatedArg = invocation.ArgumentList.Arguments
                .Any(a => a.Expression is InterpolatedStringExpressionSyntax);
            if (!hasInterpolatedArg)
                context.ReportDiagnostic(Diagnostic.Create(NES003Rule, invocation.GetLocation()));
        }
    }

    static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken);
        var type = typeInfo.Type;
        if (type is null)
            return;

        // Allow struct allocations
        if (type.IsValueType)
            return;

        // Allow byte[] and ushort[]
        // Note: array allocations are ArrayCreationExpression, not ObjectCreationExpression,
        // but this guard handles any case where the type resolves to an array.
        if (type is IArrayTypeSymbol arrayType)
        {
            var elementType = arrayType.ElementType;
            if (elementType.SpecialType == SpecialType.System_Byte || elementType.SpecialType == SpecialType.System_UInt16)
                return;
        }

        context.ReportDiagnostic(Diagnostic.Create(NES004Rule, context.Node.GetLocation()));
    }

    static void AnalyzeArrayCreation(SyntaxNodeAnalysisContext context)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken);
        var type = typeInfo.Type;
        if (type is not IArrayTypeSymbol arrayType)
            return;

        var elementType = arrayType.ElementType;

        // Allow byte[] and ushort[]
        if (elementType.SpecialType == SpecialType.System_Byte || elementType.SpecialType == SpecialType.System_UInt16)
            return;

        // Allow struct arrays
        if (elementType.IsValueType && elementType.TypeKind == TypeKind.Struct && elementType.SpecialType == SpecialType.None)
            return;

        context.ReportDiagnostic(Diagnostic.Create(NES004Rule, context.Node.GetLocation()));
    }


    static void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
    {
        var variableDeclaration = (VariableDeclarationSyntax)context.Node;
        var typeInfo = context.SemanticModel.GetTypeInfo(variableDeclaration.Type, context.CancellationToken);
        var type = typeInfo.Type;
        if (type is null)
            return;

        // Skip array types — those are checked by NES004 at allocation
        if (type is IArrayTypeSymbol)
            return;

        if (!IsSupportedType(type))
        {
            foreach (var variable in variableDeclaration.Variables)
            {
                context.ReportDiagnostic(Diagnostic.Create(NES005Rule, variable.Identifier.GetLocation(), type.ToDisplayString()));
            }
        }
    }

    static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        AnalyzeDllImport(context, method.AttributeLists);
        AnalyzeMethodReturnAndParams(context, method.ReturnType, method.ParameterList);
        AnalyzeRecursion(context, method.Identifier, method.Body, method.ExpressionBody);
    }

    static void AnalyzeLocalFunctionStatement(SyntaxNodeAnalysisContext context)
    {
        var function = (LocalFunctionStatementSyntax)context.Node;
        AnalyzeMethodReturnAndParams(context, function.ReturnType, function.ParameterList);
        AnalyzeRecursion(context, function.Identifier, function.Body, function.ExpressionBody);
    }

    static void AnalyzeDllImport(SyntaxNodeAnalysisContext context, SyntaxList<AttributeListSyntax> attributeLists)
    {
        var dllImportType = context.SemanticModel.Compilation.GetTypeByMetadataName("System.Runtime.InteropServices.DllImportAttribute");
        if (dllImportType is null)
            return;

        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var symbolInfo = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken);
                if (symbolInfo.Symbol is IMethodSymbol attributeConstructor &&
                    SymbolEqualityComparer.Default.Equals(attributeConstructor.ContainingType, dllImportType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(NES006Rule, attribute.GetLocation()));
                    return;
                }
            }
        }
    }

    static void AnalyzeMethodReturnAndParams(SyntaxNodeAnalysisContext context, TypeSyntax returnType, ParameterListSyntax parameterList)
    {
        // Check return type (skip void)
        var returnTypeInfo = context.SemanticModel.GetTypeInfo(returnType, context.CancellationToken);
        if (returnTypeInfo.Type is not null &&
            returnTypeInfo.Type.SpecialType != SpecialType.System_Void &&
            !IsSupportedType(returnTypeInfo.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(NES005Rule, returnType.GetLocation(), returnTypeInfo.Type.ToDisplayString()));
        }

        // Check parameters
        foreach (var parameter in parameterList.Parameters)
        {
            if (parameter.Type is null)
                continue;
            var paramTypeInfo = context.SemanticModel.GetTypeInfo(parameter.Type, context.CancellationToken);
            if (paramTypeInfo.Type is not null && !IsSupportedType(paramTypeInfo.Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(NES005Rule, parameter.GetLocation(), paramTypeInfo.Type.ToDisplayString()));
            }
        }
    }

    static void AnalyzeInfiniteLoop(SemanticModelAnalysisContext context)
    {
        var root = context.SemanticModel.SyntaxTree.GetRoot(context.CancellationToken);
        var compilationUnit = root as CompilationUnitSyntax;
        if (compilationUnit is null)
            return;

        // Only apply to top-level statement programs (no namespace/class structure)
        var globalStatements = compilationUnit.Members.OfType<GlobalStatementSyntax>().ToList();
        if (globalStatements.Count == 0)
            return;

        // Find the last non-local-function statement
        // Local functions in top-level code appear as GlobalStatementSyntax wrapping LocalFunctionStatementSyntax
        StatementSyntax? lastStatement = null;
        for (int i = globalStatements.Count - 1; i >= 0; i--)
        {
            var stmt = globalStatements[i].Statement;
            if (stmt is not LocalFunctionStatementSyntax)
            {
                lastStatement = stmt;
                break;
            }
        }

        if (lastStatement is null)
            return;

        if (IsInfiniteWhileTrue(lastStatement))
            return;

        // Also accept when the last statement calls a local function that itself
        // ends with while (true) — e.g. scroll_demo(); where scroll_demo has the loop
        if (lastStatement is ExpressionStatementSyntax exprStmt
            && exprStmt.Expression is InvocationExpressionSyntax invocation
            && invocation.Expression is IdentifierNameSyntax identifier)
        {
            var calledName = identifier.Identifier.Text;
            var localFunctions = globalStatements
                .Select(gs => gs.Statement)
                .OfType<LocalFunctionStatementSyntax>();
            foreach (var fn in localFunctions)
            {
                if (fn.Identifier.Text == calledName && ContainsInfiniteWhileTrue(fn))
                    return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(NES001Rule, lastStatement.GetLocation()));
    }

    static bool IsInfiniteWhileTrue(StatementSyntax statement)
    {
        if (statement is not WhileStatementSyntax whileStatement)
            return false;

        // Check condition is 'true'
        if (whileStatement.Condition is not LiteralExpressionSyntax literal)
            return false;
        if (!literal.IsKind(SyntaxKind.TrueLiteralExpression))
            return false;

        // Any body is valid — while (true) ; , while (true) { }, while (true) { game_loop(); }
        return true;
    }

    static bool ContainsInfiniteWhileTrue(LocalFunctionStatementSyntax function)
    {
        // Check if the function body or expression contains while (true)
        if (function.Body is not null)
        {
            foreach (var stmt in function.Body.Statements)
            {
                if (IsInfiniteWhileTrue(stmt))
                    return true;
            }
        }
        return false;
    }

    static void AnalyzeRecursion(SyntaxNodeAnalysisContext context, SyntaxToken identifier, BlockSyntax? body, ArrowExpressionClauseSyntax? expressionBody)
    {
        var methodName = identifier.Text;
        SyntaxNode? searchNode = (SyntaxNode?)body ?? expressionBody;
        if (searchNode is null)
            return;

        foreach (var invocation in searchNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? calledName = invocation.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
                _ => null
            };

            if (calledName == methodName)
            {
                context.ReportDiagnostic(Diagnostic.Create(NES007Rule, invocation.GetLocation(), methodName));
            }
        }
    }

    static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var usingDirective = (UsingDirectiveSyntax)context.Node;
        var name = usingDirective.Name?.ToString();
        if (name == "System.Linq")
        {
            context.ReportDiagnostic(Diagnostic.Create(NES008Rule, usingDirective.GetLocation()));
        }
    }

    static void AnalyzeLambdaExpression(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(NES009Rule, context.Node.GetLocation()));
    }

    static void AnalyzeAnonymousMethod(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(NES009Rule, context.Node.GetLocation()));
    }

    static void AnalyzeDelegateDeclaration(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(NES009Rule, context.Node.GetLocation()));
    }

    static void AnalyzeForEach(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(NES010Rule, context.Node.GetLocation()));
    }

    static void AnalyzeTryCatchFinally(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(NES011Rule, context.Node.GetLocation()));
    }

    static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        context.ReportDiagnostic(Diagnostic.Create(NES012Rule, property.Identifier.GetLocation(), property.Identifier.Text));
    }

    static bool IsSupportedType(ITypeSymbol type)
    {
        // byte, sbyte, ushort are supported
        switch (type.SpecialType)
        {
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_UInt16:
            case SpecialType.System_Void:
            case SpecialType.System_Boolean:
            case SpecialType.System_Int32:  // int is used as intermediate type in expressions
                return true;
        }

        // Arrays of supported types
        if (type is IArrayTypeSymbol arrayType)
        {
            return IsSupportedType(arrayType.ElementType);
        }

        // User-defined structs are supported
        if (type.IsValueType && type.TypeKind == TypeKind.Struct && type.SpecialType == SpecialType.None)
        {
            return true;
        }

        // Enum types (they're value types backed by int/byte)
        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        // String is allowed (only as literals passed to NESLib)
        if (type.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        return false;
    }
}
