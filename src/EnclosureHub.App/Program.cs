using EnclosureHub.App.Api;
using EnclosureHub.App.Install;
using EnclosureHub.App.Modules.SystemSensors;
using EnclosureHub.App.Web;
using EnclosureHub.Camera;
using EnclosureHub.Core;
using EnclosureHub.Core.Settings;
using EnclosureHub.Mqtt;

// `sudo enclosurehub --install` / `--uninstall` manage the systemd service and exit
if (ServiceInstaller.TryHandle(args, out int exitCode))
    return exitCode;

var builder = WebApplication.CreateBuilder(args);

// Local overrides with secrets (MQTT password, …), not committed to git. Also the file the
// dashboard writes settings to; LocalSettingsStore reloads the configuration after each save.
string localSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.Local.json");
builder.Configuration.AddJsonFile(localSettingsPath, optional: true, reloadOnChange: false);

builder.Host.UseSystemd();

var config = builder.Configuration;

builder.Services.Configure<DeviceOptions>(config.GetSection("Device"));
builder.Services.Configure<WebOptions>(config.GetSection("Web"));
builder.Services.AddSingleton<IEntityRegistry, EntityRegistry>();
builder.Services.AddSingleton<ISettingsStore>(new LocalSettingsStore(localSettingsPath, config));

// ---- Modules (each one registers its entities and settings; honours Modules:{Name}:Enabled) ----
builder.Services.AddRpicamCamera(config.GetSection("Modules:Camera"));

if (config.GetValue("Modules:SystemSensors:Enabled", true))
    builder.Services.AddSystemSensors(config.GetSection("Modules:SystemSensors"));

// Dashboard's own settings section, listed after the modules
builder.Services.AddSingleton<ISettingsSection, WebDashboardSettingsSection>();

// ---- Adapters (expose entities to the outside world) ----
if (config.GetValue("Mqtt:Enabled", true))
    builder.Services.AddMqttBridge(config.GetSection("Mqtt"));

var app = builder.Build();

app.UseBasicAuth();        // Web:Auth – everything except /health
app.UseDashboardSwitch();  // Web:Dashboard:Enabled – static files only, API stays
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", (IEntityRegistry registry) => Results.Json(new { status = "ok", entities = registry.All.Count }));
app.MapEntityApi();
app.MapCameraApi();
app.MapSettingsApi();
app.MapSystemApi();

app.Run();
return 0;
