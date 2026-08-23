namespace MeshKit.Web.Email;

public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromName { get; set; } = "MeshKit by Atypical Consulting";
    public string FromAddress { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public sealed record EmailMessage(string ToAddress, string? ToName, string Subject, string Html, string Text);

/// <summary>Transport only. Implementations must not throw for a bad recipient; they report and move on.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>What the app calls: enqueue and return immediately. Delivery happens on the background worker with retries.</summary>
public interface IEmailQueue
{
    void Enqueue(EmailMessage message);
}
