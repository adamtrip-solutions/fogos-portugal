using Fogos.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Fogos.Integration.Tests.Photos;

[Collection("fogos")]
public sealed class StorageProbeTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Put_via_app_object_storage_succeeds()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        var storage = fixture.Factory.Services.GetRequiredService<IObjectStorage>();
        using var ms = new MemoryStream(new byte[128]);
        var ex = await Record.ExceptionAsync(() => storage.PutAsync("probe/x.jpg", ms, "image/jpeg"));
        Assert.True(ex is null, $"PUT failed: {ex}");
        Assert.True(await storage.ExistsAsync("probe/x.jpg"));
    }
}
