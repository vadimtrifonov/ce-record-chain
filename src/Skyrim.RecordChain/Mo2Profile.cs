using Mutagen.Bethesda.Plugins;

namespace Skyrim.RecordChain;

internal sealed class Mo2Profile
{
    private readonly string _gameDataFolder;
    private readonly IReadOnlyList<string> _enabledModFolders;
    private readonly string _overwriteFolder;

    private Mo2Profile(
        GameKind game,
        string gameDataFolder,
        IReadOnlyList<string> enabledModFolders,
        string overwriteFolder,
        string pluginsPath,
        string creationClubPath)
    {
        Game = game;
        _gameDataFolder = gameDataFolder;
        _enabledModFolders = enabledModFolders;
        _overwriteFolder = overwriteFolder;
        ActivePlugins = PluginList.ReadActive(this, pluginsPath, creationClubPath);
    }

    internal GameKind Game { get; }
    internal IReadOnlyList<ActivePlugin> ActivePlugins { get; }

    internal static Mo2Profile Load(GameKind game, string instanceRoot, string profileName)
    {
        var organizerIniPath = Path.Combine(instanceRoot, "ModOrganizer.ini");
        var organizerIni = IniFile.Read(organizerIniPath);

        ValidateConfiguredGame(
            game,
            QSettingsValue.DecodeString(
                organizerIni.Get("General", "gameName"),
                $"[General] gameName in {organizerIniPath}"),
            organizerIniPath);

        var gamePathValue = QSettingsValue.DecodeString(
            organizerIni.Get("General", "gamePath"),
            $"[General] gamePath in {organizerIniPath}");
        if (string.IsNullOrWhiteSpace(gamePathValue))
        {
            throw new InvalidOperationException($"{organizerIniPath} has no [General] gamePath value.");
        }

        var gameRoot = ResolvePath(gamePathValue, instanceRoot, instanceRoot, "gamePath");
        var gameDataFolder = Path.Combine(gameRoot, "Data");
        RequireDirectory(gameDataFolder, "Physical game Data directory");

        var baseValue = QSettingsValue.DecodeString(
            organizerIni.Get("Settings", "base_directory"),
            $"[Settings] base_directory in {organizerIniPath}");
        var baseDirectory = string.IsNullOrWhiteSpace(baseValue)
            ? instanceRoot
            : ResolvePath(baseValue, instanceRoot, instanceRoot, "base_directory");

        var modsFolder = ResolveConfiguredDirectory(
            organizerIni,
            organizerIniPath,
            "mod_directory",
            "mods",
            instanceRoot,
            baseDirectory);
        var profilesFolder = ResolveConfiguredDirectory(
            organizerIni,
            organizerIniPath,
            "profiles_directory",
            "profiles",
            instanceRoot,
            baseDirectory);
        var overwriteFolder = ResolveConfiguredDirectory(
            organizerIni,
            organizerIniPath,
            "overwrite_directory",
            "overwrite",
            instanceRoot,
            baseDirectory);

        RequireDirectory(modsFolder, "MO2 mods directory");
        RequireDirectory(profilesFolder, "MO2 profiles directory");

        var profileFolder = Path.GetFullPath(Path.Combine(profilesFolder, profileName));
        RequireDirectory(profileFolder, $"MO2 profile '{profileName}'");
        var modlistPath = RequireFile(profileFolder, "modlist.txt");
        var pluginsPath = RequireFile(profileFolder, "plugins.txt");

        return new Mo2Profile(
            game,
            gameDataFolder,
            Mo2ModList.ReadEnabled(modlistPath, modsFolder),
            overwriteFolder,
            pluginsPath,
            Path.Combine(gameRoot, "Skyrim.ccc"));
    }

    internal string? TryResolvePluginPath(ModKey modKey)
    {
        var fileName = modKey.FileName;

        var path = ExistingFile(_overwriteFolder, fileName);
        if (path is not null)
        {
            return path;
        }

        foreach (var modFolder in _enabledModFolders)
        {
            path = ExistingFile(modFolder, fileName);
            if (path is not null)
            {
                return path;
            }
        }

        return ExistingFile(_gameDataFolder, fileName);
    }

    private static string? ExistingFile(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        return File.Exists(path) ? NormalizePath(path) : null;
    }

    private static string ResolveConfiguredDirectory(
        IniFile ini,
        string iniPath,
        string key,
        string defaultName,
        string instanceRoot,
        string baseDirectory)
    {
        var value = QSettingsValue.DecodeString(
            ini.Get("Settings", key),
            $"[Settings] {key} in {iniPath}");
        value = string.IsNullOrWhiteSpace(value) ? $"%BASE_DIR%/{defaultName}" : value;
        return ResolvePath(value, instanceRoot, baseDirectory, key);
    }

    private static string ResolvePath(
        string value,
        string relativeRoot,
        string baseDirectory,
        string setting)
    {
        value = ReplaceOrdinalIgnoreCase(value, "%BASE_DIR%", baseDirectory)
            .Replace('/', Path.DirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(Path.IsPathFullyQualified(value)
                ? value
                : Path.Combine(relativeRoot, value));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException($"Invalid MO2 path setting {setting}: {value}", exception);
        }
    }

    private static string ReplaceOrdinalIgnoreCase(string value, string oldValue, string newValue)
    {
        var index = value.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            value = string.Concat(value.AsSpan(0, index), newValue, value.AsSpan(index + oldValue.Length));
            index = value.IndexOf(oldValue, index + newValue.Length, StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }

    private static void ValidateConfiguredGame(GameKind game, string? configuredName, string iniPath)
    {
        if (string.IsNullOrWhiteSpace(configuredName))
        {
            throw new InvalidOperationException($"{iniPath} has no [General] gameName value.");
        }

        var expectedName = game switch
        {
            GameKind.SkyrimSE => "Skyrim Special Edition",
            GameKind.SkyrimVR => "Skyrim VR",
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null)
        };
        if (!configuredName.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Requested game {game} requires {iniPath} gameName '{expectedName}', but found '{configuredName}'.");
        }
    }

    private static void RequireDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"{description} does not exist: {path}");
        }
    }

    private static string RequireFile(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"MO2 profile file does not exist: {path}");
        }

        return path;
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/');
}

internal sealed record ActivePlugin(
    ModKey ModKey,
    int LoadOrderIndex,
    string PhysicalPath);
