using System.IO.Compression;
using System.Text;
using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Domain.Entities;
using Ebios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.Services;

public class BiometricFormService : IBiometricFormService
{
    private readonly AppDbContext _context;
    private readonly IEbiosSoapService _ebiosSoapService;
    private readonly ILogger<BiometricFormService> _logger;
    private readonly string _storageBasePath;

    public BiometricFormService(
        AppDbContext context,
        IEbiosSoapService ebiosSoapService,
        ILogger<BiometricFormService> logger)
    {
        _context = context;
        _ebiosSoapService = ebiosSoapService;
        _logger = logger;

        // Base directory: storage/biometric_forms under application root (handles running from root or web project)
        var webStorage = Path.Combine(Directory.GetCurrentDirectory(), "src", "Ebios.Web", "storage", "biometric_forms");
        if (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "src", "Ebios.Web")))
        {
            _storageBasePath = webStorage;
        }
        else
        {
            _storageBasePath = Path.Combine(Directory.GetCurrentDirectory(), "storage", "biometric_forms");
        }

        if (!Directory.Exists(_storageBasePath))
        {
            Directory.CreateDirectory(_storageBasePath);
        }
    }

    public string GetPhysicalStoragePath(string empCode)
    {
        var safeEmpCode = SanitizeEmpCode(empCode);
        var dir = Path.Combine(_storageBasePath, safeEmpCode);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public async Task<BiometricFormDto> GetOrCreateFormByEmpCodeAsync(string empCode)
    {
        var cleanCode = empCode?.Trim() ?? string.Empty;
        var existing = await _context.EmployeeBiometricForms
            .Include(f => f.Employee)
            .FirstOrDefaultAsync(f => f.EmpCode.ToLower() == cleanCode.ToLower());

        if (existing != null)
        {
            return MapToDto(existing);
        }

        // Check if employee exists to prefill particulars
        var emp = await _context.Employees
            .Include(e => e.SubArea)
                .ThenInclude(s => s!.Area)
            .FirstOrDefaultAsync(e => e.EmpCode.ToLower() == cleanCode.ToLower());

        var newForm = new EmployeeBiometricForm
        {
            EmployeeId = emp?.Id,
            EmpCode = emp?.EmpCode ?? cleanCode,
            EmployeeName = emp?.FullName ?? string.Empty,
            Category = (emp != null && emp.IsHigherAuthority) ? "Executive" : "Non-Executive",
            PositionDesignation = emp?.Designation ?? "Colliery Worker",
            DepartmentUnit = emp?.SubArea?.SubAreaName ?? string.Empty,
            PlaceOfPosting = emp?.SubArea?.Area?.AreaName ?? string.Empty,
            RegistrationDateTime = DateTime.UtcNow,
            RegistrationLocation = emp?.SubArea?.SubAreaName ?? string.Empty,
            EclHrDepartment = "HR",
            EclHrOrganization = "ECL",
            EclSecurityDepartment = "Security",
            EclSecurityOrganization = "ECL",
            WebelOrganization = "WEBEL",
            Witness4Organization = "ECL / WEBEL",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.EmployeeBiometricForms.Add(newForm);
        await _context.SaveChangesAsync();

        return MapToDto(newForm);
    }

    public async Task<BiometricFormDto> SaveFormAsync(BiometricFormDto dto, string updatedBy)
    {
        var cleanCode = dto.EmpCode?.Trim() ?? string.Empty;
        var entity = await _context.EmployeeBiometricForms
            .FirstOrDefaultAsync(f => f.EmpCode.ToLower() == cleanCode.ToLower());

        if (entity == null)
        {
            entity = new EmployeeBiometricForm
            {
                EmpCode = cleanCode,
                CreatedAt = DateTime.UtcNow
            };
            _context.EmployeeBiometricForms.Add(entity);
        }

        // Link or auto-onboard employee through biometric form
        bool isExecutive = !string.IsNullOrWhiteSpace(dto.Category) && 
                           dto.Category.Contains("Executive", StringComparison.OrdinalIgnoreCase) && 
                           !dto.Category.Contains("Non", StringComparison.OrdinalIgnoreCase);

        // Location matching against ECL SubAreas (case-insensitive)
        var allSubAreas = await _context.SubAreas.Include(s => s.Area).AsNoTracking().ToListAsync();
        var allAreas = await _context.Areas.AsNoTracking().ToListAsync();

        string locCandidate = dto.DepartmentUnit?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(locCandidate)) locCandidate = dto.PlaceOfPosting?.Trim() ?? "";

        SubArea? matchedSubArea = MatchSubAreaCaseInsensitive(locCandidate, allSubAreas, allAreas);

        var emp = await _context.Employees.FirstOrDefaultAsync(e => e.EmpCode.ToLower() == cleanCode.ToLower());
        if (emp == null)
        {
            // Worker does not exist yet. Colliery location is strictly COMPULSORY to onboard.
            if (matchedSubArea == null)
            {
                string locDesc = string.IsNullOrWhiteSpace(locCandidate) ? "MISSING" : locCandidate;
                throw new InvalidOperationException($"Cannot onboard worker '{cleanCode}': Colliery location '{locDesc}' is missing or does not match any ECL Colliery Unit (case-insensitive check). Location is compulsory.");
            }

            emp = new Employee
            {
                EmpCode = cleanCode.ToUpperInvariant(),
                FullName = !string.IsNullOrWhiteSpace(dto.EmployeeName) ? dto.EmployeeName.Trim() : cleanCode.ToUpperInvariant(),
                Designation = "Colliery Worker",
                SubAreaId = matchedSubArea.Id,
                IsHigherAuthority = isExecutive,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Employees.Add(emp);
            await _context.SaveChangesAsync();

            entity.EmployeeId = emp.Id;

            // Retroactively link past flagged punches & muster summaries
            try
            {
                var pastFlagged = await _context.AttendanceLogs
                    .Where(l => l.EmpCode.ToUpper() == emp.EmpCode && (l.EmployeeId == null || l.IsFlagged))
                    .ToListAsync();
                foreach (var p in pastFlagged)
                {
                    p.EmployeeId = emp.Id;
                    p.Status = "Verified";
                    p.IsFlagged = false;
                }

                var pastSummaries = await _context.AttendanceDailySummaries
                    .Where(s => s.EmpCode.ToUpper() == emp.EmpCode && (s.EmployeeId == null || s.IsFlagged))
                    .ToListAsync();
                foreach (var s in pastSummaries)
                {
                    s.EmployeeId = emp.Id;
                    s.EmployeeName = emp.FullName;
                    s.Designation = emp.Designation;
                    s.IsVerified = true;
                    s.IsFlagged = false;
                    s.IsHigherAuthority = emp.IsHigherAuthority;
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception retroEx)
            {
                _logger.LogWarning("Retroactive linking warning on biometric save for {Code}: {Msg}", emp.EmpCode, retroEx.Message);
            }

            // Sync newly onboarded employee to eBios SOAP server
            try
            {
                var (soapOk, _) = await _ebiosSoapService.UpdateEmployeeAsync(
                    emp.EmpCode,
                    emp.FullName,
                    matchedSubArea.SubAreaCode,
                    "Employee",
                    "None"
                );
                if (soapOk)
                {
                    emp.IsSyncedToEbios = true;
                    emp.LastSyncedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception soapEx)
            {
                _logger.LogWarning("SOAP sync warning on biometric save for {Code}: {Msg}", emp.EmpCode, soapEx.Message);
            }
        }
        else
        {
            // Employee already exists: link and update particulars (do NOT overwrite Designation)
            entity.EmployeeId = emp.Id;
            if (!string.IsNullOrWhiteSpace(dto.EmployeeName)) emp.FullName = dto.EmployeeName.Trim();
            if (isExecutive) emp.IsHigherAuthority = true;

            if (!string.IsNullOrWhiteSpace(locCandidate))
            {
                if (matchedSubArea == null)
                {
                    throw new InvalidOperationException($"Colliery location '{locCandidate}' does not match any ECL Colliery Unit (case-insensitive check).");
                }

                if (emp.SubAreaId != matchedSubArea.Id)
                {
                    emp.SubAreaId = matchedSubArea.Id;
                    try
                    {
                        var (soapOk, _) = await _ebiosSoapService.UpdateEmployeeAsync(
                            emp.EmpCode,
                            emp.FullName,
                            matchedSubArea.SubAreaCode,
                            "Employee",
                            "None"
                        );
                        if (soapOk)
                        {
                            emp.IsSyncedToEbios = true;
                            emp.LastSyncedAt = DateTime.UtcNow;
                        }
                    }
                    catch (Exception soapEx)
                    {
                        _logger.LogWarning("SOAP sync warning on biometric transfer for {Code}: {Msg}", emp.EmpCode, soapEx.Message);
                    }
                }
            }
        }

        // Update Section 1
        entity.EmployeeName = dto.EmployeeName?.Trim() ?? string.Empty;
        entity.AadhaarNumber = dto.AadhaarNumber?.Trim() ?? string.Empty;
        entity.Category = string.IsNullOrWhiteSpace(dto.Category) ? "Non-Executive" : dto.Category.Trim();
        entity.PositionDesignation = dto.PositionDesignation?.Trim() ?? string.Empty;
        entity.DepartmentUnit = dto.DepartmentUnit?.Trim() ?? string.Empty;
        entity.PlaceOfPosting = dto.PlaceOfPosting?.Trim() ?? string.Empty;
        entity.RegistrationDateTime = dto.RegistrationDateTime.HasValue 
            ? (dto.RegistrationDateTime.Value.Kind == DateTimeKind.Utc 
                ? dto.RegistrationDateTime.Value 
                : DateTime.SpecifyKind(dto.RegistrationDateTime.Value, DateTimeKind.Utc))
            : null;
        entity.RegistrationLocation = dto.RegistrationLocation?.Trim() ?? string.Empty;
        entity.ControllingOfficerName = dto.ControllingOfficerName?.Trim() ?? string.Empty;
        entity.ControllingOfficerDesignation = dto.ControllingOfficerDesignation?.Trim() ?? string.Empty;

        // Update Section 3: Personnel Present
        entity.EclHrName = dto.EclHrName?.Trim();
        entity.EclHrDesignation = dto.EclHrDesignation?.Trim();
        entity.EclHrDepartment = string.IsNullOrWhiteSpace(dto.EclHrDepartment) ? "HR" : dto.EclHrDepartment.Trim();
        entity.EclHrOrganization = string.IsNullOrWhiteSpace(dto.EclHrOrganization) ? "ECL" : dto.EclHrOrganization.Trim();

        entity.EclSecurityName = dto.EclSecurityName?.Trim();
        entity.EclSecurityDesignation = dto.EclSecurityDesignation?.Trim();
        entity.EclSecurityDepartment = string.IsNullOrWhiteSpace(dto.EclSecurityDepartment) ? "Security" : dto.EclSecurityDepartment.Trim();
        entity.EclSecurityOrganization = string.IsNullOrWhiteSpace(dto.EclSecurityOrganization) ? "ECL" : dto.EclSecurityOrganization.Trim();

        entity.WebelName = dto.WebelName?.Trim();
        entity.WebelDesignation = dto.WebelDesignation?.Trim();
        entity.WebelDepartment = dto.WebelDepartment?.Trim();
        entity.WebelOrganization = string.IsNullOrWhiteSpace(dto.WebelOrganization) ? "WEBEL" : dto.WebelOrganization.Trim();

        entity.Witness4Name = dto.Witness4Name?.Trim();
        entity.Witness4Designation = dto.Witness4Designation?.Trim();
        entity.Witness4Department = dto.Witness4Department?.Trim();
        entity.Witness4Organization = string.IsNullOrWhiteSpace(dto.Witness4Organization) ? "ECL / WEBEL" : dto.Witness4Organization.Trim();

        entity.IsCompleted = dto.IsCompleted;
        entity.UpdatedBy = updatedBy;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return MapToDto(entity);
    }

    public async Task<string> UploadPhotoAsync(string empCode, string boxType, Stream fileStream, string fileName, string updatedBy)
    {
        var safeEmpCode = SanitizeEmpCode(empCode);
        var normalizedBox = NormalizeBoxType(boxType);
        if (string.IsNullOrEmpty(normalizedBox))
        {
            throw new ArgumentException($"Invalid photo box type: '{boxType}'. Expected aadhaar, idcard, person, or template.");
        }

        var empDir = GetPhysicalStoragePath(safeEmpCode);
        var targetFileName = $"{safeEmpCode}_{normalizedBox}.jpg";
        var fullPath = Path.Combine(empDir, targetFileName);

        // Save stream directly to disk as JPG
        using (var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await fileStream.CopyToAsync(output);
        }

        // Relative path stored in database
        var relativePath = $"storage/biometric_forms/{safeEmpCode}/{targetFileName}";

        // Update database record
        var form = await _context.EmployeeBiometricForms.FirstOrDefaultAsync(f => f.EmpCode.ToLower() == safeEmpCode.ToLower());
        if (form == null)
        {
            form = new EmployeeBiometricForm
            {
                EmpCode = safeEmpCode,
                CreatedAt = DateTime.UtcNow
            };
            _context.EmployeeBiometricForms.Add(form);
        }

        switch (normalizedBox)
        {
            case "aadhaar":
                form.AadhaarPhotoPath = relativePath;
                break;
            case "idcard":
                form.IdCardPhotoPath = relativePath;
                break;
            case "person":
                form.PersonPhotoPath = relativePath;
                break;
            case "template":
                form.TemplatePhotoPath = relativePath;
                break;
        }

        form.UpdatedBy = updatedBy;
        form.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Saved photo {BoxType} for employee {EmpCode} to {Path}", normalizedBox, safeEmpCode, relativePath);

        return relativePath;
    }

    public async Task<string> UploadSignedPhysicalFormAsync(string empCode, Stream fileStream, string fileName, string updatedBy)
    {
        var safeEmpCode = SanitizeEmpCode(empCode);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext != ".pdf" && ext != ".jpg" && ext != ".jpeg" && ext != ".png")
        {
            ext = ".pdf";
        }

        var empDir = GetPhysicalStoragePath(safeEmpCode);
        var targetFileName = $"{safeEmpCode}_signed_form{ext}";
        var fullPath = Path.Combine(empDir, targetFileName);

        using (var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await fileStream.CopyToAsync(output);
        }

        var relativePath = $"storage/biometric_forms/{safeEmpCode}/{targetFileName}";

        var form = await _context.EmployeeBiometricForms.FirstOrDefaultAsync(f => f.EmpCode.ToLower() == safeEmpCode.ToLower());
        if (form == null)
        {
            form = new EmployeeBiometricForm
            {
                EmpCode = safeEmpCode,
                CreatedAt = DateTime.UtcNow
            };
            _context.EmployeeBiometricForms.Add(form);
        }

        form.SignedPhysicalFormPath = relativePath;
        form.UpdatedBy = updatedBy;
        form.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Saved physical signed form for employee {EmpCode} to {Path}", safeEmpCode, relativePath);

        return relativePath;
    }

    public async Task<BulkBiometricImportResult> ImportBulkCsvAsync(Stream csvStream, string updatedBy)
    {
        var result = new BulkBiometricImportResult();
        using var reader = new StreamReader(csvStream, Encoding.UTF8);

        string? headerLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            result.Errors.Add("CSV file is completely empty.");
            return result;
        }

        var allSubAreas = await _context.SubAreas.Include(s => s.Area).AsNoTracking().ToListAsync();
        var allAreas = await _context.Areas.AsNoTracking().ToListAsync();

        var headers = headerLine.Split(',').Select(h => h.Trim().Trim('"').ToLowerInvariant()).ToList();
        int idxEmpCode = headers.FindIndex(h => h.Contains("emp") || h.Contains("id"));
        if (idxEmpCode == -1)
        {
            result.Errors.Add("Could not find Employee ID column (header must contain 'Emp' or 'ID').");
            return result;
        }

        int idxName = headers.FindIndex(h => h.Contains("name") && !h.Contains("officer") && !h.Contains("hr") && !h.Contains("security") && !h.Contains("webel") && !h.Contains("witness"));
        int idxAadhaar = headers.FindIndex(h => h.Contains("aadhaar") || h.Contains("aadhar"));
        int idxCategory = headers.FindIndex(h => h.Contains("category"));
        int idxDesignation = headers.FindIndex(h => h.Contains("designation") || h.Contains("position"));
        int idxUnit = headers.FindIndex(h => h.Contains("unit") || h.Contains("department"));
        int idxPosting = headers.FindIndex(h => h.Contains("posting") || h.Contains("place"));
        int idxRegDate = headers.FindIndex(h => h.Contains("reg") && (h.Contains("date") || h.Contains("time")));
        int idxLocation = headers.FindIndex(h => h.Contains("reg") && h.Contains("location"));
        int idxOfficerName = headers.FindIndex(h => h.Contains("controlling") && h.Contains("name"));
        int idxOfficerDesig = headers.FindIndex(h => h.Contains("controlling") && h.Contains("desig"));

        // Committee columns
        int idxHrName = headers.FindIndex(h => h.Contains("hr") && h.Contains("name"));
        int idxHrDesig = headers.FindIndex(h => h.Contains("hr") && (h.Contains("desig") || h.Contains("position")));
        int idxSecName = headers.FindIndex(h => h.Contains("sec") && h.Contains("name"));
        int idxSecDesig = headers.FindIndex(h => h.Contains("sec") && (h.Contains("desig") || h.Contains("position")));
        int idxWebelName = headers.FindIndex(h => h.Contains("webel") && h.Contains("name"));
        int idxWebelDesig = headers.FindIndex(h => h.Contains("webel") && (h.Contains("desig") || h.Contains("position")));
        int idxWebelDept = headers.FindIndex(h => h.Contains("webel") && h.Contains("dept"));
        int idxWit4Name = headers.FindIndex(h => (h.Contains("wit") || h.Contains("ecl_webel")) && h.Contains("name"));
        int idxWit4Desig = headers.FindIndex(h => (h.Contains("wit") || h.Contains("ecl_webel")) && (h.Contains("desig") || h.Contains("position")));

        int lineNum = 1;
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            result.TotalProcessed++;
            try
            {
                var cols = ParseCsvRow(line);
                if (cols.Count <= idxEmpCode) continue;

                var empCode = cols[idxEmpCode].Trim();
                if (string.IsNullOrWhiteSpace(empCode)) continue;

                var form = await _context.EmployeeBiometricForms.FirstOrDefaultAsync(f => f.EmpCode.ToLower() == empCode.ToLower());
                if (form == null)
                {
                    form = new EmployeeBiometricForm
                    {
                        EmpCode = empCode,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.EmployeeBiometricForms.Add(form);
                }

                if (idxName >= 0 && idxName < cols.Count && !string.IsNullOrWhiteSpace(cols[idxName])) form.EmployeeName = cols[idxName].Trim();
                if (idxAadhaar >= 0 && idxAadhaar < cols.Count && !string.IsNullOrWhiteSpace(cols[idxAadhaar])) form.AadhaarNumber = cols[idxAadhaar].Trim();
                if (idxCategory >= 0 && idxCategory < cols.Count && !string.IsNullOrWhiteSpace(cols[idxCategory])) form.Category = cols[idxCategory].Trim();
                if (idxDesignation >= 0 && idxDesignation < cols.Count && !string.IsNullOrWhiteSpace(cols[idxDesignation])) form.PositionDesignation = cols[idxDesignation].Trim();
                if (idxUnit >= 0 && idxUnit < cols.Count && !string.IsNullOrWhiteSpace(cols[idxUnit])) form.DepartmentUnit = cols[idxUnit].Trim();
                if (idxPosting >= 0 && idxPosting < cols.Count && !string.IsNullOrWhiteSpace(cols[idxPosting])) form.PlaceOfPosting = cols[idxPosting].Trim();
                if (idxRegDate >= 0 && idxRegDate < cols.Count && DateTime.TryParse(cols[idxRegDate], out var dt))
                {
                    form.RegistrationDateTime = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }
                if (idxLocation >= 0 && idxLocation < cols.Count && !string.IsNullOrWhiteSpace(cols[idxLocation])) form.RegistrationLocation = cols[idxLocation].Trim();
                if (idxOfficerName >= 0 && idxOfficerName < cols.Count && !string.IsNullOrWhiteSpace(cols[idxOfficerName])) form.ControllingOfficerName = cols[idxOfficerName].Trim();
                if (idxOfficerDesig >= 0 && idxOfficerDesig < cols.Count && !string.IsNullOrWhiteSpace(cols[idxOfficerDesig])) form.ControllingOfficerDesignation = cols[idxOfficerDesig].Trim();

                // Committee
                if (idxHrName >= 0 && idxHrName < cols.Count && !string.IsNullOrWhiteSpace(cols[idxHrName])) form.EclHrName = cols[idxHrName].Trim();
                if (idxHrDesig >= 0 && idxHrDesig < cols.Count && !string.IsNullOrWhiteSpace(cols[idxHrDesig])) form.EclHrDesignation = cols[idxHrDesig].Trim();
                if (idxSecName >= 0 && idxSecName < cols.Count && !string.IsNullOrWhiteSpace(cols[idxSecName])) form.EclSecurityName = cols[idxSecName].Trim();
                if (idxSecDesig >= 0 && idxSecDesig < cols.Count && !string.IsNullOrWhiteSpace(cols[idxSecDesig])) form.EclSecurityDesignation = cols[idxSecDesig].Trim();
                if (idxWebelName >= 0 && idxWebelName < cols.Count && !string.IsNullOrWhiteSpace(cols[idxWebelName])) form.WebelName = cols[idxWebelName].Trim();
                if (idxWebelDesig >= 0 && idxWebelDesig < cols.Count && !string.IsNullOrWhiteSpace(cols[idxWebelDesig])) form.WebelDesignation = cols[idxWebelDesig].Trim();
                if (idxWebelDept >= 0 && idxWebelDept < cols.Count && !string.IsNullOrWhiteSpace(cols[idxWebelDept])) form.WebelDepartment = cols[idxWebelDept].Trim();
                if (idxWit4Name >= 0 && idxWit4Name < cols.Count && !string.IsNullOrWhiteSpace(cols[idxWit4Name])) form.Witness4Name = cols[idxWit4Name].Trim();
                if (idxWit4Desig >= 0 && idxWit4Desig < cols.Count && !string.IsNullOrWhiteSpace(cols[idxWit4Desig])) form.Witness4Designation = cols[idxWit4Desig].Trim();

                form.UpdatedBy = updatedBy;
                form.UpdatedAt = DateTime.UtcNow;

                // Location validation against ECL SubAreas (case-insensitive)
                string locCandidate = form.DepartmentUnit?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(locCandidate)) locCandidate = form.PlaceOfPosting?.Trim() ?? "";

                SubArea? matchedSubArea = MatchSubAreaCaseInsensitive(locCandidate, allSubAreas, allAreas);

                if (matchedSubArea == null)
                {
                    result.FailureCount++;
                    string locDesc = string.IsNullOrWhiteSpace(locCandidate) ? "MISSING" : locCandidate;
                    result.Errors.Add($"Line {lineNum} ({empCode}): Location '{locDesc}' is missing or does not match any ECL Colliery Unit (case-insensitive check). Worker NOT onboarded.");
                    continue;
                }

                // Check worker existence: update if exists, or auto-onboard if new
                bool isExecutive = !string.IsNullOrWhiteSpace(form.Category) && 
                                   form.Category.Contains("Executive", StringComparison.OrdinalIgnoreCase) && 
                                   !form.Category.Contains("Non", StringComparison.OrdinalIgnoreCase);

                var emp = await _context.Employees.FirstOrDefaultAsync(e => e.EmpCode.ToLower() == empCode.ToLower());
                if (emp != null)
                {
                    // Existing worker: update details (do NOT overwrite Designation from form)
                    if (!string.IsNullOrWhiteSpace(form.EmployeeName)) emp.FullName = form.EmployeeName;
                    emp.SubAreaId = matchedSubArea.Id;
                    if (isExecutive) emp.IsHigherAuthority = true;

                    form.EmployeeId = emp.Id;

                    // Sync to eBios SOAP server
                    try
                    {
                        var (soapOk, _) = await _ebiosSoapService.UpdateEmployeeAsync(
                            emp.EmpCode,
                            emp.FullName,
                            matchedSubArea.SubAreaCode,
                            "Employee",
                            "None"
                        );
                        if (soapOk)
                        {
                            emp.IsSyncedToEbios = true;
                            emp.LastSyncedAt = DateTime.UtcNow;
                        }
                    }
                    catch (Exception soapEx)
                    {
                        _logger.LogWarning("SOAP sync warning in bulk CSV for {Code}: {Msg}", emp.EmpCode, soapEx.Message);
                    }
                }
                else
                {
                    // Direct onboard worker from bulk template with mandatory location
                    var newEmp = new Employee
                    {
                        EmpCode = empCode.ToUpperInvariant(),
                        FullName = !string.IsNullOrWhiteSpace(form.EmployeeName) ? form.EmployeeName : empCode.ToUpperInvariant(),
                        Designation = "Colliery Worker",
                        SubAreaId = matchedSubArea.Id,
                        IsHigherAuthority = isExecutive,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Employees.Add(newEmp);
                    await _context.SaveChangesAsync();

                    form.EmployeeId = newEmp.Id;

                    // Retroactively link past flagged punches & muster summaries
                    try
                    {
                        var pastPunches = await _context.AttendanceLogs
                            .Where(l => l.EmpCode.ToUpper() == newEmp.EmpCode && (l.EmployeeId == null || l.IsFlagged))
                            .ToListAsync();
                        foreach (var p in pastPunches)
                        {
                            p.EmployeeId = newEmp.Id;
                            p.Status = "Verified";
                            p.IsFlagged = false;
                        }

                        var pastSummaries = await _context.AttendanceDailySummaries
                            .Where(s => s.EmpCode.ToUpper() == newEmp.EmpCode && (s.EmployeeId == null || s.IsFlagged))
                            .ToListAsync();
                        foreach (var s in pastSummaries)
                        {
                            s.EmployeeId = newEmp.Id;
                            s.EmployeeName = newEmp.FullName;
                            s.Designation = newEmp.Designation;
                            s.IsVerified = true;
                            s.IsFlagged = false;
                            s.IsHigherAuthority = newEmp.IsHigherAuthority;
                        }
                    }
                    catch (Exception retroEx)
                    {
                        _logger.LogWarning("Retroactive linking warning for worker {Code}: {Msg}", newEmp.EmpCode, retroEx.Message);
                    }

                    // Sync newly onboarded employee to eBios SOAP server
                    try
                    {
                        var (soapOk, _) = await _ebiosSoapService.UpdateEmployeeAsync(
                            newEmp.EmpCode,
                            newEmp.FullName,
                            matchedSubArea.SubAreaCode,
                            "Employee",
                            "None"
                        );
                        if (soapOk)
                        {
                            newEmp.IsSyncedToEbios = true;
                            newEmp.LastSyncedAt = DateTime.UtcNow;
                        }
                    }
                    catch (Exception soapEx)
                    {
                        _logger.LogWarning("SOAP sync warning in bulk CSV onboard for {Code}: {Msg}", newEmp.EmpCode, soapEx.Message);
                    }
                }

                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailureCount++;
                result.Errors.Add($"Line {lineNum}: {ex.Message}");
            }
        }

        await _context.SaveChangesAsync();
        result.SuccessSummaries.Add($"Successfully imported/updated particulars and synced worker records for {result.SuccessCount} forms.");
        return result;
    }

    public async Task<BulkBiometricImportResult> ImportBulkZipAsync(Stream zipStream, string updatedBy)
    {
        var result = new BulkBiometricImportResult();
        var updatedEmployees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"ebios_bulk_photos_{Guid.NewGuid():N}.zip");
        try
        {
            // Asynchronously copy browser stream to temporary file on disk (avoids Synchronous reads not supported & supports seek)
            using (var tempFileOut = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await zipStream.CopyToAsync(tempFileOut);
            }

            using (var zipFs = new FileStream(tempZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(zipFs, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    // Skip directory entries or macOS meta files
                    if (string.IsNullOrEmpty(entry.Name) || entry.FullName.Contains("__MACOSX"))
                        continue;

                    result.TotalProcessed++;

                    try
                    {
                        // Determine EmpCode and box type
                        // Examples:
                        // TSA011/aadhaar.jpg
                        // TSA011/TSA011_person.jpg
                        // TSA011_idcard.jpg
                        string empCode = "";
                        string fileName = entry.Name;
                        string fullName = entry.FullName.Replace('\\', '/');

                        var pathParts = fullName.Split('/');
                        if (pathParts.Length > 1 && !string.IsNullOrWhiteSpace(pathParts[0]))
                        {
                            empCode = pathParts[0].Trim();
                        }
                        else
                        {
                            // Flat structure: TSA011_aadhaar.jpg
                            var nameOnly = Path.GetFileNameWithoutExtension(fileName);
                            var split = nameOnly.Split(new[] { '_', '-' }, 2);
                            if (split.Length >= 2)
                            {
                                empCode = split[0].Trim();
                            }
                        }

                        if (string.IsNullOrWhiteSpace(empCode))
                        {
                            result.FailureCount++;
                            result.Errors.Add($"Skipped '{entry.FullName}': could not determine EmpCode from folder name or file prefix.");
                            continue;
                        }

                        var safeEmpCode = SanitizeEmpCode(empCode);
                        var lowerName = fileName.ToLowerInvariant();

                        string? boxType = null;
                        bool isSignedHardcopy = false;

                        if (lowerName.Contains("aadhaar") || lowerName.Contains("aadhar") || lowerName.EndsWith("_1.jpg") || lowerName.EndsWith("-1.jpg"))
                        {
                            boxType = "aadhaar";
                        }
                        else if (lowerName.Contains("idcard") || lowerName.Contains("id_card") || lowerName.Contains("voter") || lowerName.Contains("pan") || lowerName.EndsWith("_2.jpg") || lowerName.EndsWith("-2.jpg"))
                        {
                            boxType = "idcard";
                        }
                        else if (lowerName.Contains("person") || lowerName.Contains("face") || lowerName.Contains("photo") || lowerName.Contains("worker") || lowerName.EndsWith("_3.jpg") || lowerName.EndsWith("-3.jpg"))
                        {
                            boxType = "person";
                        }
                        else if (lowerName.Contains("template") || lowerName.Contains("machine") || lowerName.Contains("finger") || lowerName.Contains("fp") || lowerName.EndsWith("_4.jpg") || lowerName.EndsWith("-4.jpg"))
                        {
                            boxType = "template";
                        }
                        else if (lowerName.Contains("signed") || lowerName.Contains("form") || lowerName.EndsWith(".pdf"))
                        {
                            isSignedHardcopy = true;
                        }
                        else
                        {
                            result.FailureCount++;
                            result.Errors.Add($"File '{entry.FullName}': unrecognized box name. Expected aadhaar, idcard, person, template, or signed form.");
                            continue;
                        }

                        using var entryStream = entry.Open();
                        if (isSignedHardcopy)
                        {
                            await UploadSignedPhysicalFormAsync(safeEmpCode, entryStream, fileName, updatedBy);
                        }
                        else if (boxType != null)
                        {
                            await UploadPhotoAsync(safeEmpCode, boxType, entryStream, fileName, updatedBy);
                        }

                        updatedEmployees.Add(safeEmpCode);
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.FailureCount++;
                        result.Errors.Add($"Error processing '{entry.FullName}': {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                try { File.Delete(tempZipPath); } catch { /* ignore */ }
            }
        }

        result.SuccessSummaries.Add($"Successfully imported {result.SuccessCount} photos across {updatedEmployees.Count} worker profiles.");
        return result;
    }

    public byte[] GenerateBulkCsvTemplate()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Employee ID,Employee Name,Aadhaar Number,Category of Employee (Executive/Non-Executive),Position/Designation,Department/Unit,Place of Posting,Registration Date & Time (YYYY-MM-DD HH:MM),Location of Registration,Name of Controlling Officer,Designation of Controlling Officer,ECL HR Name,ECL HR Designation,ECL Security Name,ECL Security Designation,WEBEL Name,WEBEL Designation,WEBEL Department,ECL/WEBEL Witness Name,ECL/WEBEL Witness Designation");
        sb.AppendLine("TSA011,RAMESH KUMAR,123456789012,Non-Executive,Colliery Worker,Pit 1 Colliery Unit,Salanpur Mining Area,2026-11-04 10:30,Pithead Incline Desk,S. K. Verma,Senior Manager (Mining),A. Banerjee,Assistant Manager (HR),R. Singh,Security Inspector,D. Ghosh,Site Engineer,Technical,M. Roy,System Supervisor");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<List<BiometricFormDto>> GetFormsForBulkPrintAsync(int? areaId, int? subAreaId, string? search, bool? isFlagged = null)
    {
        var query = _context.EmployeeBiometricForms
            .Include(f => f.Employee)
                .ThenInclude(e => e!.SubArea)
                    .ThenInclude(s => s!.Area)
            .AsNoTracking()
            .AsQueryable();

        if (isFlagged.HasValue)
        {
            if (isFlagged.Value)
            {
                // Flagged = worker has not yet been onboarded or linked
                query = query.Where(f => f.EmployeeId == null);
            }
            else
            {
                // Non-flagged = officially registered employee
                query = query.Where(f => f.EmployeeId != null);
            }
        }

        if (subAreaId.HasValue && subAreaId.Value > 0)
        {
            query = query.Where(f => (f.Employee != null && f.Employee.SubAreaId == subAreaId.Value) ||
                                     (f.Employee == null && _context.SubAreas.Any(s => s.Id == subAreaId.Value && (f.DepartmentUnit.ToLower() == s.SubAreaName.ToLower() || f.DepartmentUnit.ToLower() == s.SubAreaCode.ToLower()))));
        }
        else if (areaId.HasValue && areaId.Value > 0)
        {
            query = query.Where(f => (f.Employee != null && f.Employee.SubArea != null && f.Employee.SubArea.AreaId == areaId.Value) ||
                                     (f.Employee == null && _context.SubAreas.Any(s => s.AreaId == areaId.Value && ((s.Area != null && f.PlaceOfPosting.ToLower() == s.Area.AreaName.ToLower()) || f.DepartmentUnit.ToLower() == s.SubAreaName.ToLower()))));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(f => f.EmpCode.ToLower().Contains(s) || f.EmployeeName.ToLower().Contains(s));
        }

        var list = await query.OrderBy(f => f.EmpCode).ToListAsync();
        return list.Select(MapToDto).ToList();
    }

    private static BiometricFormDto MapToDto(EmployeeBiometricForm f)
    {
        return new BiometricFormDto
        {
            Id = f.Id,
            EmployeeId = f.EmployeeId,
            EmpCode = f.EmpCode,
            EmployeeName = f.EmployeeName,
            AadhaarNumber = f.AadhaarNumber,
            Category = string.IsNullOrWhiteSpace(f.Category) ? "Non-Executive" : f.Category,
            PositionDesignation = f.PositionDesignation,
            DepartmentUnit = f.DepartmentUnit,
            PlaceOfPosting = f.PlaceOfPosting,
            RegistrationDateTime = f.RegistrationDateTime,
            RegistrationLocation = f.RegistrationLocation,
            ControllingOfficerName = f.ControllingOfficerName,
            ControllingOfficerDesignation = f.ControllingOfficerDesignation,
            AadhaarPhotoPath = f.AadhaarPhotoPath,
            IdCardPhotoPath = f.IdCardPhotoPath,
            PersonPhotoPath = f.PersonPhotoPath,
            TemplatePhotoPath = f.TemplatePhotoPath,
            SignedPhysicalFormPath = f.SignedPhysicalFormPath,
            EclHrName = f.EclHrName,
            EclHrDesignation = f.EclHrDesignation,
            EclHrDepartment = f.EclHrDepartment,
            EclHrOrganization = f.EclHrOrganization,
            EclSecurityName = f.EclSecurityName,
            EclSecurityDesignation = f.EclSecurityDesignation,
            EclSecurityDepartment = f.EclSecurityDepartment,
            EclSecurityOrganization = f.EclSecurityOrganization,
            WebelName = f.WebelName,
            WebelDesignation = f.WebelDesignation,
            WebelDepartment = f.WebelDepartment,
            WebelOrganization = f.WebelOrganization,
            Witness4Name = f.Witness4Name,
            Witness4Designation = f.Witness4Designation,
            Witness4Department = f.Witness4Department,
            Witness4Organization = f.Witness4Organization,
            IsCompleted = f.IsCompleted,
            UpdatedBy = f.UpdatedBy,
            UpdatedAt = f.UpdatedAt
        };
    }

    public static SubArea? MatchSubAreaCaseInsensitive(string? locationInput, IEnumerable<SubArea> allSubAreas, IEnumerable<Area> allAreas)
    {
        if (string.IsNullOrWhiteSpace(locationInput)) return null;
        string target = locationInput.Trim();

        if (int.TryParse(target, out int sid))
        {
            var byId = allSubAreas.FirstOrDefault(s => s.Id == sid);
            if (byId != null) return byId;
        }

        var bySubName = allSubAreas.FirstOrDefault(s => s.SubAreaName.Trim().Equals(target, StringComparison.OrdinalIgnoreCase));
        if (bySubName != null) return bySubName;

        var bySubCode = allSubAreas.FirstOrDefault(s => s.SubAreaCode.Trim().Equals(target, StringComparison.OrdinalIgnoreCase));
        if (bySubCode != null) return bySubCode;

        var areaMatch = allAreas.FirstOrDefault(a => 
            a.AreaName.Trim().Equals(target, StringComparison.OrdinalIgnoreCase) ||
            a.AreaCode.Trim().Equals(target, StringComparison.OrdinalIgnoreCase));
        if (areaMatch != null)
        {
            var firstSub = allSubAreas.FirstOrDefault(s => s.AreaId == areaMatch.Id);
            if (firstSub != null) return firstSub;
        }

        return null;
    }

    private static string SanitizeEmpCode(string empCode)
    {
        var clean = empCode?.Trim() ?? "UNKNOWN";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(clean.Where(c => !invalid.Contains(c) && c != '/' && c != '\\').ToArray());
    }

    private static string? NormalizeBoxType(string boxType)
    {
        var b = boxType?.Trim().ToLowerInvariant() ?? "";
        if (b.Contains("aadhaar") || b.Contains("aadhar") || b == "1") return "aadhaar";
        if (b.Contains("idcard") || b.Contains("id_card") || b.Contains("id") || b == "2") return "idcard";
        if (b.Contains("person") || b.Contains("face") || b.Contains("photo") || b == "3") return "person";
        if (b.Contains("template") || b.Contains("machine") || b.Contains("finger") || b == "4") return "template";
        return null;
    }

    private static List<string> ParseCsvRow(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                {
                    current.Append('\"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result;
    }
}
