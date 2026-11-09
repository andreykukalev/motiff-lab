#:package Microsoft.CodeAnalysis.CSharp@5.3.0
#:property PublishAot=false

// Method length for C# (#332): the .NET analyzers measure complexity, maintainability and coupling, but not
// length. A syntax-only pass, no compilation: `dotnet run tools/quality/csharp-length.cs -- <max>` reads file
// paths from standard input, one per line, and prints one JSON line per method, constructor, operator, accessor
// or local function longer than <max> lines.
// Lines are counted from the declaration to its closing brace, comments and blank lines included, as ESLint's
// max-lines-per-function counts them. Run by tools/quality/quality.mjs.
using System.Text.Json;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Length != 1 || !int.TryParse(args[0], out var max))
{
    Console.Error.WriteLine("usage: csharp-length.cs <max-lines> < file-list");
    return 2;
}

while (Console.ReadLine() is { } path)
{
    if (path.Length == 0)
    {
        continue;
    }

    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
    foreach (var node in tree.GetRoot().DescendantNodes().Where(IsFunction))
    {
        var span = node.GetLocation().GetLineSpan();
        var lines = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        if (lines > max)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                file = path,
                line = span.StartLinePosition.Line + 1,
                symbol = NameOf(node),
                lines,
            }));
        }
    }
}

return 0;

static bool IsFunction(SyntaxNode node) =>
    node is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax;

static string NameOf(SyntaxNode node)
{
    var own = node switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text,
        DestructorDeclarationSyntax d => "~" + d.Identifier.Text,
        OperatorDeclarationSyntax o => "operator " + o.OperatorToken.Text,
        ConversionOperatorDeclarationSyntax c => "operator " + c.Type,
        AccessorDeclarationSyntax a => $"{MemberOf(a)}.{a.Keyword.Text}",
        LocalFunctionStatementSyntax l => l.Identifier.Text,
        _ => "?",
    };
    var outer = node.Ancestors()
        .Select(a => a switch
        {
            BaseTypeDeclarationSyntax t => t.Identifier.Text,
            MethodDeclarationSyntax m when node is LocalFunctionStatementSyntax => m.Identifier.Text,
            LocalFunctionStatementSyntax l => l.Identifier.Text,
            _ => null,
        })
        .OfType<string>()
        .Reverse();
    return string.Join('.', outer.Append(own));
}

static string MemberOf(AccessorDeclarationSyntax accessor) => accessor.Parent?.Parent switch
{
    PropertyDeclarationSyntax p => p.Identifier.Text,
    EventDeclarationSyntax e => e.Identifier.Text,
    IndexerDeclarationSyntax => "this[]",
    _ => "?",
};