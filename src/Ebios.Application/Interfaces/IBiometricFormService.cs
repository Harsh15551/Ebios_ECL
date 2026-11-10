using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IBiometricFormService
{
    Task<BiometricFormDto> GetOrCreateFormByEmpCodeAsync(string empCode);
    Task<BiometricFormDto> SaveFormAsync(
        BiometricFormDto dto, 
        string updatedBy,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<string> UploadPhotoAsync(
        string empCode, 
        string boxType, 
        Stream fileStream, 
        string fileName, 
        string updatedBy,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null,
        bool skipAudit = false);
    Task<string> UploadSignedPhysicalFormAsync(
        string empCode, 
        Stream fileStream, 
        string fileName, 
        string updatedBy,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null,
        bool skipAudit = false);
    Task<BulkBiometricImportResult> ImportBulkCsvAsync(
        Stream csvStream, 
        string updatedBy,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<BulkBiometricImportResult> ImportBulkZipAsync(
        Stream zipStream, 
        string updatedBy,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    byte[] GenerateBulkCsvTemplate();
    Task<List<BiometricFormDto>> GetFormsForBulkPrintAsync(int? areaId, int? subAreaId, string? search, bool? isFlagged = null);
    string GetPhysicalStoragePath(string empCode);
    Task<(byte[] FileBytes, string ContentType)?> GetPhotoBytesAsync(string empCode, string boxType);
    Task<(byte[] FileBytes, string ContentType)?> GetSignedFormBytesAsync(string empCode);
    Task<int> EncryptAllExistingPlainImagesAsync();
}

