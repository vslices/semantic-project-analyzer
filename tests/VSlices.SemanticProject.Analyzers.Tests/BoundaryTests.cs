using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using VSlices.SemanticProject.Analyzers;
using Xunit;

namespace VSlices.SemanticProject.Analyzers.Tests;

public sealed class BoundaryTests
{
    [Theory]
    [InlineData("namespace Example.Tickets.Spaces { public class DomainType {} } namespace Example.Tickets.Work { public class Handler { DomainType value; } }", null)]
    [InlineData("namespace Example.Tickets.Grounding { public class Adapter {} } namespace Example.Tickets.Spaces { public class DomainType { Adapter a; } }", "SPA001")]
    [InlineData("namespace Example.Tickets.Work { public class Handler {} } namespace Example.Tickets.Spaces { public class DomainType { Handler h; } }", "SPA001")]
    [InlineData("namespace Example.Accounts.Spaces { public class Account {} } namespace Example.Tickets.Work { public class Handler { Account a; } }", "SPA001")]
    [InlineData("namespace Example.Tickets.Spaces { internal class State {} } namespace Example.Tickets.Work { public class Handler { State s; } }", "SPA002")]
    [InlineData("namespace Example.Tickets.Spaces { internal class State {} public class SameSpace { State s; } }", null)]
    public async Task Enforces_semantic_boundaries(string source, string? expected)
    {
        // Use fully qualified names in source to resolve across namespaces.
        source = source.Replace("DomainType value;", "Example.Tickets.Spaces.DomainType value;")
            .Replace("Adapter a;", "Example.Tickets.Grounding.Adapter a;")
            .Replace("Handler h;", "Example.Tickets.Work.Handler h;")
            .Replace("Account a;", "Example.Accounts.Spaces.Account a;")
            .Replace("State s;", "Example.Tickets.Spaces.State s;");
        var ids = await Diagnose(source);
        Assert.Equal(expected is null ? Array.Empty<string>() : new[] { expected },
            ids.Distinct().OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task Explicit_domain_permission_allows_cross_domain_access()
    {
        var diagnostics = await Diagnose(
            "namespace Example.Accounts.Spaces { public class Account {} } namespace Example.Tickets.Work { public class Handler { Example.Accounts.Spaces.Account a; } }",
            "Tickets>Accounts");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Projects_without_opt_in_are_not_analyzed()
    {
        var diagnostics = await Diagnose(
            "namespace Example.Tickets.Grounding { public class Adapter {} } namespace Example.Tickets.Spaces { public class State { Example.Tickets.Grounding.Adapter a; } }",
            enabled: false);
        Assert.Empty(diagnostics);
    }

    private static async Task<string[]> Diagnose(string source, string dependencies = "", bool enabled = true)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = new[] { typeof(object).Assembly.Location, typeof(Enumerable).Assembly.Location }
            .Distinct().Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("BoundaryFixture", new[] { tree }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var global = new Dictionary<string, string>();
        if (enabled) global["build_property.SemanticProjectRootNamespace"] = "Example";
        global["build_property.SemanticProjectDomainDependencies"] = dependencies;
        var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty,
            new OptionsProvider(global));
        var analyzer = new SemanticBoundaryAnalyzer();
        var diagnostics = await compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
            new CompilationWithAnalyzersOptions(options, null, true, false, false))
            .GetAnalyzerDiagnosticsAsync();
        return diagnostics.Select(d => d.Id).ToArray();
    }

    private sealed class OptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global;
        public OptionsProvider(Dictionary<string, string> values) => _global = new MapOptions(values);
        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _global;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _global;
    }

    private sealed class MapOptions : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> _values;
        public MapOptions(Dictionary<string, string> values) => _values = values;
        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    }
}
