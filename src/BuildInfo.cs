using System.IO;

namespace WearableDistributor;

internal static class BuildInfo
{
    public const string Label = "v0.4.12 · LOCAL LANGUAGE PREVIEW";
    public static string SettingsRoot => Environment.GetEnvironmentVariable("WEARABLE_DISTRIBUTOR_SETTINGS_DIR") is { Length: > 0 } isolated
        ? Path.GetFullPath(isolated) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WearableDistributor");
    public static void CheckOutput(string root)
    {
        if (File.Exists(Path.Combine(root, "SkyrimSPIDPresetTest.esp")) || File.Exists(Path.Combine(root, "SkyrimSPIDPresetTest.patch.json")))
            throw new InvalidDataException("This folder contains an experimental test patch. Choose your released WearableDistributor output folder or a new folder. Test output is not merged into released output.");
    }
}
