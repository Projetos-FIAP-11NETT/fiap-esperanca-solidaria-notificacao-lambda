using System.Text;
using System.Text.Json;

namespace FiapEsperancaSolidaria.Notifications.Lambda.Services;

public class NewRelicLogService
{
    private readonly HttpClient _httpClient;

    public NewRelicLogService()
    {
        _httpClient = new HttpClient();

        _httpClient.DefaultRequestHeaders.Add(
            "X-License-Key",
            Environment.GetEnvironmentVariable("NEW_RELIC_LICENSE_KEY")?.Trim()
        );
    }

    public async Task SendLogAsync(
        string level,
        string message,
        object? data = null)
    {
        var payload = new[]
        {
            new
            {
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                message,
                level,
                service = "notification-lambda",
                environment = "local",
                data
            }
        };

        var json = JsonSerializer.Serialize(payload);

        var response = await _httpClient.PostAsync(
            "https://log-api.newrelic.com/log/v1",
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        );

        Console.WriteLine(
            $"[NewRelic] Log status: {response.StatusCode}"
        );

        if (!response.IsSuccessStatusCode)
        {
            var error =
                await response.Content.ReadAsStringAsync();

            Console.WriteLine(
                $"[NewRelic] Error: {error}"
            );
        }
    }
}