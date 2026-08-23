using RusZip.Desktop.Models;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

public class SettingsViewModelTests
{
    private class FakeFileAssociationService : IFileAssociationService
    {
        public List<FileAssociationInfo> AssociationsToReturn { get; set; } =
        [
            new FileAssociationInfo(".zrus", "Zstandard TAR Archive (.zrus)", true, "RusZip"),
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
                    AssociationsToReturn[idx] = AssociationsToReturn[idx] with { IsAssociated = true, CurrentHandler = "RusZip" };
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
    public async Task LoadAssociationsAsync_PopulatesFormatsAndStatus()
    {
        var service = new FakeFileAssociationService();
        var vm = new SettingsViewModel(service);

        await vm.LoadAssociationsAsync();

        Assert.Equal(3, vm.Formats.Count);
        Assert.False(vm.AllFormatsAssociated);
        Assert.True(vm.Formats[0].IsAssociated);
        Assert.Equal("Default Handler", vm.Formats[0].StatusText);
        Assert.False(vm.Formats[1].IsAssociated);
        Assert.Equal("Not Associated", vm.Formats[1].StatusText);
        Assert.False(vm.Formats[2].IsAssociated);
        Assert.Equal("Handled by 7-Zip", vm.Formats[2].StatusText);
    }

    [Fact]
    public async Task ApplyAssociationsCommand_WhenNoneSelected_SetsStatusMessage()
    {
        var service = new FakeFileAssociationService();
        var vm = new SettingsViewModel(service);
        await vm.LoadAssociationsAsync();

        vm.SelectNoneCommand.Execute(null);

        await vm.ApplyAssociationsCommand.ExecuteAsync(null);

        Assert.Equal("No formats selected.", vm.StatusMessage);
        Assert.Empty(service.RegisteredExtensions);
    }

    [Fact]
    public async Task ApplyAssociationsCommand_RegistersSelectedAndRefreshes()
    {
        var service = new FakeFileAssociationService();
        var vm = new SettingsViewModel(service);
        await vm.LoadAssociationsAsync();

        vm.Formats[0].IsSelected = false;
        vm.Formats[1].IsSelected = true;
        vm.Formats[2].IsSelected = false;

        await vm.ApplyAssociationsCommand.ExecuteAsync(null);

        Assert.Contains("Applied associations for 1 format(s).", vm.StatusMessage);
        Assert.Equal([".zip"], service.RegisteredExtensions);
        Assert.True(vm.Formats[1].IsAssociated);
    }

    [Fact]
    public async Task ReapplyAllAssociationsCommand_RegistersAllFormats()
    {
        var service = new FakeFileAssociationService();
        var vm = new SettingsViewModel(service);
        await vm.LoadAssociationsAsync();

        await vm.ReapplyAllAssociationsCommand.ExecuteAsync(null);

        Assert.Equal("All supported formats associated with rus-zip.", vm.StatusMessage);
        Assert.Equal(3, service.RegisteredExtensions.Count);
        Assert.True(vm.AllFormatsAssociated);
        Assert.All(vm.Formats, f => Assert.True(f.IsAssociated));
    }

    [Fact]
    public async Task SelectAll_And_SelectNone_TogglesAllItems()
    {
        var service = new FakeFileAssociationService();
        var vm = new SettingsViewModel(service);
        await vm.LoadAssociationsAsync();

        vm.SelectNoneCommand.Execute(null);
        Assert.All(vm.Formats, f => Assert.False(f.IsSelected));

        vm.SelectAllCommand.Execute(null);
        Assert.All(vm.Formats, f => Assert.True(f.IsSelected));
    }
}
