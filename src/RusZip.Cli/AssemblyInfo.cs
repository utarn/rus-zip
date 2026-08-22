using System.Runtime.CompilerServices;

// Expose internal progress columns and the report-feeding helper so the CLI test project can
// pin the ThroughputTracker integration (issue #52).
[assembly: InternalsVisibleTo("RusZip.Cli.Tests")]
