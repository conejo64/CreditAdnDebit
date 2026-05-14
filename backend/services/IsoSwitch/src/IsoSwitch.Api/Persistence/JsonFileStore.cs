using System.Text.Json;

namespace IsoSwitch.Api.Persistence;

public sealed class JsonFileStore
{
    private readonly string _baseDir;
    private static readonly JsonSerializerOptions Opt = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public JsonFileStore(IConfiguration cfg)
    {
        _baseDir = cfg["Persistence:BaseDir"] ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(_baseDir);
    }

    public async Task SaveAsync<T>(string fileName, T value, CancellationToken ct)
    {
        var path = Path.Combine(_baseDir, fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Opt), ct);
    }

    public async Task<T?> LoadAsync<T>(string fileName, CancellationToken ct)
    {
        var path = Path.Combine(_baseDir, fileName);
        if (!File.Exists(path)) return default;
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<T>(json, Opt);
    }

    public async Task AppendJsonLineAsync(string fileName, object obj, CancellationToken ct)
    {
        var path = Path.Combine(_baseDir, fileName);
        var line = JsonSerializer.Serialize(obj, Opt);
        await File.AppendAllTextAsync(path, line + Environment.NewLine, ct);
    }
}
