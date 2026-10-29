# Semantic boundary contract — experiment v0

## Distinct concepts

- **Domain**: semantic responsibility, not inherently a project or deployment unit.
- **Spaces / Work / Grounding**: semantic layers inside a domain.
- **Project/assembly**: physical build boundary; may contain several domains, or one domain may span several projects.
- **Deployable**: an operational boundary chosen independently of the semantic partition. Each separately deployed application still requires an appropriate build/publish entry point, whether or not its sources are grouped with other projects.

## v0 implemented convention

Opt-in per compilation using the MSBuild property `SemanticProjectRootNamespace`. A namespace `<root>.<domain>.<layer>[.<subnamespace>]` classifies a declaration in one semantic boundary, where layer is exactly `Spaces`, `Work`, or `Grounding`.

Within a domain, permitted reference directions are:

| Consumer | May reference |
| --- | --- |
| Spaces | Spaces |
| Work | Spaces, Work |
| Grounding | Spaces, Work, Grounding |

Across domains, access is rejected by default. Opt in via `SemanticProjectDomainDependencies` entries such as `Tickets>Accounts,Identities;Accounts>Identities`. Even explicitly allowed cross-domain references must respect the layer direction above. This is a **provisional experiment policy**, not a settled VSlices architectural law.

`internal` types are semantically private to their `(domain, layer)` boundary even when the compiler considers them accessible because they share an assembly. `public` types may cross only permitted dependency boundaries. Namespace nesting does not create additional barriers.

## Diagnostics

- `SPA001`: disallowed dependency across domains or layers.
- `SPA002`: internal type used outside its semantic boundary.
- `SPA003`: malformed explicit domain dependency entry.

All diagnostics are errors. The analyzer is opt-in, so unconfigured compilations retain their normal C# semantics.

## Honest limitations

- **Intra-compilation only** in v0: references to declarations from separately compiled assemblies are not yet analyzed. This matters when a domain spans multiple projects. The full design must support metadata-backed boundary declarations and cross-compilation enforcement before claiming project-independent equivalence.
- Namespace pattern is an initial convention; it does not model arbitrary source path mappings, partial declarations with contradictory identities, namespace aliases in every context, or non-C# languages.
- `internal` checks cover named types and their containing types in ordinary symbol references. Member-level `internal` visibility and sophisticated accessibility cases are not yet modeled.
- No runtime security boundary: reflection and binaries compiled without the analyzer can bypass these diagnostics.
- `using` directives are not independently validated; actual resolved symbol references are checked.
- No performance claim yet: benchmark clean, incremental, and IDE load costs before consolidation.
- CI build/test verification is required; this first push has not been compiled locally if a .NET SDK is unavailable.

## Next experiments

1. Add explicit domain exports/imports and metadata identity across assemblies.
2. Add declaration coverage diagnostics for unclassified source inside opted-in roots.
3. Measure overhead and false positives against a real three-project VSlices domain.
4. Refine directional policies from actual Spaces/Work/Grounding invariants rather than generic layering assumptions.
