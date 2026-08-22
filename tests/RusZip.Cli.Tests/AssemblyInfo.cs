using Xunit;

// Issue #62: CLI tests exercise Spectre.Console static state (AnsiConsole.Console and the
// System.Console writers it wraps). CliTestBase.RunCliAsync redirects AnsiConsole.Console to a
// test-local console for the duration of a command run, so any test that performs human-mode
// (non-JSON) AnsiConsole output can race a concurrent RunCliAsync test and inherit a disposed
// TextWriter ("Cannot write to a closed TextWriter"). This was an order-dependent flake:
// EmitError_SecurityViolation_InHumanMode_Returns1 failed roughly every other full-suite run.
//
// Serializing the assembly (matching the Desktop test project) guarantees that no test observes
// another test's transient console redirection, eliminating the shared-static race.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
