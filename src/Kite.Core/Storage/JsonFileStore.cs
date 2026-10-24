using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kite.Core.Storage;

/// <summary>the value that was loaded, plus a warning when something went wrong.</summary>
public sealed record LoadResult<T>(T Value, string? Warning);

/// <summary>
/// reads and writes one json file. a missing file gives default values.
/// a damaged file is kept as a backup and default values are used instead.
/// </summary>
public sealed class JsonFileStore<T>
    where T : class, new()
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();

    public JsonFileStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public LoadResult<T> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                return new LoadResult<T>(new T(), null);
            }

            string json;
            try
            {
                json = File.ReadAllText(FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new LoadResult<T>(new T(), $"Could not read {Path.GetFileName(FilePath)}: {ex.Message}");
            }

            try
            {
                var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (value is not null)
                {
                    return new LoadResult<T>(value, null);
                }
            }
            catch (JsonException)
            {
                // fall through and back up the bad file
            }
            catch (NotSupportedException)
            {
                // fall through and back up the bad file
            }

            var backupPath = BackupDamagedFile();
            var message = backupPath is null
                ? $"{Path.GetFileName(FilePath)} was damaged, so default values are used."
                : $"{Path.GetFileName(FilePath)} was damaged. It was saved as {Path.GetFileName(backupPath)} and default values are used.";
            return new LoadResult<T>(new T(), message);
        }
    }

    public void Save(T value)
    {
        lock (_gate)
        {
            var folder = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // write to a temp file first so a crash never leaves half a file
            var tempPath = FilePath + ".tmp";
            var json = JsonSerializer.Serialize(value, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
    }

    private string? BackupDamagedFile()
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var backupPath = Path.ChangeExtension(FilePath, $".damaged-{stamp}.json");
            File.Copy(FilePath, backupPath, overwrite: true);
            return backupPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
