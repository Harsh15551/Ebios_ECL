using System.Text.Json;
using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ebios.Web.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        app.MapPost("/api/webhook/attendance", async (
            HttpRequest request,
            [FromServices] IAttendanceService attendanceService,
            [FromServices] IConfiguration config,
            [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body);
                string rawBody = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(rawBody))
                {
                    logger.LogWarning("Webhook received empty body.");
                    return Results.Content("Success", "text/plain");
                }

                logger.LogInformation("Webhook received payload length: {Length}", rawBody.Length);

                string jsonToParse = rawBody;

                // Check if encrypted {"data": "..."}
                if (rawBody.Contains("\"data\"", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var encryptedEnvelope = JsonSerializer.Deserialize<WebhookEncryptedPayload>(rawBody, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (!string.IsNullOrWhiteSpace(encryptedEnvelope?.Data))
                        {
                            string encryptionKey = config["EbiosServer:WebhookKey"] 
                                                ?? config["EBIOS_WEBHOOK_KEY"] 
                                                ?? string.Empty;

                            if (string.IsNullOrWhiteSpace(encryptionKey))
                            {
                                logger.LogError("Webhook received encrypted payload, but EBIOS_WEBHOOK_KEY is not configured in .env or appsettings.");
                            }
                            else
                            {
                                jsonToParse = AesWebhookDecryptor.Decrypt(encryptedEnvelope.Data, encryptionKey, logger);
                                logger.LogInformation("Successfully decrypted webhook payload.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to decrypt webhook payload with AES-256.");
                    }
                }

                // Parse punch items (can be single object or array)
                var punches = new List<WebhookPunchItem>();
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                string trimmed = jsonToParse.Trim();
                if (trimmed.StartsWith("["))
                {
                    var list = JsonSerializer.Deserialize<List<WebhookPunchItem>>(trimmed, jsonOptions);
                    if (list != null) punches.AddRange(list);
                }
                else if (trimmed.StartsWith("{"))
                {
                    var single = JsonSerializer.Deserialize<WebhookPunchItem>(trimmed, jsonOptions);
                    if (single != null) punches.Add(single);
                }

                if (punches.Any())
                {
                    int saved = await attendanceService.ProcessWebhookPunchesAsync(punches);
                    logger.LogInformation("Webhook saved {Count} attendance punches.", saved);
                }

                // eBioServer strictly expects plain text "Success"
                return Results.Content("Success", "text/plain");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception processing attendance webhook.");
                // Return Success so eBioServer doesn't keep retrying corrupt payload indefinitely
                return Results.Content("Success", "text/plain");
            }
        });
    }
}

