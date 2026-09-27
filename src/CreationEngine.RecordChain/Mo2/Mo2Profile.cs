using Mutagen.Bethesda.Plugins;

namespace CreationEngine.RecordChain.Mo2;

internal sealed class Mo2Profile
{
    private readonly string _gameDataFolder;
    private readonly string _documentsFolder;
    private readonly IReadOnlyList<string> _enabledModFolders;
    private readonly string _overwriteFolder;

    private Mo2Profile(
        string gameRoot,
        string profileFolder,
        IReadOnlyList<string> enabledModFolders,
        string overwriteFolder,
        string pluginsPath,
        IniFile organizerSettings,
        string documentsFolder)
    {
        GameRoot = gameRoot;
        ProfileFolder = profileFolder;
        PluginsPath = pluginsPath;
        OrganizerSettings = organizerSettings;
        _gameDataFolder = Path.Combine(gameRoot, "Data");
        _documentsFolder = documentsFolder;
        _enabledModFolders = enabledModFolders;
        _overwriteFolder = overwriteFolder;
    }

    internal string GameRoot { get; }
    internal string ProfileFolder { get; }
    internal string PluginsPath { get; }
    private IniFile OrganizerSettings { get; }

    internal static Mo2Profile Load(
        string expectedGameName, string instanceRoot, string profileName, string documentsFolder)
    {
        var organizerIniPath = Path.Combine(instanceRoot, "ModOrganizer.ini");
        var organizerIni = IniFile.Read(organizerIniPath);

        ValidateConfiguredGame(
            expectedGameName,
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
            gameRoot,
            profileFolder,
            Mo2ModList.ReadEnabled(modlistPath, modsFolder),
            overwriteFolder,
            pluginsPath,
            organizerIni,
            documentsFolder);
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

    internal string GetIniFolder(string myGamesFolder)
    {
        var settingsPath = Path.Combine(ProfileFolder, "settings.ini");
        var local = File.Exists(settingsPath)
            ? IniFile.Read(settingsPath).Get("General", "LocalSettings")
            : null;
        local ??= OrganizerSettings.Get("Settings", "profile_local_inis") ?? "true";
        var useLocal = local.Trim().ToLowerInvariant() switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => throw new InvalidOperationException($"Invalid profile local-INI setting: {local}")
        };
        return useLocal ? ProfileFolder : GetMyGamesFolder(myGamesFolder);
    }

    internal string GetMyGamesFolder(string name) => Path.Combine(_documentsFolder, "My Games", name);

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

    private static void ValidateConfiguredGame(string expectedName, string? configuredName, string iniPath)
    {
        if (string.IsNullOrWhiteSpace(configuredName))
        {
            throw new InvalidOperationException($"{iniPath} has no [General] gameName value.");
        }

        if (!configuredName.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Requested game requires {iniPath} gameName '{expectedName}', but found '{configuredName}'.");
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
