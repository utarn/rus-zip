# 0002: Four-Project Solution Architecture for .NET 10

We decided to structure the repository into four distinct projects separating core domain logic, CLI, desktop UI, and tests.

## Context
`rus-zip` needs to ship both a standalone CLI executable (optimized for terminal use and AI agents) and a desktop GUI application (built with Avalonia UI), along with a core compression engine that can be unit-tested without UI or terminal dependencies.

## Decision
The solution is organized into:
1. `src/RusZip.Core/` — Class library containing pure domain models, archive abstractions (`IArchiveEngine`), streaming pipelines, and format handlers. Zero GUI or CLI framework dependencies.
2. `src/RusZip.Cli/` — Standalone CLI executable built with `Spectre.Console.Cli`, providing rich `--help` with examples, zero-argument help fallback, and `--json` machine-readable output.
3. `src/RusZip.Desktop/` — Cross-platform desktop application built with Avalonia UI (Fluent theme, MVVM pattern, reactive state, asynchronous progress/cancellation).
4. `tests/RusZip.Core.Tests/` — Unit and integration test suite (xUnit) verifying compression/decompression across `.zrus`, `.zip`, `.rar`, `.7z`, `.gz`, `.tar.gz`.

## Consequences
CLI and Desktop binaries are decoupled and independently buildable, publishable, and testable.
