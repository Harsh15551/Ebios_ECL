using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IBiometricFormService
{
    Task<BiometricFormDto> GetOrCreateFormByEmpCodeAsync(string empCode);
    Task<BiometricFormDto> SaveFormAsync(BiometricFormDto dto, string updatedBy);
    Task<string> UploadPhotoAsync(string empCode, string boxType, Stream fileStream, string fileName, string updatedBy);
    Task<string> UploadSignedPhysicalFormAsync(string empCode, Stream fileStream, string fileName, string updatedBy);
    Task<BulkBiometricImportResult> ImportBulkCsvAsync(Stream csvStream, string updatedBy);
    Task<BulkBiometricImportResult> ImportBulkZipAsync(Stream zipStream, string updatedBy);
    byte[] GenerateBulkCsvTemplate();
    Task<List<BiometricFormDto>> GetFormsForBulkPrintAsync(int? areaId, int? subAreaId, string? search, bool? isFlagged = null);
    string GetPhysicalStoragePath(string empCode);
}

