using RusZip.Desktop.Services;
using Xunit;

namespace RusZip.Desktop.Tests;

public class FileAssociationServiceFactoryAndCompositeTests
{
    [Fact]
    public void FileAssociationServiceFactory_CreateDefault_ReturnsNonNullService()
    {
        var service = FileAssociationServiceFactory.CreateDefault();
        Assert.NotNull(service);
        Assert.NotEmpty(service.SupportedExtensions);

        var customService = FileAssociationServiceFactory.CreateDefault("/usr/bin/ruszip");
        Assert.NotNull(customService);
    }

    [Fact]
    public async Task CompositeFileAssociationService_DelegatesToUnderlyingService()
    {
        var inner = new LinuxAssociationService();
        var composite = new CompositeFileAssociationService(inner);

        Assert.Equal(inner.SupportedExtensions, composite.SupportedExtensions);

        var associations = await composite.GetAssociationsAsync();
        Assert.NotNull(associations);

        var isAssociated = await composite.IsFormatAssociatedAsync(".zip");
        Assert.Equal(await inner.IsFormatAssociatedAsync(".zip"), isAssociated);

        var allAssociated = await composite.AreAllFormatsAssociatedAsync();
        Assert.Equal(await inner.AreAllFormatsAssociatedAsync(), allAssociated);

        await composite.RegisterDefaultAssociationsAsync();
        await composite.RegisterAssociationsAsync([".zrus"]);
        await composite.RemoveAssociationsAsync([".zrus"]);
    }

    [Fact]
    public void CompositeFileAssociationService_NullService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeFileAssociationService(null!));
    }
}
