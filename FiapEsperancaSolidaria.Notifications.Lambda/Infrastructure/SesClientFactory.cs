using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmail;
using Amazon.Lambda.Core;
using System.Net;

namespace FiapEsperancaSolidaria.Notifications.Lambda.Infrastructure
{
    public static class SesClientFactory
    {
        public static IAmazonSimpleEmailService Create()
        {
            // Ler APENAS de variáveis de ambiente
            var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
            var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
            var serviceUrl = Environment.GetEnvironmentVariable("AWS_SES_ENDPOINT");
            var region = Environment.GetEnvironmentVariable("AWS_REGION");

            LambdaLogger.Log($"[SesClientFactory] Environment Variables:");
            LambdaLogger.Log($"  AWS_ACCESS_KEY_ID: {(string.IsNullOrEmpty(accessKey) ? "NOT SET" : "SET")}");
            LambdaLogger.Log($"  AWS_SECRET_ACCESS_KEY: {(string.IsNullOrEmpty(secretKey) ? "NOT SET" : "SET")}");
            LambdaLogger.Log($"  AWS_SES_ENDPOINT: {serviceUrl ?? "NOT SET"}");
            LambdaLogger.Log($"  AWS_REGION: {region ?? "NOT SET"}");

            // Se tiver endpoint customizado, usar com credenciais explícitas
            if (!string.IsNullOrEmpty(serviceUrl))
            {
                LambdaLogger.Log($"[SesClientFactory] Creating SES client with LocalStack endpoint");

                var credentials = new BasicAWSCredentials(
                    accessKey ?? "test",
                    secretKey ?? "test"
                );

                var config = new AmazonSimpleEmailServiceConfig
                {
                    ServiceURL = serviceUrl,
                    UseHttp = true,
                    //AuthenticationRegion = RegionEndpoint.GetBySystemName(region ?? "sa-east-1").ToString(),
                    DisableHostPrefixInjection = true
                };

                LambdaLogger.Log($"[SesClientFactory] Configuration:");
                LambdaLogger.Log($"  ServiceURL: {config.ServiceURL}");
                //LambdaLogger.Log($"  Region: {config.AuthenticationRegion}");
                LambdaLogger.Log($"  UseHttp: {config.UseHttp}");
                LambdaLogger.Log($"  DisableHostPrefixInjection: {config.DisableHostPrefixInjection}");

                try
                {
                    // Desabilitar validação de certificado SSL (importante para LocalStack)
                    ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
                    {
                        LambdaLogger.Log($"[SesClientFactory] SSL Certificate validation - allowing all");
                        return true;
                    };

                    var client = new AmazonSimpleEmailServiceClient(credentials, config);
                    LambdaLogger.Log($"[SesClientFactory] ✓ SES client created successfully");
                    return client;
                }
                catch (Exception ex)
                {
                    LambdaLogger.Log($"[SesClientFactory] ERROR creating client: {ex.Message}");
                    LambdaLogger.Log($"[SesClientFactory] StackTrace: {ex.StackTrace}");
                    throw;
                }
            }

            LambdaLogger.Log($"[SesClientFactory] WARNING: Using default SES client (no custom endpoint)");
            return new AmazonSimpleEmailServiceClient();
        }
    }
}
