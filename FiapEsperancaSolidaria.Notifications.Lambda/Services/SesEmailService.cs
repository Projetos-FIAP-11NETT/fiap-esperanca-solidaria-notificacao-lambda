using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Amazon.Lambda.Core;
using FiapEsperancaSolidaria.Notifications.Lambda.Infrastructure;
using FiapEsperancaSolidaria.Notifications.Lambda.Models;
using FiapEsperancaSolidaria.Notifications.Lambda.Services;

public class SesEmailService : IEmailService
{
    private readonly IAmazonSimpleEmailService _sesClient;

    public SesEmailService()
    {
        LambdaLogger.Log($"[SesEmailService] Initializing SES Email Service");
        _sesClient = SesClientFactory.Create();
    }

    public async Task SendAsync(EmailMessage message)
    {
        var request = new SendEmailRequest
        {
            Source = "no-reply@fiapcloudgames.local",
            Destination = new Destination
            {
                ToAddresses = new List<string> { message.To }
            },
            Message = new Message
            {
                Subject = new Content(message.Subject),
                Body = new Body
                {
                    Text = new Content(message.Body)
                }
            }
        };

        LambdaLogger.Log($"[SesEmailService] Sending email");
        LambdaLogger.Log($"  From: {request.Source}");
        LambdaLogger.Log($"  To: {message.To}");
        LambdaLogger.Log($"  Subject: {message.Subject}");

        try
        {
            LambdaLogger.Log($"[SesEmailService] Calling SendEmailAsync...");
            var response = await _sesClient.SendEmailAsync(request);
            LambdaLogger.Log($"[SesEmailService] ✓ Email sent successfully!");
            LambdaLogger.Log($"  MessageId: {response.MessageId}");
        }
        catch (MessageRejectedException ex)
        {
            LambdaLogger.Log($"[SesEmailService] ✗ MessageRejectedException: {ex.Message}");
            throw new AmazonSimpleEmailServiceException($"Message rejected: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            LambdaLogger.Log($"[SesEmailService] ✗ Exception: {ex.GetType().Name}");
            LambdaLogger.Log($"  Message: {ex.Message}");

            if (ex.InnerException != null)
            {
                LambdaLogger.Log($"  Inner Exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }

            throw new AmazonSimpleEmailServiceException($"Failed to send email: {ex.Message}", ex);
        }
    }
}

