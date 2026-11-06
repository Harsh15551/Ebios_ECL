using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ebios.Application.Common;

/// <summary>
/// Safe parser and formatter for audit trail snapshots.
/// Ensures sensitive internal database keys, queries, hashes, and IDs are never exposed in user-facing UI.
/// </summary>
public static class AuditSnapshotFormatter
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id", "UserId", "EmployeeId", "SubAreaId", "AreaId", 
        "Password", "PasswordHash", "Secret", "Token", "SecurityStamp", "ConcurrencyStamp",
        "Query", "SqlQuery", "ConnectionString"
    };

    private static readonly Dictionary<string, string> FriendlyLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        { "EmpCode", "Worker Code" },
        { "FullName", "Worker Name" },
        { "EmployeeName", "Worker Name" },
        { "OfficerName", "Officer Name" },
        { "Username", "Username" },
        { "Email", "Email Address" },
        { "Role", "Role" },
        { "Designation", "Designation" },
        { "PositionDesignation", "Position / Designation" },
        { "Category", "Cadre Category" },
        { "IsHigherAuthority", "Executive Privilege (Single-Punch)" },
        { "SubAreaCode", "Colliery Unit Code" },
        { "SubAreaName", "Colliery Unit Name" },
        { "DepartmentUnit", "Colliery / Department" },
        { "AreaCode", "Mining Area Code" },
        { "AreaName", "Mining Area Name" },
        { "PlaceOfPosting", "Area / Posting Location" },
        { "Headquarters", "Headquarters Location" },
        { "AdditionalSubAreaIds", "Additional Assigned Pits" },
        { "IsActive", "Active Status" },
        { "CanManageEmployees", "Worker Management Permission" },
        { "CanManageShifts", "Shift Management Permission" },
        { "TwoFactorEnabled", "Two-Factor Authentication" },
        { "FirstInTime", "In Punch Time" },
        { "LastOutTime", "Out Punch Time" },
        { "ShiftCode", "Shift Code" },
        { "ShiftName", "Shift Description" },
        { "StartTime", "Shift Start Time" },
        { "EndTime", "Shift End Time" },
        { "InWindowStart", "In-Punch Window Start" },
        { "InWindowEnd", "In-Punch Window End" },
        { "OutWindowStart", "Out-Punch Window Start" },
        { "OutWindowEnd", "Out-Punch Window End" },
        { "GraceMinutes", "Grace Period (Minutes)" },
        { "DurationHours", "Shift Duration (Hours)" },
        { "IsNightShift", "Overnight Shift" },
        { "RosterDate", "Roster Schedule Date" },
        { "AssignedBy", "Assigned By" },
        { "CorrectionReason", "Reason for Modification" },
        { "CorrectedBy", "Authorizing Officer" },
        { "CorrectedAt", "Modification Timestamp" },
        { "SerialNumber", "Device Serial Number" },
        { "DeviceName", "Device Name" },
        { "DeviceType", "Device Type" },
        { "ActivationCode", "Device Activation Code" },
        { "IpAddress", "Device IP Address" },
        { "Direction", "Turnstile Direction" },
        { "TotalWorkers", "Total Workers" },
        { "BlacklistedCount", "Blacklisted Workers Count" },
        { "TotalRows", "Total Records Processed" },
        { "SuccessCount", "Successful Records" },
        { "FailureCount", "Failed Records" },
        { "Mode", "Batch Import Mode" },
        { "WorkerCount", "Affected Workers Count" },
        { "DatesCovered", "Dates Covered" },
        { "BoxType", "Biometric Photo Box" },
        { "FileName", "Uploaded File" },
        { "StoragePath", "Storage Reference" },
        { "AadhaarNumber", "Aadhaar Number" },
        { "IsCompleted", "Form Verification Complete" }
    };

    public static List<(string Label, string Value)> FormatAttributes(string? json)
    {
        var result = new List<(string Label, string Value)>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (SensitiveKeys.Contains(prop.Name)) continue;

                string label = FriendlyLabels.TryGetValue(prop.Name, out var friendly)
                    ? friendly
                    : Regex.Replace(prop.Name, "([a-z])([A-Z])", "$1 $2");

                string value = prop.Value.ValueKind switch
                {
                    JsonValueKind.True => "Yes (Active / Granted)",
                    JsonValueKind.False => "No (Inactive / Denied)",
                    JsonValueKind.Null => "—",
                    JsonValueKind.String => string.IsNullOrWhiteSpace(prop.Value.GetString()) ? "—" : prop.Value.GetString()!,
                    _ => prop.Value.ToString()
                };

                result.Add((label, value));
            }
        }
        catch
        {
            // If json cannot be parsed, safely ignore rather than crashing or leaking technical syntax
        }

        return result;
    }
}

