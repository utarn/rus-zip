# 0019: Hybrid Deployment Strategy — Standard JIT for CLI and ReadyToRun for Desktop

## Status

Accepted

## Context

As `rus-zip` approaches production readiness across Windows (`win-x64`), Linux (`linux-x64`), and macOS (`osx-arm64`, `osx-x64`), the binary compilation and publishing strategy required architectural evaluation regarding startup latency, binary distribution size, and platform compatibility.

Two primary ahead-of-time technologies were investigated:
1. **Native AOT (`PublishAot=true`)**:
   - Compiles managed C# directly to platform-native machine code without a JIT compiler.
   - *Investigation Findings*:
     - **Cross-OS Build Failure**: .NET SDK ILCompiler does not support cross-OS linking out of the box (`error : Cross-OS native compilation is not supported.`), preventing `win-x64` builds on Linux/macOS build agents without Windows runners.
     - **CLI Runtime Crash**: `RusZip.Cli` crashes immediately on startup (`Spectre.Console.Cli.CommandRuntimeException: Could not get settings type`) due to reflection-based command discovery stripped by trimming.
     - **Desktop Reflection Violations**: `RusZip.Desktop` produces multiple `IL2026`/`IL3050`/`IL2075` trim warnings from `ProDataGrid` / `HierarchicalModel`, XAML reflection bindings, and JSON serialization.
2. **ReadyToRun (R2R, `PublishReadyToRun=true`)**:
   - Pre-compiles managed assemblies to native code ahead-of-time while maintaining 100% full CLR JIT runtime and reflection support.
   - Fully supports cross-OS publishing (e.g. building Windows R2R binaries from Linux).
   - *Investigation Findings (Empirical Single-File Executable Sizes)*:
     - **`RusZip.Cli`**: Standard JIT is **~74 MB**; R2R increases size to **~94 MB** (+26% to +31%).
     - **`RusZip.Desktop`**: Standard JIT is **~105 MB**; R2R increases size to **~165 MB** (+56% to +66%).

## Decision

Adopt a **Hybrid Deployment Strategy** tailored to the usage characteristics of each target application:

1. **`RusZip.Cli` — Standard JIT (`PublishReadyToRun=false`)**:
   - Prioritize lean single-file binary distribution size (~74 MB).
   - Command-line startup latency is already sub-60ms under standard JIT, making the +20 MB binary size overhead of R2R unjustified for CLI workflows.

2. **`RusZip.Desktop` — ReadyToRun (`PublishReadyToRun=true`, `PublishReadyToRunShowWarnings=true`)**:
   - Prioritize cold-launch snappiness, instantaneous window activation, and smooth initial UI rendering by pre-compiling Avalonia, ProDataGrid, ProCharts, and view models.
   - Enable `PublishReadyToRunShowWarnings=true` to alert developers if any assembly fails pre-compilation.

3. **Project File and Script Synchronization**:
   - Declare the deployment defaults directly in `src/RusZip.Cli/RusZip.Cli.csproj` and `src/RusZip.Desktop/RusZip.Desktop.csproj`.
   - Mirror these flags explicitly in `scripts/publish.sh` and `scripts/publish.ps1`.

## Consequences

- **Positive**:
  - `RusZip.Cli` remains lean and fast to download and deploy in CI/terminal environments.
  - `RusZip.Desktop` achieves instant cold startup without UI stutter during window creation.
  - 100% full C# reflection, Avalonia bindings, and Spectre.Console compatibility is preserved.
  - Full cross-OS publishing capability is maintained across Linux, macOS, and Windows.
- **Negative / Trade-offs**:
  - `RusZip.Desktop` executable size increases by ~60 MB (~165 MB vs ~105 MB) due to pre-compiled native code sections.

## Implementation Reference

### `src/RusZip.Cli/RusZip.Cli.csproj`
```xml
<PropertyGroup>
  <PublishReadyToRun>false</PublishReadyToRun>
</PropertyGroup>
```

### `src/RusZip.Desktop/RusZip.Desktop.csproj`
```xml
<PropertyGroup>
  <PublishReadyToRun>true</PublishReadyToRun>
  <PublishReadyToRunShowWarnings>true</PublishReadyToRunShowWarnings>
</PropertyGroup>
```
