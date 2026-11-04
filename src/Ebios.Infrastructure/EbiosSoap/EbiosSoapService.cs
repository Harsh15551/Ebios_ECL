using System.Security;
using System.Text;
using System.Xml.Linq;
using Ebios.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.EbiosSoap;

public class EbiosSoapService : IEbiosSoapService
{
    private readonly HttpClient _httpClient;
    private readonly string _endpointUrl;
    private readonly string _userName;
    private readonly string _password;
    private readonly ILogger<EbiosSoapService> _logger;

    public EbiosSoapService(HttpClient httpClient, IConfiguration configuration, ILogger<EbiosSoapService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _endpointUrl = configuration["EbiosServer:Url"] ?? string.Empty;
        _userName = configuration["EbiosServer:UserName"] ?? string.Empty;
        _password = configuration["EbiosServer:Password"] ?? string.Empty;
    }

    public async Task<(bool success, string message)> UpdateLocationAsync(string locationCode, string locationDescription)
    {
        string soapAction = "\"http://tempuri.org/UpdateLocation\"";
        string xmlPayload = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <UpdateLocation xmlns=""http://tempuri.org/"">
      <UserName>{SecurityElement.Escape(_userName)}</UserName>
      <Password>{SecurityElement.Escape(_password)}</Password>
      <LocationCode>{SecurityElement.Escape(locationCode)}</LocationCode>
      <LocationDescription>{SecurityElement.Escape(locationDescription)}</LocationDescription>
    </UpdateLocation>
  </soap:Body>
</soap:Envelope>";

        return await SendSoapRequestAsync(soapAction, xmlPayload, "UpdateLocationResult");
    }

    public async Task<(bool success, string message)> UpdateDeviceAsync(
        string serialNumber, 
        string name, 
        string direction, 
        string deviceType, 
        string timeZone, 
        string activationCode,
        string locationCode)
    {
        string soapAction = "\"http://tempuri.org/UpdateDevice\"";
        string xmlPayload = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <UpdateDevice xmlns=""http://tempuri.org/"">
      <UserName>{SecurityElement.Escape(_userName)}</UserName>
      <Password>{SecurityElement.Escape(_password)}</Password>
      <DeviceSerialNumber>{SecurityElement.Escape(serialNumber)}</DeviceSerialNumber>
      <DeviceName>{SecurityElement.Escape(name)}</DeviceName>
      <DeviceDiretion>{SecurityElement.Escape(direction)}</DeviceDiretion>
      <DeviceType>{SecurityElement.Escape(deviceType)}</DeviceType>
      <TimeZone>{SecurityElement.Escape(timeZone)}</TimeZone>
      <DeviceActivationCode>{SecurityElement.Escape(string.IsNullOrWhiteSpace(activationCode) ? "0" : activationCode)}</DeviceActivationCode>
      <Location>{SecurityElement.Escape(locationCode)}</Location>
      <IsAttendanceDevice>1</IsAttendanceDevice>
    </UpdateDevice>
  </soap:Body>
</soap:Envelope>";

        return await SendSoapRequestAsync(soapAction, xmlPayload, "UpdateDeviceResult");
    }

    public async Task<(bool success, string message)> UpdateEmployeeAsync(
        string empCode, 
        string empName, 
        string locationCode, 
        string role = "Employee", 
        string verificationType = "None")
    {
        string soapAction = "\"http://tempuri.org/UpdateEmployee\"";
        string xmlPayload = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <UpdateEmployee xmlns=""http://tempuri.org/"">
      <UserName>{SecurityElement.Escape(_userName)}</UserName>
      <Password>{SecurityElement.Escape(_password)}</Password>
      <EmployeeCode>{SecurityElement.Escape(empCode)}</EmployeeCode>
      <EmployeeName>{SecurityElement.Escape(empName)}</EmployeeName>
      <EmployeeLocation>{SecurityElement.Escape(locationCode)}</EmployeeLocation>
      <EmployeeRole>{SecurityElement.Escape(role)}</EmployeeRole>
      <EmployeeVerificationType>{SecurityElement.Escape(verificationType)}</EmployeeVerificationType>
    </UpdateEmployee>
  </soap:Body>
</soap:Envelope>";

        return await SendSoapRequestAsync(soapAction, xmlPayload, "UpdateEmployeeResult");
    }

    public async Task<(bool success, string message)> BlockUnblockUserOnDeviceAsync(
        string deviceSerialNumber, 
        string employeeCode, 
        bool blockUser)
    {
        string soapAction = "\"http://tempuri.org/DeviceCommand_BlockUnBlockUser\"";
        string xmlPayload = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <DeviceCommand_BlockUnBlockUser xmlns=""http://tempuri.org/"">
      <UserName>{SecurityElement.Escape(_userName)}</UserName>
      <Password>{SecurityElement.Escape(_password)}</Password>
      <DeviceSerialNumber>{SecurityElement.Escape(deviceSerialNumber)}</DeviceSerialNumber>
      <EmployeeCode>{SecurityElement.Escape(employeeCode)}</EmployeeCode>
      <BlockUser>{(blockUser ? "true" : "false")}</BlockUser>
    </DeviceCommand_BlockUnBlockUser>
  </soap:Body>
</soap:Envelope>";

        return await SendSoapRequestAsync(soapAction, xmlPayload, "DeviceCommand_BlockUnBlockUserResult");
    }

    private async Task<(bool success, string message)> SendSoapRequestAsync(string soapAction, string xmlPayload, string resultElementName)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpointUrl);
            request.Headers.Add("SOAPAction", soapAction);
            request.Content = new StringContent(xmlPayload, Encoding.UTF8, "text/xml");

            using var response = await _httpClient.SendAsync(request);
            string responseXml = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("eBios SOAP error: HTTP {Status} - {Response}", response.StatusCode, responseXml);
                return (false, $"HTTP Error {response.StatusCode}");
            }

            var xdoc = XDocument.Parse(responseXml);
            var resultNode = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == resultElementName);
            string resultText = resultNode?.Value ?? "No result element found in response";

            bool isSuccess = resultText.Contains("Success", StringComparison.OrdinalIgnoreCase) || 
                             resultText.Contains("Saved", StringComparison.OrdinalIgnoreCase) ||
                             resultText.Contains("Updated", StringComparison.OrdinalIgnoreCase);

            return (isSuccess, resultText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call eBios SOAP API");
            return (false, ex.Message);
        }
    }
}
