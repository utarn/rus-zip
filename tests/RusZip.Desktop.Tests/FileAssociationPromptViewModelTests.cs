using RusZip.Desktop.Models;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

public class FileAssociationPromptViewModelTests
{
    private class FakeFileAssociationService : IFileAssociationService
    {
        public List<FileAssociationInfo> AssociationsToReturn { get; set; } =
        [
            new FileAssociationInfo(".zrus", "Zstandard TAR Archive (.zrus)", false),
            new FileAssociationInfo(".zip", "Standard Zip Archive (.zip)", false),
            new FileAssociationInfo(".7z", "7-Zip Archive (.7z)", false, "7-Zip")
        ];

        public List<string> RegisteredExtensions { get; } = [];

        public IReadOnlyList<string> SupportedExtensions => [".zrus", ".zip", ".7z"];

        public Task<IReadOnlyList<FileAssociationInfo>> GetAssociationsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileAssociationInfo>>(AssociationsToReturn);

        public Task<bool> AreAllFormatsAssociatedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(AssociationsToReturn.All(a => a.IsAssociated));

        public Task<bool> IsFormatAssociatedAsync(string extension, CancellationToken cancellationToken = default)
            => Task.FromResult(AssociationsToReturn.FirstOrDefault(a => a.Extension == extension)?.IsAssociated ?? false);

        public Task RegisterAssociationsAsync(IEnumerable<string> extensions, CancellationToken cancellationToken = default)
        {
            RegisteredExtensions.AddRange(extensions);
            foreach (var ext in extensions)
            {
                var idx = AssociationsToReturn.FindIndex(a => a.Extension == ext);
                if (idx >= 0)
                {
                    AssociationsToReturn[idx] = AssociationsToReturn[idx] with { IsAssociated = true, CurrentHandler = "RUS ZIP" };
                }
            }
            return Task.CompletedTask;
        }

        public Task RegisterDefaultAssociationsAsync(CancellationToken cancellationToken = default)
            => RegisterAssociationsAsync(SupportedExtensions, cancellationToken);

        public Task RemoveAssociationsAsync(IEnumerable<string> extensions, CancellationToken cancellationToken = default)
        {
            foreach (var ext in extensions)
            {
                RegisteredExtensions.Remove(ext);
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task InitializeAsync_PopulatesFormats_WithCheckedByDefault()
    {
        var fakeService = new FakeFileAssociationService();
        var vm = new FileAssociationPromptViewModel(fakeService);

        await vm.InitializeAsync();

        Assert.Equal(3, vm.Formats.Count);
        Assert.All(vm.Formats, f => Assert.True(f.IsSelected));
        Assert.Equal(".zrus", vm.Formats[0].Extension);
        Assert.Equal(".zip", vm.Formats[1].Extension);
        Assert.Equal(".7z", vm.Formats[2].Extension);
        Assert.Equal("Handled by 7-Zip", vm.Formats[2].StatusText);
    }

    [Fact]
    public async Task SetAsDefaultCommand_RegistersSelectedFormats_AndInvokesCloseRequested()
    {
        var fakeService = new FakeFileAssociationService();
        var vm = new FileAssociationPromptViewModel(fakeService);
        await vm.InitializeAsync();

        // Deselect .7z
        vm.Formats[2].IsSelected = false;

        bool closeRequested = false;
        vm.CloseRequested += () => closeRequested = true;

        await vm.SetAsDefaultCommand.ExecuteAsync(null);

        Assert.True(closeRequested);
        Assert.Equal([".zrus", ".zip"], fakeService.RegisteredExtensions);
    }

    [Fact]
    public async Task NotNowCommand_InvokesCloseRequested_WithoutRegistering()
    {
        var fakeService = new FakeFileAssociationService();
        var vm = new FileAssociationPromptViewModel(fakeService);
        await vm.InitializeAsync();

        bool closeRequested = false;
        vm.CloseRequested += () => closeRequested = true;

        vm.NotNowCommand.Execute(null);

        Assert.True(closeRequested);
        Assert.Empty(fakeService.RegisteredExtensions);
    }

    [Fact]
    public async Task SelectAll_And_SelectNone_ToggleSelectionState()
    {
        var fakeService = new FakeFileAssociationService();
        var vm = new FileAssociationPromptViewModel(fakeService);
        await vm.InitializeAsync();

        vm.SelectNoneCommand.Execute(null);
        Assert.All(vm.Formats, f => Assert.False(f.IsSelected));

        vm.SelectAllCommand.Execute(null);
        Assert.All(vm.Formats, f => Assert.True(f.IsSelected));
    }
}
