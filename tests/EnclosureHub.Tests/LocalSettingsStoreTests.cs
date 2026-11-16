using System.Text.Json.Nodes;
using EnclosureHub.App.Web;
using Microsoft.Extensions.Configuration;

namespace EnclosureHub.Tests;

public class LocalSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "enclosurehub-tests-" + Guid.NewGuid().ToString("N"));

    public LocalSettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Merges_into_existing_file_and_keeps_other_values()
    {
        string path = Path.Combine(_dir, "appsettings.Local.json");
        await File.WriteAllTextAsync(path, """
            {
              "Mqtt": { "Host": "192.168.1.10", "Username": "hub", "Password": "secret" },
              "Modules": { "Camera": { "ExtraArgs": "--hflip", "Quality": 90 } }
            }
            """);

        var store = new LocalSettingsStore(path, configuration: null);
        await store.SaveAsync(new Dictionary<string, object?>
        {
            ["Modules:Camera:Quality"] = 70,
            ["Modules:Camera:Enabled"] = false,
            ["Web:Dashboard:Enabled"] = true,
        }, CancellationToken.None);

        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Equal("secret", (string?)json["Mqtt"]!["Password"]);
        Assert.Equal("--hflip", (string?)json["Modules"]!["Camera"]!["ExtraArgs"]);
        Assert.Equal(70, (int?)json["Modules"]!["Camera"]!["Quality"]);
        Assert.False((bool?)json["Modules"]!["Camera"]!["Enabled"]);
        Assert.True((bool?)json["Web"]!["Dashboard"]!["Enabled"]);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Creates_file_and_reloads_configuration()
    {
        string path = Path.Combine(_dir, "appsettings.Local.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: true, reloadOnChange: false).Build();
        Assert.Null(configuration["Modules:Camera:Framerate"]);

        var store = new LocalSettingsStore(path, configuration);
        await store.SaveAsync(new Dictionary<string, object?> { ["Modules:Camera:Framerate"] = 0.5 }, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.Equal("0.5", configuration["Modules:Camera:Framerate"]);
    }
}
