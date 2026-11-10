using Ebios.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ebios.Web.Endpoints;

public static class BiometricEndpoints
{
    public static IEndpointRouteBuilder MapBiometricEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/biometric-forms");

        // 1. Photo endpoint (AES-256-GCM in-memory decrypted stream)
        group.MapGet("/photo/{empCode}/{boxType}", async (
            string empCode,
            string boxType,
            [FromServices] IBiometricFormService biometricService) =>
        {
            var result = await biometricService.GetPhotoBytesAsync(empCode, boxType);
            if (result == null || result.Value.FileBytes == null || result.Value.FileBytes.Length == 0)
            {
                return Results.NotFound();
            }

            return Results.File(result.Value.FileBytes, result.Value.ContentType, enableRangeProcessing: true);
        });

        // 2. Signed physical form download / view endpoint (AES-256-GCM in-memory decrypted stream)
        group.MapGet("/signed-form/{empCode}", async (
            string empCode,
            [FromServices] IBiometricFormService biometricService) =>
        {
            var result = await biometricService.GetSignedFormBytesAsync(empCode);
            if (result == null || result.Value.FileBytes == null || result.Value.FileBytes.Length == 0)
            {
                return Results.NotFound();
            }

            return Results.File(result.Value.FileBytes, result.Value.ContentType, enableRangeProcessing: true);
        });

        // 3. Download CSV Template endpoint
        group.MapGet("/template-csv", ([FromServices] IBiometricFormService biometricService) =>
        {
            var bytes = biometricService.GenerateBulkCsvTemplate();
            return Results.File(bytes, "text/csv", "sample_bulk_biometric_forms.csv");
        });

        return app;
    }
}
