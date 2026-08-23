using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using MeshKit.Core.Catalog;
using MeshKit.Web.Data;
using MeshKit.Web.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MeshKit.Web.Tests;

/// <summary>"Email me when a new pack is released": consent on the account page, one email per release, one-click opt-out.</summary>
public sealed partial class ReleaseNotificationTests : IDisposable
{
    private readonly MeshKitWebFactory _factory = new();
    private readonly DirectoryInfo _scratch = Directory.CreateTempSubdirectory("meshkit-release");

    private void AddUser(string id, string email, bool optIn, string? password = null)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { Id = id, UserName = email, Email = email, NewReleaseOptIn = optIn };
        var result = password is null ? users.CreateAsync(user).Result : users.CreateAsync(user, password).Result;
        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    private byte[] PackZip(string slug)
    {
        var dir = Path.Combine(_scratch.FullName, slug);
        TestPacks.Write(_scratch.FullName, slug, mutate: m => m with { Sample = "chest" });
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

    private async Task<HttpResponseMessage> IngestAsync(byte[] zip)
    {
        var client = _factory.CreateClientAs(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MeshKitWebFactory.IngestToken);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "file", "pack.zip");
        return await client.PostAsync("/api/ingest", content);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [Fact]
    public async Task Account_page_toggles_the_opt_in()
    {
        AddUser("u1", "u1@example.com", optIn: false);
        var client = _factory.CreateClientAs("u1");
        var html = await client.GetStringAsync("/account");
        Assert.Contains("Email me when a new pack is released", html);
        var token = TokenPattern().Match(html).Groups[1].Value;

        var response = await client.PostAsync("/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["_handler"] = "notifications",
            ["Notify.NewReleases"] = "true",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = _factory.WithDb(db => db.Users.Single(u => u.Id == "u1"));
        Assert.True(user.NewReleaseOptIn);
        Assert.NotNull(user.NewReleaseOptInAt);
    }

    [Fact]
    public async Task New_sellable_pack_is_announced_once_to_opted_in_users_with_a_working_unsubscribe_link()
    {
        AddUser("yes", "yes@example.com", optIn: true);
        AddUser("no", "no@example.com", optIn: false);

        var first = await IngestAsync(PackZip("new-pack"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Contains("\"announced\":1", await first.Content.ReadAsStringAsync());

        var mail = Assert.Single(await _factory.WaitForEmailsAsync(1));
        Assert.Equal("yes@example.com", mail.ToAddress);
        Assert.Contains("New pack: Pack new-pack", mail.Subject);
        Assert.Contains("http://localhost/packs/new-pack", mail.Text);
        Assert.Contains("Chest", mail.Text);                                  // the free sample is mentioned
        var unsubscribe = Regex.Match(mail.Text, @"http://localhost/account/notifications/unsubscribe\?token=\S+").Value;
        Assert.NotEmpty(unsubscribe);

        // re-ingesting the same pack (a resume run) never announces again
        var again = await IngestAsync(PackZip("new-pack"));
        Assert.Contains("\"announced\":0", await again.Content.ReadAsStringAsync());
        await Task.Delay(100);
        Assert.Single(_factory.Outbox.Sent);
        Assert.Single(_factory.WithDb(db => db.PackAnnouncements.ToList()));

        // one click, no login
        var clicked = await _factory.CreateClientAs(null).GetAsync(unsubscribe);
        Assert.Equal(HttpStatusCode.Redirect, clicked.StatusCode);
        Assert.Equal("/account/unsubscribed?ok=1", clicked.Headers.Location!.ToString());
        Assert.False(_factory.WithDb(db => db.Users.Single(u => u.Id == "yes")).NewReleaseOptIn);

        var forged = await _factory.CreateClientAs(null).GetAsync("/account/notifications/unsubscribe?token=forged");
        Assert.Equal("/account/unsubscribed?ok=0", forged.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Deleting_the_account_pseudonymises_orders_and_removes_everything_else()
    {
        AddUser("gone", "gone@example.com", optIn: true, password: "correct-horse-battery");
        _factory.WithDb(db =>
        {
            var order = new Order { UserId = "gone", PackSlug = "props", StripeSessionId = "cs_gone", AmountTotal = 1900, Currency = "eur", Status = OrderStatus.Paid, CreatedAt = DateTimeOffset.UnixEpoch };
            db.Orders.Add(order);
            db.SaveChanges();
            db.Entitlements.Add(new Entitlement { UserId = "gone", PackSlug = "props", OrderId = order.Id, GrantedAt = DateTimeOffset.UnixEpoch });
            db.SampleDownloads.Add(new SampleDownload { UserId = "gone", PackSlug = "props", ModelSlug = "chest", DownloadedAt = DateTimeOffset.UnixEpoch });
            return db.SaveChanges();
        });
        var client = _factory.CreateClientAs("gone");
        var token = TokenPattern().Match(await client.GetStringAsync("/account")).Groups[1].Value;

        var wrong = await client.PostAsync("/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["_handler"] = "delete", ["Delete.Password"] = "nope",
        }));
        Assert.Contains("not correct", await wrong.Content.ReadAsStringAsync());
        Assert.Single(_factory.WithDb(db => db.Users.Where(u => u.Id == "gone").ToList()));

        var response = await client.PostAsync("/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["_handler"] = "delete", ["Delete.Password"] = "correct-horse-battery",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(_factory.WithDb(db => db.Users.Where(u => u.Id == "gone").ToList()));
        Assert.Empty(_factory.WithDb(db => db.Entitlements.ToList()));
        Assert.Empty(_factory.WithDb(db => db.SampleDownloads.ToList()));
        var order = Assert.Single(_factory.WithDb(db => db.Orders.ToList()));
        Assert.Equal(AccountDeleter.Pseudonym("gone"), order.UserId);
        Assert.StartsWith("deleted:", order.UserId);
        Assert.Equal(1900, order.AmountTotal);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _scratch.Delete(recursive: true);
    }
}
