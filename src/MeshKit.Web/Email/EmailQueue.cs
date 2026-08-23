using System.Threading.Channels;

namespace MeshKit.Web.Email;

/// <summary>
/// Bounded in-process queue drained by a hosted worker: 3 attempts with backoff, failures logged with
/// the recipient and subject so they can be re-sent by hand. Request handlers (and the Stripe
/// webhook) never wait on SMTP.
/// </summary>
public sealed class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Enqueue(EmailMessage message) => _channel.Writer.TryWrite(message);
}

public sealed class EmailWorker(EmailQueue queue, IEmailSender sender, ILogger<EmailWorker> logger, TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await DeliverAsync(message, stoppingToken);
        }
    }

    internal async Task DeliverAsync(EmailMessage message, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await sender.SendAsync(message, ct);
                logger.LogInformation("Email sent to {To}: {Subject}", message.ToAddress, message.Subject);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                if (attempt == 3)
                {
                    logger.LogError(ex, "Email to {To} failed after 3 attempts: {Subject}", message.ToAddress, message.Subject);
                    return;
                }

                logger.LogWarning(ex, "Email to {To} failed (attempt {Attempt}); retrying", message.ToAddress, attempt);
                await Task.Delay(RetryBaseDelay * Math.Pow(2, attempt - 1), _time, ct);
            }
        }
    }
}
