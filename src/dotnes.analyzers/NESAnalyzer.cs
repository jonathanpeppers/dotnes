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
        "Classes and objects are not supported",
        "Type '{0}' uses classes/objects which are not supported on the NES",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The NES has no heap or garbage collector, so classes and objects cannot be used.");

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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(NES001Rule, NES002Rule, NES003Rule, NES004Rule, NES005Rule, NES006Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeStringExpression, SyntaxKind.AddExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInterpolatedString, SyntaxKind.InterpolatedStringExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ImplicitObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeVariableDeclaration, SyntaxKind.VariableDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunctionStatement, SyntaxKind.LocalFunctionStatement);
        context.RegisterSemanticModelAction(AnalyzeInfiniteLoop);
    }

    static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
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
    }

    static void AnalyzeLocalFunctionStatement(SyntaxNodeAnalysisContext context)
    {
        var function = (LocalFunctionStatementSyntax)context.Node;
        AnalyzeMethodReturnAndParams(context, function.ReturnType, function.ParameterList);
    }

    static void AnalyzeDllImport(SyntaxNodeAnalysisContext context, SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var symbolInfo = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken);
                if (symbolInfo.Symbol is IMethodSymbol attributeConstructor &&
                    attributeConstructor.ContainingType.ToDisplayString() == "System.Runtime.InteropServices.DllImportAttribute")
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
