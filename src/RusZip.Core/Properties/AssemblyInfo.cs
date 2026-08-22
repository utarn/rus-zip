using System.Runtime.CompilerServices;

// Expose internal helpers (e.g. SharpCompressArchiveEngine.IsPasswordOrEncryptedException) to the
// unit test project so the error-translation classifier can be pinned directly (issue #48).
[assembly: InternalsVisibleTo("RusZip.Core.Tests")]
