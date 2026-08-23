using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeshKit.Core.Catalog;
using Microsoft.AspNetCore.Hosting;

namespace MeshKit.Web.Tests;

public sealed class IngestTests : IDisposable
{
    private readonly MeshKitWebFactory _factory = new();
    private readonly DirectoryInfo _scratch = Directory.CreateTempSubdirectory("meshkit-ingest");

    private byte[] PackZip(string slug, Func<PackManifest, PackManifest>? mutate = null, bool dropFile = false)
    {
        var dir = Path.Combine(_scratch.FullName, slug);
        TestPacks.Write(_scratch.FullName, slug, mutate: mutate);
        if (dropFile)
        {
            File.Delete(Path.Combine(dir, "private", "chest", "chest.fbx"));
        }

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                zip.CreateEntryFromFile(file, Path.GetRelativePath(dir, file).Replace('\\', '/'));
            }
        }

        return buffer.ToArray();
    }

    private static MultipartFormDataContent Upload(byte[] zip)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "file", "pack.zip");
        return content;
    }

    private HttpClient Client(string? token = MeshKitWebFactory.IngestToken)
    {
        var client = _factory.CreateClientAs(null);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    [Fact]
    public async Task Valid_pack_is_installed_and_becomes_visible()
    {
        var response = await Client().PostAsync("/api/ingest", Upload(PackZip("fresh-pack")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/packs/fresh-pack", response.Headers.Location!.ToString());
        var html = await _factory.CreateClientAs(null).GetStringAsync("/packs");
        Assert.Contains("Pack fresh-pack", html);
        Assert.True(File.Exists(Path.Combine(_factory.CatalogPath, "fresh-pack", "private", "chest", "chest.glb")));
        Assert.Empty(Directory.EnumerateDirectories(_factory.CatalogPath, ".staging-*"));
    }

    [Fact]
    public async Task Reimport_replaces_the_previous_version()
    {
        await Client().PostAsync("/api/ingest", Upload(PackZip("pack")));
        var v2 = PackZip("pack", m => m with { Name = "Pack v2" });

        var response = await Client().PostAsync("/api/ingest", Upload(v2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("Pack v2", await _factory.CreateClientAs(null).GetStringAsync("/packs/pack"));
    }

    [Fact]
    public async Task Wrong_token_is_unauthorized_and_nothing_is_written()
    {
        var response = await Client("nope").PostAsync("/api/ingest", Upload(PackZip("pack")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_factory.CatalogPath, "pack")));
    }

    [Fact]
    public async Task Missing_token_is_unauthorized()
    {
        var response = await Client(token: null).PostAsync("/api/ingest", Upload(PackZip("pack")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Manifest_with_traversal_is_refused()
    {
        var zip = PackZip("evil", m => m with { Models = [m.Models[0] with { Preview = "../../outside.glb" }] });

        var response = await Client().PostAsync("/api/ingest", Upload(zip));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unsafe", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error);
        Assert.False(Directory.Exists(Path.Combine(_factory.CatalogPath, "evil")));
    }

    [Fact]
    public async Task Manifest_listing_a_file_missing_from_the_archive_is_refused()
    {
        var response = await Client().PostAsync("/api/ingest", Upload(PackZip("partial", dropFile: true)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("chest.fbx", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error);
    }

    [Fact]
    public async Task Garbage_upload_is_refused()
    {
        var response = await Client().PostAsync("/api/ingest", Upload([1, 2, 3, 4]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Failed_import_leaves_existing_pack_untouched()
    {
        await Client().PostAsync("/api/ingest", Upload(PackZip("keep")));
        var broken = PackZip("keep", m => m with { Models = [m.Models[0] with { Thumbnail = "/etc/passwd" }] });

        var response = await Client().PostAsync("/api/ingest", Upload(broken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Pack keep", await _factory.CreateClientAs(null).GetStringAsync("/packs/keep"));
    }

    [Fact]
    public async Task Ingest_without_configured_token_is_unavailable_even_with_a_header()
    {
        using var disabled = _factory.WithWebHostBuilder(b => b.UseSetting("MeshKit:Ingest:Token", ""));
        var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "anything");

        var response = await client.PostAsync("/api/ingest", Upload(PackZip("pack")));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Upload_above_the_configured_limit_is_413_and_under_it_is_accepted()
    {
        var zip = PackZip("sized");
        using var limited = _factory.WithWebHostBuilder(b => b.UseSetting("MeshKit:Ingest:MaxUploadBytes", (zip.Length - 1).ToString()));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MeshKitWebFactory.IngestToken);

        var tooBig = await client.PostAsync("/api/ingest", Upload(zip));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Client().PostAsync("/api/ingest", Upload(zip))).StatusCode);
    }

    private sealed record ErrorBody(string Error);

    public void Dispose()
    {
        _factory.Dispose();
        _scratch.Delete(recursive: true);
    }
}
