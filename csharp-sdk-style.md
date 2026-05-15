# Idiomatic C# for Public SDKs

Opinionated. Authoritative. Optimized for maintainability and consumer
ergonomics. When in doubt, choose the option that makes the consumer's
IntelliSense more correct and their stack traces more honest.

---

## 1. Project & Solution Layout

- One public type per file. Filename matches type name exactly.
- File-scoped namespaces (`namespace Foo.Bar;`). No exceptions.
- Folder structure mirrors namespace structure.
- `internal` is the default access modifier. `public` is a deliberate,
  reviewed decision — every public symbol is a permanent contract.
- `sealed` is the default for classes. Inheritance is opt-in and documented.
- Separate `Abstractions/` project for interfaces consumed by callers.
- `InternalsVisibleTo` only to test projects. Never to siblings.
- `Directory.Build.props` lifts shared settings up; projects override only
  when they must.

---

## 2. Project File Settings (non-negotiable)

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
  <IsAotCompatible>true</IsAotCompatible>
</PropertyGroup>
```

- Source Link + deterministic builds for every published package.
- Publish `.snupkg` with symbols.
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` on every public-facing
  project. `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` are
  reviewed like code.

---

## 3. Async — Public API Rules

- Every awaitable-returning method ends in `Async`.
- Every public async method takes `CancellationToken ct = default` as
  the **last** parameter.
- Return `Task` / `Task<T>` for general use. `ValueTask<T>` only when
  the synchronous path is common and measured.
- `ConfigureAwait(false)` on every await in library code.
- Never `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`. Ever.
- `async void` is banned except for genuine event handlers.
- Do not expose `Task` from properties.
- Sync-over-async and async-over-sync are both wrong. Expose what the
  underlying work actually is.

---

## 4. Naming

- `PascalCase`: types, methods, properties, events, public fields, consts.
- `camelCase`: parameters, locals.
- `_camelCase`: private/protected fields.
- `I` prefix for interfaces.
- `T` prefix for generic type parameters (`T`, `TKey`, `TValue`, `TResult`).
- 2-letter acronyms uppercase (`IOStream`). 3+ letters Pascal (`HttpClient`).
- No Hungarian notation. No `m_`. No abbreviations the consumer must guess.
- `nameof(x)` always over string literals.

---

## 5. Type Design

- Prefer `record` for DTOs and value-like data.
- Prefer `readonly record struct` for small (≤16 byte) value types.
- Init-only properties for object initializers; constructor for required state.
- Validate in constructors. Throw `ArgumentNullException.ThrowIfNull(x)`,
  `ArgumentException.ThrowIfNullOrWhiteSpace(s)`,
  `ArgumentOutOfRangeException.ThrowIfNegative(n)`.
- Public surface returns `IReadOnlyList<T>`, `IReadOnlyDictionary<K,V>`,
  `IReadOnlyCollection<T>`. Never `List<T>`, `Dictionary<K,V>`,
  `T[]` (unless an array is the literal abstraction).
- Public surface accepts the narrowest interface that satisfies the need:
  `IEnumerable<T>` for iterate-once, `IReadOnlyList<T>` for indexed access.
- No public mutable static state.
- No singletons via `Instance` field. Register through DI.

---

## 6. Errors

- Throw the most specific exception type. Never throw bare `Exception`.
- Custom exceptions inherit from `Exception`. Suffix `Exception`.
- Include `paramName` in arg exceptions; use `nameof`.
- Don't catch what you can't handle. Catch `Exception` only at framework
  boundaries (host, message pump, top-of-pipeline).
- `try`/`catch when (filter)` for predicate-based handling.
- `TryX(out T result)` pattern when failure is an expected branch in
  hot paths.
- Never swallow exceptions silently. If intentional, log at debug+ with
  the exception and a one-line justification.

---

## 7. Dependency Injection

- Constructor injection only. No service locator. No property injection.
- Public SDKs ship an `IServiceCollection` extension:
  `services.AddMyLibrary(opts => { ... })`.
- Use `IOptions<T>` / `IOptionsMonitor<T>` for configuration.
- Honor caller's `ILoggerFactory`. Do not construct loggers internally.
- Register concrete types behind interfaces. Register interfaces; resolve
  interfaces. Lifetimes are explicit (`Singleton` / `Scoped` / `Transient`)
  and documented in the registration method.
