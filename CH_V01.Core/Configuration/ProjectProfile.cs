using System.Text.Json;

namespace CH_V01.Core.Configuration;

public sealed class ProjectProfile
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string ProtocolAdapterId { get; set; } = "Default.Ict";

    public string Notes { get; set; } = string.Empty;

    public AppConfiguration Configuration { get; set; } = new();
}

public sealed class ProjectProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ProjectProfileStore(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public IReadOnlyList<ProjectProfile> LoadAll()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return Array.Empty<ProjectProfile>();
        }

        var profiles = new List<ProjectProfile>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var profile = Load(file);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles
            .OrderBy(profile => profile.CustomerName)
            .ThenBy(profile => profile.ProductName)
            .ThenBy(profile => profile.Name)
            .ToList();
    }

    public ProjectProfile? Load(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ProjectProfile>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(ProjectProfile profile)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var fileName = MakeFileName(string.IsNullOrWhiteSpace(profile.Id) ? profile.Name : profile.Id);
        var filePath = Path.Combine(Directory, fileName + ".json");
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    private static string MakeFileName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "profile" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }
}

