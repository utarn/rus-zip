# 0008: ProDataGrid and ProCharts Desktop Migration

We decided to migrate the desktop archive viewer from `Avalonia.Controls.TreeDataGrid` to `ProDataGrid` (12.1.0.4) and adopt `ProCharts.Avalonia` (12.1.0.4) for live throughput telemetry, managed via Central Package Management (`Directory.Packages.props`), while keeping `RusZip.Core` purely headless.

## Context

1. `RusZip.Desktop` previously used `Avalonia.Controls.TreeDataGrid` (11.1.1) with C#-constructed column models in `ArchiveBrowserViewModel`. While functional, declarative XAML styling, column reordering, and advanced cell virtualization are better supported by `ProDataGrid`.
2. Long-running archive compression and extraction operations in `ProgressOverlay` only showed text-based throughput statistics (`FormattedSpeed`, `FormattedEta`), without visual feedback on I/O burstiness or sustained transfer rates.
3. Scaling across multiple interrelated UI packages (Avalonia, `ProDataGrid`, `ProCharts`) increases the risk of version drift between desktop and test projects.

## Decision

1. **Central Package Management (CPM)**: Introduce `Directory.Packages.props` at the repository root to centrally lock all Avalonia, `ProDataGrid` (12.1.0.4), and `ProCharts.Avalonia` (12.1.0.4) versions across `RusZip.Desktop.csproj` and `RusZip.Desktop.Tests.csproj`.
2. **ProDataGrid Archive Browser**: Replace `TreeDataGrid` in `src/RusZip.Desktop/Views/ArchiveBrowserView.axaml` and `src/RusZip.Desktop/ViewModels/ArchiveBrowserViewModel.cs` with `ProDataGrid` using declarative hierarchical columns (`DataGridHierarchicalColumn`) and `HierarchicalRowsEnabled="True"`.
3. **Directory-Preserving Hierarchical Sort**: Ensure hierarchical sorting always groups directory nodes above file nodes within each branch across all sorted columns.
4. **Live Throughput Telemetry**: Integrate `ProCharts.Avalonia` into `src/RusZip.Desktop/Views/ProgressOverlay.axaml` with a rolling throughput velocity series (MB/s over time) populated from `ThroughputTracker` in `src/RusZip.Desktop/ViewModels/OperationProgressViewModel.cs`.
5. **Headless Domain Boundary Preservation**: `RusZip.Core` and `RusZip.Cli` remain 100% headless C# with zero dependencies on Avalonia, Skia, or `Pro*` packages. All UI adapters, hierarchical row structures, and chart data points reside strictly in `RusZip.Desktop`.
6. **Guardrail Defense-in-Depth**: Retain `EntryCountCap` (ADR-0007 F-36) in `ArchiveBrowserViewModel.LoadEntries` to reject visual tree construction for hostile archives exceeding 1,000,000 entries.

```xml
<!-- Directory.Packages.props snippet -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="ProDataGrid" Version="12.1.0.4" />
    <PackageVersion Include="ProCharts.Avalonia" Version="12.1.0.4" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
  </ItemGroup>
</Project>
```

```csharp
// Telemetry Series in OperationProgressViewModel.cs
public ChartModel ThroughputChartModel { get; }
private readonly ThroughputSeriesBuffer _throughputSeries = new(TimeSpan.FromSeconds(60), maxCapacity: 600);

public OperationProgressViewModel(TimeSpan? throughputSampleInterval = null)
{
    _throughputSeries = new ThroughputSeriesBuffer(TimeSpan.FromSeconds(60), maxCapacity: 600);
    _throughputSampleInterval = throughputSampleInterval ?? TimeSpan.FromMilliseconds(250);

    ThroughputChartModel = new ChartModel
    {
        DataSource = new ThroughputChartDataSource(_throughputSeries)
    };
    ThroughputChartModel.Legend.IsVisible = false;
    ThroughputChartModel.CategoryAxis.Title = "Time";
    ThroughputChartModel.ValueAxis.Title = "MB/s";
    ThroughputChartModel.ValueAxis.Minimum = 0;
    ThroughputChartModel.ValueAxis.LabelFormatter = value => $"{value:0.#}";
}

public void ReportProgress(ProgressReport report)
{
    _throughputTracker.Update(report.ProcessedBytes, report.TotalBytes);
    BytesProgressFormatted = _throughputTracker.FormatProgress(report.TotalBytes);
    TryAddThroughputSample();

    if (_throughputTracker.SmoothedSpeedBytesPerSec > 0)
    {
        SpeedFormatted = _throughputTracker.FormatSpeed();
        EtaFormatted = _throughputTracker.FormatEta(report.TotalBytes);
    }
}
```

## Considered Options

- **Retaining TreeDataGrid** — rejected: lacks declarative XAML column customization and advanced cell virtualization available in `ProDataGrid`.
- **Including Full Pro* Suite (`FormulaEngine`, `ProDiagnostics`)** — rejected: formula engines are unnecessary for archive file managers, and diagnostics add runtime overhead to production distributions.
- **Direct Project Pinning** — rejected: leads to dependency mismatch and transitive version conflicts across projects.

## Consequences

- Modern, declarative XAML archive grid with column reordering and hierarchical row display.
- Real-time visual throughput graphs during archive compression and extraction.
- Strict preservation of headless architecture in `RusZip.Core` and security guardrails against decompression bombs.
