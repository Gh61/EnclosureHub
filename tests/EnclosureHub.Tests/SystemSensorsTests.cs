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
    public void Parses_cpu_line_of_proc_stat()
    {
        const string stat = "cpu  2255 34 2290 22625563 6290 127 456 0 0 0\ncpu0 1132 34 1441 11311718 3675 127 438 0 0 0\n";
        var parsed = SystemSensorsService.ParseCpuStat(stat);

        Assert.NotNull(parsed);
        Assert.Equal(22625563 + 6290, parsed.Value.Idle);
        Assert.Equal(2255 + 34 + 2290 + 22625563 + 6290 + 127 + 456 + 0, parsed.Value.Total);
        Assert.Null(SystemSensorsService.ParseCpuStat("intr 1 2 3\n"));
    }

    [Fact]
    public void Returns_zero_without_wireless_interface_or_link()
    {
        Assert.Equal(0, SystemSensorsService.ParseWirelessLevel(Header));
        Assert.Equal(0, SystemSensorsService.ParseWirelessLevel(""));
        Assert.Equal(0, SystemSensorsService.ParseWirelessLevel(Header + " wlan0: 0000    0.    0.     0        0      0      0      0      0        0\n"));
    }
}
