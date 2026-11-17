using EnclosureHub.App.Modules.SystemSensors;

namespace EnclosureHub.Tests;

public class SystemSensorsTests
{
    private const string Header = "Inter-| sta-|   Quality        |   Discarded packets               | Missed | WE\n"
        + " face | tus | link level noise |  nwid  crypt   frag  retry   misc | beacon | 22\n";

    [Fact]
    public void Parses_level_of_first_wireless_interface()
    {
        string content = Header + " wlan0: 0000   52.  -58.  -256        0      0      0      0      0        0\n";

        Assert.Equal(-58, SystemSensorsService.ParseWirelessLevel(content));
    }

    [Fact]
    public void Returns_zero_without_wireless_interface_or_link()
    {
        Assert.Equal(0, SystemSensorsService.ParseWirelessLevel(Header));
        Assert.Equal(0, SystemSensorsService.ParseWirelessLevel(""));
        Assert.Equal(0, SystemSensorsService.ParseWirelessLevel(Header + " wlan0: 0000    0.    0.     0        0      0      0      0      0        0\n"));
    }
}
