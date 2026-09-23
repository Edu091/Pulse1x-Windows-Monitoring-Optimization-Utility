using System.IO;
using System.Text.Json;
using Pulse1x.App.Services;

namespace Pulse1x.Hub.Tests;

internal static class PersistenceTests
{
    public static void Register(TestSuite suite)
    {
        suite.Run("Atomic file replaces content without leaving temporary files", () =>
        {
            string directory = NewTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "state.json");
                AtomicFile.WriteAllText(path, "first");
                AtomicFile.WriteAllText(path, "second");

                TestSuite.Equal("second", File.ReadAllText(path));
                TestSuite.Equal(1, Directory.GetFiles(directory).Length);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });

        suite.Run("Settings loader repairs null and out-of-range persisted values", () =>
        {
            string directory = NewTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "settings.json");
                File.WriteAllText(path, """
                    {
                      "UpdateIntervalMs": -5,
                      "Language": null,
                      "DashboardSectionOrder": null,
                      "DashboardSectionVisibility": null,
                      "Appearance": null,
                      "GameHub": null,
                      "OemCommands": null
                    }
                    """);

                var settings = new SettingsService(path).Current;
                TestSuite.Equal(250, settings.UpdateIntervalMs);
                TestSuite.Equal("pt", settings.Language);
                TestSuite.Equal(0, settings.DashboardSectionOrder.Count);
                TestSuite.Equal(0, settings.DashboardSectionVisibility.Count);
                TestSuite.Equal(false, settings.Appearance is null);
                TestSuite.Equal(false, settings.GameHub is null);
                TestSuite.Equal(false, settings.OemCommands is null);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });

        suite.Run("Settings loader clamps nested visual and GameHub values", () =>
        {
            string directory = NewTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "settings.json");
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    UpdateIntervalMs = 50_000,
                    Language = "invalid",
                    Appearance = new { Transparency = 4, BackgroundBlur = -10, BackgroundSaturation = 7 },
                    GameHub = new { SoundVolume = 2.5, ControllerGlyphs = "invalid", CardSize = 99 },
                }));

                var settings = new SettingsService(path).Current;
                TestSuite.Equal(10_000, settings.UpdateIntervalMs);
                TestSuite.Equal("pt", settings.Language);
                TestSuite.Equal(1d, settings.Appearance.Transparency);
                TestSuite.Equal(0d, settings.Appearance.BackgroundBlur);
                TestSuite.Equal(2d, settings.Appearance.BackgroundSaturation);
                TestSuite.Equal(1d, settings.GameHub.SoundVolume);
                TestSuite.Equal("Auto", settings.GameHub.ControllerGlyphs);
                TestSuite.Equal(Pulse1x.App.Models.GameHub.CardSize.Medium, settings.GameHub.CardSize);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "Pulse1x-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
