using System.Net;
using System.Text.RegularExpressions;
using MeshKit.Web.Data;
using MeshKit.Web.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;

namespace MeshKit.Web.Tests;

public sealed partial class EmailTests : IDisposable
{
    private readonly MeshKitWebFactory _factory = new();

    private async Task<ApplicationUser> CreateUserAsync(string email, string password = "correct-horse-battery")
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        return user;
    }

    private static async Task<(string Token, string Cookie)> FormTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var token = TokenPattern().Match(html).Groups[1].Value;
        Assert.NotEmpty(token);
        return (token, "");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [Fact]
    public async Task Paid_webhook_sends_a_purchase_confirmation_with_download_and_licence_links()
    {
        TestPacks.Write(_factory.CatalogPath, "props");
        var user = await CreateUserAsync("buyer@example.com");
        var payload = """{"id":"evt_1","object":"event","api_version":"2026-07-29.dahlia","type":"checkout.session.completed","data":{"object":{"id":"cs_mail","object":"checkout.session","payment_status":"paid","client_reference_id":"USER_ID","metadata":{"pack_slug":"props"},"amount_total":1900,"currency":"eur","payment_intent":"pi_1"}}}""".Replace("USER_ID", user.Id);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var request = new HttpRequestMessage(HttpMethod.Post, "/stripe/webhook") { Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", $"t={ts},v1={EventUtility.ComputeSignature(MeshKitWebFactory.WebhookSecret, ts.ToString(), payload)}");

        var response = await _factory.CreateClientAs(null).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mail = Assert.Single(await _factory.WaitForEmailsAsync(1));
        Assert.Equal("buyer@example.com", mail.ToAddress);
        Assert.Contains("Pack props", mail.Subject);
        Assert.Contains("http://localhost/library/props/download", mail.Html);
        Assert.Contains("http://localhost/packs/props/licence", mail.Text);
        Assert.Contains("€19.00", mail.Text);
        Assert.Contains("BE 0744.517.956", mail.Text);

        // A replayed event must not send a second email.
        await _factory.CreateClientAs(null).SendAsync(new HttpRequestMessage(HttpMethod.Post, "/stripe/webhook")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            Headers = { { "Stripe-Signature", $"t={ts},v1={EventUtility.ComputeSignature(MeshKitWebFactory.WebhookSecret, ts.ToString(), payload)}" } },
        });
        await Task.Delay(150);
        Assert.Single(_factory.Outbox.Sent);
    }

    [Fact]
    public async Task Registration_sends_a_confirmation_whose_link_confirms_the_address()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await FormTokenAsync(client, "/account/register");

        var response = await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["_handler"] = "register",
            ["Input.Email"] = "new@example.com",
            ["Input.Password"] = "correct-horse-battery",
            ["Input.ConfirmPassword"] = "correct-horse-battery",
        }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var mail = Assert.Single(await _factory.WaitForEmailsAsync(1));
        Assert.Equal("new@example.com", mail.ToAddress);
        var link = Regex.Match(mail.Text, @"http://localhost/account/confirm-email\?\S+").Value;
        Assert.NotEmpty(link);

        var confirm = await client.GetAsync(link);
        Assert.Equal(HttpStatusCode.Found, confirm.StatusCode);
        Assert.Equal("/account/login?confirmed=1", confirm.Headers.Location!.ToString());
        using var scope = _factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("new@example.com");
        Assert.True(user!.EmailConfirmed);
    }

    [Fact]
    public async Task Forgot_password_emails_a_link_that_resets_the_password()
    {
        await CreateUserAsync("forgetful@example.com", "old-password-here");
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await FormTokenAsync(client, "/account/forgot-password");

        var sent = await client.PostAsync("/account/forgot-password", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["_handler"] = "forgot",
            ["Input.Email"] = "forgetful@example.com",
        }));
        Assert.Contains("a reset link is on its way", await sent.Content.ReadAsStringAsync());

        var mail = Assert.Single(await _factory.WaitForEmailsAsync(1));
        var link = Regex.Match(mail.Text, @"http://localhost/account/reset-password\?\S+").Value;
        Assert.NotEmpty(link);

        var (resetToken, _) = await FormTokenAsync(client, link);
        var reset = await client.PostAsync(link, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = resetToken,
            ["_handler"] = "reset",
            ["Input.Password"] = "brand-new-password",
            ["Input.ConfirmPassword"] = "brand-new-password",
        }));
        Assert.Equal(HttpStatusCode.Found, reset.StatusCode);
        Assert.EndsWith("/account/login?reset=1", reset.Headers.Location!.ToString());

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("forgetful@example.com");
        Assert.True(await users.CheckPasswordAsync(user!, "brand-new-password"));
        Assert.False(await users.CheckPasswordAsync(user!, "old-password-here"));
    }

    [Fact]
    public async Task Forgot_password_for_unknown_address_says_the_same_thing_and_sends_nothing()
    {
        var client = _factory.CreateClient();
        var (token, _) = await FormTokenAsync(client, "/account/forgot-password");

        var response = await client.PostAsync("/account/forgot-password", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["_handler"] = "forgot",
            ["Input.Email"] = "nobody@example.com",
        }));

        Assert.Contains("a reset link is on its way", await response.Content.ReadAsStringAsync());
        await Task.Delay(150);
        Assert.Empty(_factory.Outbox.Sent);
    }

    [Fact]
    public async Task Worker_retries_transient_failures_then_gives_up_without_crashing()
    {
        var attempts = 0;
        var flaky = new FlakySender(() => ++attempts < 3 ? throw new IOException("smtp down") : Task.CompletedTask);
        var worker = new EmailWorker(new EmailQueue(), flaky, NullLogger<EmailWorker>.Instance) { RetryBaseDelay = TimeSpan.FromMilliseconds(1) };

        await worker.DeliverAsync(new EmailMessage("a@b", null, "s", "<p>h</p>", "t"), CancellationToken.None);
        Assert.Equal(3, attempts);

        attempts = 0;
        var dead = new FlakySender(() => { attempts++; throw new IOException("smtp dead"); });
        var worker2 = new EmailWorker(new EmailQueue(), dead, NullLogger<EmailWorker>.Instance) { RetryBaseDelay = TimeSpan.FromMilliseconds(1) };
        await worker2.DeliverAsync(new EmailMessage("a@b", null, "s", "<p>h</p>", "t"), CancellationToken.None);
        Assert.Equal(3, attempts);
    }

    private sealed class FlakySender(Func<Task> behaviour) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => behaviour();
    }

    public void Dispose() => _factory.Dispose();
}
