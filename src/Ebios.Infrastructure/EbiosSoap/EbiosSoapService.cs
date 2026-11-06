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

        _endpointUrl = Environment.GetEnvironmentVariable("EBIOS_URL") 
                       ?? configuration["EbiosServer:Url"] 
                       ?? string.Empty;
        _userName = Environment.GetEnvironmentVariable("EBIOS_USERNAME") 
                    ?? configuration["EbiosServer:UserName"] 
                    ?? string.Empty;
        _password = Environment.GetEnvironmentVariable("EBIOS_PASSWORD") 
                    ?? configuration["EbiosServer:Password"] 
                    ?? string.Empty;
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
        if (string.IsNullOrWhiteSpace(_endpointUrl))
        {
            return (false, "Central Biometric Server is not configured.");
        }

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
                return (false, "Central Biometric Server returned an unexpected error.");
            }

            var xdoc = XDocument.Parse(responseXml);
            var resultNode = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == resultElementName);
            string resultText = resultNode?.Value ?? string.Empty;

            _logger.LogInformation("eBios SOAP response for {Action}: {Result}", resultElementName, string.IsNullOrWhiteSpace(resultText) ? responseXml : resultText);

            if (resultElementName == "DeviceCommand_BlockUnBlockUserResult")
            {
                // Official eBioServer specification:
                // "Result will be displayed error if any input values are invalid else command will be executed"
                bool isError = resultText.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                               resultText.Contains("invalid", StringComparison.OrdinalIgnoreCase);

                if (isError)
                {
                    _logger.LogWarning("DeviceCommand_BlockUnBlockUser rejected: {Result}", resultText);
                    return (false, "Device command rejected by Biometric Server: invalid device serial or worker code.");
                }

                if (resultNode != null)
                {
                    _logger.LogInformation("DeviceCommand_BlockUnBlockUser executed successfully: {Result}", resultText);
                    return (true, string.IsNullOrWhiteSpace(resultText) ? "Command executed successfully on biometric device." : resultText);
                }

                return (false, "Biometric server did not return a valid command confirmation.");
            }

            bool isSuccess = resultText.Contains("Success", StringComparison.OrdinalIgnoreCase) || 
                             resultText.Contains("Saved", StringComparison.OrdinalIgnoreCase) ||
                             resultText.Contains("Updated", StringComparison.OrdinalIgnoreCase);

            return (isSuccess, resultText);
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("eBios SOAP request timed out connecting to {Url}", _endpointUrl);
            return (false, "Central Biometric Server request timed out. Please check network/VPN connection.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP connection error while reaching eBioServer at {Url}", _endpointUrl);
            return (false, "Cannot connect to Central Biometric Server. Server is unreachable or offline.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call eBios SOAP API at {Url}", _endpointUrl);
            return (false, "Biometric server communication error.");
        }
    }
}
