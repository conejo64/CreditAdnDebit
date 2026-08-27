using System;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

public class HardwareHsmProvider : IHsmService
{
    private readonly ILogger<HardwareHsmProvider>? _logger;
    private readonly string _host;
    private readonly int _port;

    public HardwareHsmProvider(string host = "localhost", int port = 9998, ILogger<HardwareHsmProvider>? logger = null)
    {
        _host = host;
        _port = port;
        _logger = logger;
    }

    public string BuildMacCommand(string payloadAscii)
    {
        // Thales format: M0 + <key details> + payload length + payload.
        // Simplified for this implementation.
        return $"M0{payloadAscii.Length:D4}{payloadAscii}";
    }

    public string ParseMacResponse(string response)
    {
        // Thales format: M1 + ErrorCode(2) + MAC(16)
        if (response.StartsWith("M1") && response.Length >= 4)
        {
            var errorCode = response.Substring(2, 2);
            if (errorCode == "00")
            {
                return response.Substring(4);
            }
            throw new Exception($"HSM Error: {errorCode}");
        }
        throw new Exception("Invalid HSM Response");
    }

    public string ComputeMacHex(string payloadAscii)
    {
        var command = BuildMacCommand(payloadAscii);
        var response = SendCommand(command);
        return ParseMacResponse(response);
    }

    public bool TryParsePinBlock(string encryptedPinBlock, string accountInfo, out string clearPin)
    {
        // Placeholder for Thales PIN translation command (e.g. CA / CB or NG)
        // For tests, fail safe
        clearPin = string.Empty;
        try 
        {
            var cmd = $"CA{encryptedPinBlock}{accountInfo}";
            var resp = SendCommand(cmd);
            if (resp.StartsWith("CB00")) 
            {
                clearPin = resp.Substring(4, 4);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Hardware PIN parsing failed.");
            return false;
        }
    }

    protected virtual string SendCommand(string command)
    {
        // Network communication to Thales
        // Virtual for easy mocking in tests if needed. For now, simulate network failure if not test.
        throw new SocketException((int)SocketError.ConnectionRefused);
    }
}