- Primary constructors (C# 12) are acceptable for DI when the type has
  no other constructors.

---

## 8. Logging

- `ILogger<T>` from `Microsoft.Extensions.Logging`.
- Source-generated logging via `[LoggerMessage]` for every hot path.
- Structured templates only: `_logger.LogInformation("User {UserId} signed in", id)`.
  Never `$"User {id} signed in"`.
- Never `Console.WriteLine` in library code.
- Event IDs are stable. Catalog them.

---

## 9. Modern C# (use these)

- File-scoped namespaces.
- Primary constructors for DI-only types.
- Collection expressions: `int[] x = [1, 2, 3];`.
- Target-typed `new()` when the type is on the left and obvious.
- Pattern matching, switch expressions, list patterns.
- `using` declarations (no extra brace nesting).
- Top-level statements only in `Program.cs`.
- `required` modifier for properties that must be set.
- Raw string literals (`"""..."""`) for embedded JSON/SQL/XML.

---

## 10. Avoid

- `dynamic`. Always.
- `region` directives. If you need them, the file is too big.
- `params object[]` on public APIs. Use overloads or a typed params array.
- Implicit operators that lose information.
- Extension methods on `object` or `Task`.
- Mutable struct.
- Reflection in hot paths — use source generators.
- Tuples as return types when `>2` values or when consumers will hold
  the result — use a `record`.
- Flag parameters (`DoThing(bool isFast)`). Split into two methods.
- `default!` to silence nullable warnings. Fix the model.

---

## 11. Performance Patterns

- `Span<T>` / `ReadOnlySpan<T>` for parsing hot paths.
- `ArrayPool<T>.Shared` for transient buffers in hot paths.
- `StringBuilder` for loops; `string.Concat`/interp for fixed counts.
- LINQ allocates iterators — avoid in measured hot paths; prefer `for`/`foreach`.
- Beware boxing on `struct → interface`. Generic constraints fix this.
- `IEnumerable<T>` is lazy. Materialize at the boundary, exactly once.

---

## 12. LINQ Style

- Method syntax, never query syntax.
- One operator per line once you exceed two.
- Materialize at the consumer boundary (`.ToList()` / `.ToArray()`), not deep
  inside helpers.
- Don't chain across side-effects.

---

## 13. Documentation

- XML doc on every `public` and `protected` member. CS1591 is an error.
- `<summary>` is one sentence, present tense, describes the contract.
- `<param>`, `<returns>`, `<exception>` for every non-trivial member.
- `<example>` for any non-obvious usage.
- Public types get an `<remarks>` section if thread-safety or lifetime
  semantics matter.

---

## 14. Versioning & Compatibility

- SemVer, strictly.
- Adding a member is a minor bump. Changing a signature is a major bump.
- `[Obsolete("Use X instead.", error: false)]` for one minor cycle, then
  `error: true` in the next major, then remove.
- `PublicAPI.Unshipped.txt` updated in the same PR as the API change.
- Floating `PackageReference` versions are forbidden in shipping projects.

---

## 15. Testing

- xUnit.
- Test naming: `Method_State_ExpectedBehavior` or
  `Should_ExpectedBehavior_When_State`.
- One behavior per test. Multiple asserts allowed only when they describe
  one observable outcome.
- Test the public API. Internal tests via `InternalsVisibleTo` are allowed
  but every internal test is a smell — prefer raising the test boundary.
- `FluentAssertions` for readability.
- Deterministic. No `Thread.Sleep`. No real clocks — inject `TimeProvider`.

---

## 16. Formatting

- `.editorconfig` at solution root is the source of truth.
- 4-space indentation. No tabs.
- **Line length: aspire to 80, hard cap 100.**
- Braces always, even for one-line bodies.
- One statement per line.
- Spaces around binary operators.
- `using` directives sorted; `System.*` first; one blank line between groups.
- Blank line between members.
- No trailing whitespace. Final newline at EOF. LF line endings.

---

## 17. Reducing Complexity — Hard Limits

These are limits, not targets. Targets are tighter.

| Metric                       | Hard Limit | Target |
|------------------------------|-----------:|-------:|
| Lines per file               |        300 |    200 |
| Lines per method (body)      |         30 |     20 |
| Cyclomatic complexity        |         10 |      6 |
| Parameters per method        |          4 |      3 |
| Nesting depth                |          3 |      2 |
| Public members per class     |         15 |     10 |

Rules of thumb when a limit is hit:

- **File too long** → split by responsibility. Use `partial` only for
  generated code or genuinely cohesive types whose split is mechanical.
- **Method too long** → extract sub-methods with names that read as
  prose. If you wrote a comment explaining a block, that block is the
  next method.
- **Complexity too high** → flatten with early returns; replace if-else
  ladders with `switch` expressions or polymorphism; extract guard clauses.
- **Too many params** → parameter `record` (`record CreateUserRequest(...)`).
- **Nesting too deep** → invert conditions and return early.
- **Too many public members** → the class is doing two jobs. Split.

One reason to change per class. One job per method. Names carry weight.
