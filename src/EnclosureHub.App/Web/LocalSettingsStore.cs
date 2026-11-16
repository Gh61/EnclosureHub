using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnclosureHub.Core.Settings;

namespace EnclosureHub.App.Web;

/// <summary>
/// Writes settings into <c>appsettings.Local.json</c> (merging with whatever is there, e.g. the MQTT
/// password), replaces the file atomically and reloads the configuration so option monitors fire.
/// </summary>
internal sealed class LocalSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private readonly IConfigurationRoot? _configuration;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LocalSettingsStore(string path, IConfigurationRoot? configuration)
    {
        _path = path;
        _configuration = configuration;
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            JsonObject root = new();
            bool exists = File.Exists(_path);
            if (exists)
            {
                string existing = await File.ReadAllTextAsync(_path, cancellationToken);
                if (!string.IsNullOrWhiteSpace(existing))
                    root = JsonNode.Parse(existing, documentOptions: ReadOptions) as JsonObject
                        ?? throw new InvalidOperationException($"{_path} does not contain a JSON object.");
            }

            foreach (var (key, value) in values)
                Set(root, key, value);

            string temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, root.ToJsonString(WriteOptions) + Environment.NewLine, cancellationToken);

            if (OperatingSystem.IsLinux())
            {
                // Keep the file private (it may hold passwords): copy the existing mode or default to 600
                File.SetUnixFileMode(temp, exists ? File.GetUnixFileMode(_path) : UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temp, _path, overwrite: true);
            _configuration?.Reload();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Sets "A:B:C" = value, creating intermediate objects.</summary>
    internal static void Set(JsonObject root, string key, object? value)
    {
        string[] parts = key.Split(':');
        JsonObject node = root;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (node[parts[i]] is JsonObject child)
            {
                node = child;
            }
            else
            {
                child = new JsonObject();
                node[parts[i]] = child;
                node = child;
            }
        }

        node[parts[^1]] = value switch
        {
            null => null,
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            string s => JsonValue.Create(s),
            _ => JsonValue.Create(value.ToString()),
        };
    }
}
