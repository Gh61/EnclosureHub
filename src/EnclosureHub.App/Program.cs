using EnclosureHub.App.Api;
using EnclosureHub.App.Modules.SystemSensors;
using EnclosureHub.Camera;
using EnclosureHub.Core;
using EnclosureHub.Mqtt;

var builder = WebApplication.CreateBuilder(args);

// Local overrides with secrets (MQTT password, …), not committed to git
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Host.UseSystemd();

var config = builder.Configuration;

builder.Services.Configure<DeviceOptions>(config.GetSection("Device"));
builder.Services.AddSingleton<IEntityRegistry, EntityRegistry>();

// ---- Modules (each one registers its entities) ----
if (config.GetValue("Modules:Camera:Enabled", true))
    builder.Services.AddRpicamCamera(config.GetSection("Modules:Camera"));

if (config.GetValue("Modules:SystemSensors:Enabled", true))
    builder.Services.AddSystemSensors(config.GetSection("Modules:SystemSensors"));

// ---- Adapters (expose entities to the outside world) ----
if (config.GetValue("Mqtt:Enabled", true))
    builder.Services.AddMqttBridge(config.GetSection("Mqtt"));

var app = builder.Build();

app.MapGet("/", () => Results.Content(IndexPage.Html, "text/html"));
app.MapGet("/health", (IEntityRegistry registry) => Results.Json(new { status = "ok", entities = registry.All.Count }));
app.MapEntityApi();
app.MapCameraApi();

app.Run();
