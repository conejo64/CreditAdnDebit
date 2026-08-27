using System;
using Xunit;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;

namespace IsoSwitch.Tests.Infrastructure.Hsm;

public class HardwareHsmProviderTests
{
    [Fact]
    public void BuildMacCommand_FormatsCorrectly()
    {
        // Arrange
        var provider = new HardwareHsmProvider();
        
        // Act
        var command = provider.BuildMacCommand("TEST");

        // Assert
        Assert.StartsWith("M0", command); // Thales MAC command code
        Assert.Contains("TEST", command);
    }

    [Fact]
    public void ParseMacResponse_ParsesCorrectly()
    {
        // Arrange
        var provider = new HardwareHsmProvider();
        var response = "M100MACDATA12345678";
        
        // Act
        var result = provider.ParseMacResponse(response);

        // Assert
        Assert.Equal("MACDATA12345678", result);
    }
}
