using Ebios.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ebios.Web.Endpoints;

public static class BiometricEndpoints
{
    public static IEndpointRouteBuilder MapBiometricEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/biometric-forms");

        // 1. Photo endpoint
        group.MapGet("/photo/{empCode}/{boxType}", (
            string empCode,
            string boxType,
            [FromServices] IBiometricFormService biometricService,
            [FromServices] IHostEnvironment env) =>
        {
            var cleanEmpCode = Sanitize(empCode);
            var cleanBox = Sanitize(boxType).ToLowerInvariant();

            // Support either {empCode}_{box}.jpg or direct file match
            var dir = Path.Combine(env.ContentRootPath, "storage", "biometric_forms", cleanEmpCode);
            if (!Directory.Exists(dir))
            {
                return Results.NotFound();
            }

            var filePath = Path.Combine(dir, $"{cleanEmpCode}_{cleanBox}.jpg");
            if (!File.Exists(filePath))
            {
                // Fallback: check if file without empCode prefix exists (e.g. aadhaar.jpg)
                var alt = Path.Combine(dir, $"{cleanBox}.jpg");
                if (File.Exists(alt)) filePath = alt;
                else return Results.NotFound();
            }

            return Results.File(filePath, "image/jpeg", enableRangeProcessing: true);
        });

        // 2. Signed form download / view endpoint
        group.MapGet("/signed-form/{empCode}", (
            string empCode,
            [FromServices] IHostEnvironment env) =>
        {
            var cleanEmpCode = Sanitize(empCode);
            var dir = Path.Combine(env.ContentRootPath, "storage", "biometric_forms", cleanEmpCode);
            if (!Directory.Exists(dir)) return Results.NotFound();

            // Look for _signed_form.*
            var files = Directory.GetFiles(dir, $"{cleanEmpCode}_signed_form.*");
            if (files.Length == 0)
            {
                // check for any pdf
                files = Directory.GetFiles(dir, "*.pdf");
                if (files.Length == 0) return Results.NotFound();
            }

            var target = files[0];
            var ext = Path.GetExtension(target).ToLowerInvariant();
            var contentType = ext == ".pdf" ? "application/pdf" : "image/jpeg";

            return Results.File(target, contentType, enableRangeProcessing: true);
        });

        // 3. Download CSV Template endpoint
        group.MapGet("/template-csv", ([FromServices] IBiometricFormService biometricService) =>
        {
            var bytes = biometricService.GenerateBulkCsvTemplate();
            return Results.File(bytes, "text/csv", "sample_bulk_biometric_forms.csv");
        });

        return app;
    }

    private static string Sanitize(string input)
    {
        var clean = input?.Trim() ?? "";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(clean.Where(c => !invalid.Contains(c) && c != '/' && c != '\\').ToArray());
    }
}

