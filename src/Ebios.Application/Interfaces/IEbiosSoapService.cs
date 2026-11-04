namespace Ebios.Application.Interfaces;

public interface IEbiosSoapService
{
    Task<(bool success, string message)> UpdateLocationAsync(string locationCode, string locationDescription);
    Task<(bool success, string message)> UpdateDeviceAsync(
        string serialNumber, 
        string name, 
        string direction, 
        string deviceType, 
        string timeZone, 
        string activationCode,
        string locationCode);
    Task<(bool success, string message)> UpdateEmployeeAsync(
        string empCode, 
        string empName, 
        string locationCode, 
        string role = "Employee", 
        string verificationType = "None");
    Task<(bool success, string message)> BlockUnblockUserOnDeviceAsync(
        string deviceSerialNumber, 
        string employeeCode, 
        bool blockUser);
}
