using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using FiapEsperancaSolidaria.Notifications.Lambda.Models;
using FiapEsperancaSolidaria.Notifications.Lambda.Services;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Text.Json;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace FiapEsperancaSolidaria.Notifications.Lambda;

public class EmailFunction
{
    private readonly IEmailService _emailService;
    private static readonly TracerProvider _tracerProvider;
    private static readonly ActivitySource Activity = new("notification-lambda");
    private readonly NewRelicLogService _newRelicLogService;

    /// <summary>
    /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
    /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
    /// region the Lambda function is executed in.
    /// </summary>
    public EmailFunction()
    {
        _emailService = new SesEmailService();
        _newRelicLogService = new NewRelicLogService();
    }

    static EmailFunction()
    {
        _tracerProvider = Sdk.CreateTracerProviderBuilder()
        .SetResourceBuilder(
            ResourceBuilder.CreateDefault()
                .AddService("notification-lambda")
        )
        //.AddHttpClientInstrumentation()
        .AddSource("notification-lambda")
        .AddConsoleExporter()
        .AddOtlpExporter(options =>
        {
            options.Endpoint =
                new Uri("https://otlp.nr-data.net");

            options.Protocol =
                OtlpExportProtocol.HttpProtobuf;

            options.Headers =
                $"api-key={Environment.GetEnvironmentVariable("NEW_RELIC_LICENSE_KEY")?.Trim()}";
        })
        .Build();
    }


    /// <summary>
    /// This method is called for every Lambda invocation. This method takes in an SQS event object and can be used 
    /// to respond to SQS messages.
    /// </summary>
    /// <param name="evnt">The event for the Lambda function handler to process.</param>
    /// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    /// <returns></returns>
    public async Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        foreach (var message in evnt.Records)
        {
            await ProcessMessageAsync(message, context);
        }

        _tracerProvider.ForceFlush();
    }

    private async Task ProcessMessageAsync(SQSEvent.SQSMessage message, ILambdaContext context)
    {

        var timestampInit = DateTimeOffset.UtcNow.ToString("o");
        context.Logger.LogInformation($"Processed message {message.Body}");

        var emailMessage = JsonSerializer.Deserialize<EmailMessage>(message.Body);
        using var activity = Activity.StartActivity("SendEmail");

        activity?.SetTag("CorrelationId", emailMessage?.CorrelationId);

        try
        {
            
            await _emailService.SendAsync(emailMessage!);

        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Erro inesperado ao enviar mensagem {message.MessageId}: {ex}");

            await _newRelicLogService.SendLogAsync("ERROR", "[notification-lambda] | Erro ao enviar email", new
            {
                Exception = ex.Message,
                StackTrace = ex.StackTrace ?? string.Empty,
                emailMessage?.CorrelationId
            }
            );
        }

        var timestampEnd = DateTimeOffset.UtcNow.ToString("o");
        var durationMs =
            (DateTimeOffset.Parse(timestampEnd) - DateTimeOffset.Parse(timestampInit))
            .TotalMilliseconds;
        try
        {
            await _newRelicLogService.SendLogAsync("INFO", "[notification-lambda] | Email enviado", new
            {
                emailMessage?.CorrelationId,
                emailMessage?.To,
                emailMessage?.Subject,
                emailMessage?.Body,
                durationMs
            });
        }
        catch (Exception ex)
        {

            context.Logger.LogError($"Erro inesperado ao enviar logs para New Relic: {ex}");

            await _newRelicLogService.SendLogAsync("ERROR", "[_newRelicLogService.SendLogAsync] | Erro ao enviar logs para New Relic", new
            {
                Exception = ex.Message,
                StackTrace = ex.StackTrace ?? string.Empty,
                emailMessage?.CorrelationId
            }
            );
        }       

        await Task.CompletedTask;
    }
}