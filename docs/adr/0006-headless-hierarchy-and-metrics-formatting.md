# 0006: Headless Hierarchy Projection and Shared Metrics Formatting

We decided to move tree hierarchy projection and data metric calculations from UI/CLI layers into headless modules in `RusZip.Core`.

## Context
1. `ArchiveBrowserViewModel` contained ~70 lines of path tokenization, parent-child linking, and rollup size calculations tightly coupled to Avalonia `ObservableObject` and `TreeDataGrid`.
2. Byte formatting (`FormatBytes`), speed smoothing (EMA), and ETA calculations were duplicated across `CliProgressBridge`, `ArchiveItemViewModel`, and `OperationProgressViewModel`.

## Decision
1. Introduce `ArchiveTreeNode` and `ArchiveHierarchy.BuildTree` in `RusZip.Core.Models` to produce pure domain tree representations from flat archive entry lists with automated size rollups.
2. Introduce `DataMetricsFormatter` (stateless unit and ratio formatting) and `ThroughputTracker` (stateful operation timing and EMA throughput smoothing) in `RusZip.Core.Models`.
3. Introduce `CliCommandRunner` in `RusZip.Cli.Infrastructure` to encapsulate command timing, progress bar lifecycles, and exception-to-exit-code mapping.

## Consequences
Completely decouples domain tree generation and throughput math from UI frameworks. View models become thin adapters over domain records, enabling automated headless testing of complex tree operations.
