using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace VSlices.SemanticProject.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SemanticBoundaryAnalyzer : DiagnosticAnalyzer
{
    public const string DependencyId = "SPA001";
    public const string VisibilityId = "SPA002";
    public const string ConfigurationId = "SPA003";

    private static readonly DiagnosticDescriptor DependencyRule = new(
        DependencyId, "Semantic boundary dependency is forbidden",
        "Boundary '{0}' cannot reference '{1}'",
        "Architecture", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor VisibilityRule = new(
        VisibilityId, "Internal symbol is not visible across semantic boundaries",
        "Internal type '{0}' belongs to boundary '{1}' and is not visible from '{2}'",
        "Architecture", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ConfigurationRule = new(
        ConfigurationId, "Invalid semantic boundary configuration",
        "Invalid SemanticProjectDomainDependencies entry '{0}' (expected Source>Target1,Target2)",
        "Configuration", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DependencyRule, VisibilityRule, ConfigurationRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var options = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (!options.TryGetValue("build_property.SemanticProjectRootNamespace", out var root) ||
                string.IsNullOrWhiteSpace(root))
                return; // Opt-in: no accidental classification of unrelated assemblies.

            options.TryGetValue("build_property.SemanticProjectDomainDependencies", out var dependencyText);
            var configuration = BoundaryConfiguration.Parse(root.Trim(), dependencyText ?? "");
            foreach (var invalid in configuration.InvalidEntries)
            {
                start.RegisterCompilationEndAction(end =>
                    end.ReportDiagnostic(Diagnostic.Create(ConfigurationRule, Location.None, invalid)));
            }

            start.RegisterSyntaxNodeAction(ctx => AnalyzeReference(ctx, configuration),
                SyntaxKind.IdentifierName, SyntaxKind.GenericName);
        });
    }

    private static void AnalyzeReference(SyntaxNodeAnalysisContext context, BoundaryConfiguration config)
    {
        var reference = (SimpleNameSyntax)context.Node;
        // Qualified names are analyzed at the rightmost terminal; skip their left parts.
        if (reference.Parent is QualifiedNameSyntax q && q.Left == reference)
            return;

        var symbol = context.SemanticModel.GetSymbolInfo(reference, context.CancellationToken).Symbol;
        if (symbol is IAliasSymbol alias) symbol = alias.Target;
        if (symbol is null || symbol is INamespaceSymbol) return;

        var declaringType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        if (declaringType is null) return;

        // Only govern symbols declared in this compilation. Other assemblies keep CLR rules.
        if (!SymbolEqualityComparer.Default.Equals(declaringType.ContainingAssembly, context.Compilation.Assembly))
            return;

        var sourceSymbol = context.SemanticModel.GetEnclosingSymbol(reference.SpanStart, context.CancellationToken);
        if (sourceSymbol is null) return;
        if (!config.TryClassify(sourceSymbol.ContainingNamespace, out var source) ||
            !config.TryClassify(declaringType.ContainingNamespace, out var target) ||
            source.Equals(target))
            return;

        if (IsInternal(declaringType))
        {
            context.ReportDiagnostic(Diagnostic.Create(VisibilityRule, reference.GetLocation(),
                declaringType.ToDisplayString(), target.ToString(), source.ToString()));
            return;
        }

        if (!config.CanReference(source, target))
            context.ReportDiagnostic(Diagnostic.Create(DependencyRule, reference.GetLocation(),
                source.ToString(), target.ToString()));
    }

    private static bool IsInternal(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility == Accessibility.Internal ||
                current.DeclaredAccessibility == Accessibility.Private ||
                current.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
                return true;
        return false;
    }

    private readonly struct Boundary : IEquatable<Boundary>
    {
        public Boundary(string domain, string layer) { Domain = domain; Layer = layer; }
        public string Domain { get; }
        public string Layer { get; }
        public bool Equals(Boundary other) => Domain == other.Domain && Layer == other.Layer;
        public override bool Equals(object? obj) => obj is Boundary b && Equals(b);
        public override int GetHashCode() => (Domain + "." + Layer).GetHashCode();
        public override string ToString() => Domain + "." + Layer;
    }

    private sealed class BoundaryConfiguration
    {
        private readonly string[] _root;
        private readonly Dictionary<string, HashSet<string>> _allowed;
        private static readonly HashSet<string> Layers = new(StringComparer.Ordinal)
            { "Spaces", "Work", "Grounding" };

        private BoundaryConfiguration(string[] root, Dictionary<string, HashSet<string>> allowed,
            ImmutableArray<string> invalidEntries)
        { _root = root; _allowed = allowed; InvalidEntries = invalidEntries; }

        public ImmutableArray<string> InvalidEntries { get; }

        public static BoundaryConfiguration Parse(string root, string text)
        {
            var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var invalid = ImmutableArray.CreateBuilder<string>();
            foreach (var entry in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Trim().Split('>');
                if (parts.Length != 2 || !IsSegment(parts[0].Trim()) ||
                    parts[1].Split(',').Any(s => !IsSegment(s.Trim())))
                { invalid.Add(entry.Trim()); continue; }

                var from = parts[0].Trim();
                if (!dependencies.TryGetValue(from, out var targets))
                    dependencies[from] = targets = new HashSet<string>(StringComparer.Ordinal);
                foreach (var target in parts[1].Split(','))
                    targets.Add(target.Trim());
            }
            return new BoundaryConfiguration(root.Split('.'), dependencies, invalid.ToImmutable());
        }

        private static bool IsSegment(string s) =>
            s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') &&
            s.All(c => char.IsLetterOrDigit(c) || c == '_');

        public bool TryClassify(INamespaceSymbol ns, out Boundary boundary)
        {
            boundary = default;
            if (ns.IsGlobalNamespace) return false;
            var name = ns.ToDisplayString().Split('.');
            if (name.Length < _root.Length + 2) return false;
            for (var i = 0; i < _root.Length; i++)
                if (name[i] != _root[i]) return false;
            var layer = name[_root.Length + 1];
            if (!Layers.Contains(layer)) return false;
            boundary = new Boundary(name[_root.Length], layer);
            return true;
        }

        public bool CanReference(Boundary source, Boundary target)
        {
            // Cross-domain dependencies are explicit; layer direction remains enforced.
            if (source.Domain != target.Domain &&
                (!_allowed.TryGetValue(source.Domain, out var set) || !set.Contains(target.Domain)))
                return false;
            return Rank(source.Layer) >= Rank(target.Layer);
        }

        private static int Rank(string layer) =>
            layer == "Spaces" ? 0 : layer == "Work" ? 1 : 2;
    }
}
