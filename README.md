# Semantic Project Analyzer

**Semantic boundaries should not require a dedicated `.csproj`.**

Experimental Roslyn analyzer that enforces boundaries between business **domains** and their **Spaces / Work / Grounding** layers even when they share an assembly. A .NET project is a physical build/publish unit; semantic domains and deployable units can be arranged independently.

## Quick start

Reference the analyzer as an analyzer project:

```xml
<ItemGroup>
  <ProjectReference Include="../semantic-project-analyzer/src/VSlices.SemanticProject.Analyzers/VSlices.SemanticProject.Analyzers.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <CompilerVisibleProperty Include="SemanticProjectRootNamespace" />
  <CompilerVisibleProperty Include="SemanticProjectDomainDependencies" />
</ItemGroup>
<PropertyGroup>
  <SemanticProjectRootNamespace>Serviu</SemanticProjectRootNamespace>
  <SemanticProjectDomainDependencies>Tickets>Accounts,Identities</SemanticProjectDomainDependencies>
</PropertyGroup>
```

For XML, write the `>` character in property text normally (or escape it as `&gt;` if desired).

Example namespaces: `Serviu.Tickets.Spaces`, `Serviu.Tickets.Work`, `Serviu.Tickets.Grounding`, `Serviu.Accounts.Spaces`. The analyzer checks references by Roslyn symbols, not file names.

Run tests:

```sh
dotnet test tests/VSlices.SemanticProject.Analyzers.Tests/VSlices.SemanticProject.Analyzers.Tests.csproj
```

See [semantic contract](docs/semantics.md) for the provisional policy, diagnostics, guarantees, and known limitations.
