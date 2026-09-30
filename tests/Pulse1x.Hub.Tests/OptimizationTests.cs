using System.IO;
using Pulse1x.App.Services;

namespace Pulse1x.Hub.Tests;

/// <summary>
/// Otimizações de 1.13.0 que não dependem do Registro real: a lista "nome=valor;" das
/// preferências do DirectX e a edição reversível dos arquivos de configuração do Discord e do
/// Spotify (aceleração de hardware por aplicativo).
/// </summary>
internal static class OptimizationTests
{
    public static void Register(TestSuite suite)
    {
        suite.Run("DirectX setting is added without touching the other entries", () =>
        {
            TestSuite.Equal("SwapEffectUpgradeEnable=1;",
                HardwareOptimizationService.WithDirectXSetting(null, "SwapEffectUpgradeEnable", "1"));
            TestSuite.Equal("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;",
                HardwareOptimizationService.WithDirectXSetting("VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;", "SwapEffectUpgradeEnable", "1"));
            TestSuite.Equal("VRROptimizeEnable=0;",
                HardwareOptimizationService.WithDirectXSetting("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", "SwapEffectUpgradeEnable", null));
        });

        suite.Run("Discord JSON setting round-trips and keeps other keys", () =>
        {
            string file = TempFile("{\"BACKGROUND_COLOR\":\"#202225\",\"enableHardwareAcceleration\":true}");
            try
            {
                string? old = AppHardwareAccelerationService.ReadJsonValue(file, "enableHardwareAcceleration");
                TestSuite.Equal("true", old);

                AppHardwareAccelerationService.WriteJsonValue(file, "enableHardwareAcceleration", "false");
                TestSuite.Equal("false", AppHardwareAccelerationService.ReadJsonValue(file, "enableHardwareAcceleration"));
                TestSuite.Equal("\"#202225\"", AppHardwareAccelerationService.ReadJsonValue(file, "BACKGROUND_COLOR"));

                AppHardwareAccelerationService.Revert(new OptimizationChange
                {
                    Kind = ChangeKind.AppSetting, KeyPath = file, ValueName = "enableHardwareAcceleration",
                    ValueKind = "Json", OldValue = old, NewValue = "false",
                });
                TestSuite.Equal("true", AppHardwareAccelerationService.ReadJsonValue(file, "enableHardwareAcceleration"));

                // Chave que não existia: desfazer remove em vez de gravar um valor inventado.
                AppHardwareAccelerationService.WriteJsonValue(file, "enableHardwareAcceleration", null);
                TestSuite.Equal<string?>(null, AppHardwareAccelerationService.ReadJsonValue(file, "enableHardwareAcceleration"));
            }
            finally { File.Delete(file); }
        });

        suite.Run("Spotify prefs line is replaced, added and removed", () =>
        {
            string file = TempFile("language=\"pt-BR\"\nui.hardware_acceleration=true\n");
            try
            {
                AppHardwareAccelerationService.WritePrefsValue(file, "ui.hardware_acceleration", "false");
                TestSuite.Equal("false", AppHardwareAccelerationService.ReadPrefsValue(file, "ui.hardware_acceleration"));
                TestSuite.Equal("\"pt-BR\"", AppHardwareAccelerationService.ReadPrefsValue(file, "language"));

                AppHardwareAccelerationService.WritePrefsValue(file, "ui.hardware_acceleration", null);
                TestSuite.Equal<string?>(null, AppHardwareAccelerationService.ReadPrefsValue(file, "ui.hardware_acceleration"));

                AppHardwareAccelerationService.WritePrefsValue(file, "ui.hardware_acceleration", "false");
                TestSuite.Equal("false", AppHardwareAccelerationService.ReadPrefsValue(file, "ui.hardware_acceleration"));
                TestSuite.Equal(2, File.ReadAllLines(file).Length);
            }
            finally { File.Delete(file); }
        });

        suite.Run("Undoing windowed-game optimization keeps later DirectX settings", () =>
        {
            const string key = @"Software\Pulse1xTests\DirectX";
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key))
                    // O usuário ligou Auto HDR DEPOIS de a otimização ser aplicada.
                    k.SetValue("DirectXUserGlobalSettings", "SwapEffectUpgradeEnable=1;AutoHDREnable=1;");

                HardwareOptimizationService.RevertDirectXItem(new OptimizationChange
                {
                    Kind = ChangeKind.Registry, Hive = "HKCU", KeyPath = key, ValueName = "DirectXUserGlobalSettings",
                    ValueKind = HardwareOptimizationService.DirectXItemKind, OldValue = null, NewValue = "1",
                });

                using var read = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)!;
                TestSuite.Equal("AutoHDREnable=1;", read.GetValue("DirectXUserGlobalSettings") as string);
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Pulse1xTests", throwOnMissingSubKey: false); }
        });

        suite.Run("Interactive user is not redirected when it is the process account", () =>
        {
            // O harness roda na própria conta de quem está logado: HKCU e AppData ficam os de sempre.
            TestSuite.Equal<string?>(null, InteractiveUser.RedirectedSid);
            TestSuite.Equal(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), InteractiveUser.RoamingAppData);
            TestSuite.Equal(Microsoft.Win32.Registry.CurrentUser.Name, InteractiveUser.Registry.Name);
        });

        suite.Run("Legacy MMCSS text values are restored as DWORD", () =>
        {
            const string games = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
            TestSuite.Equal(true, HardwareOptimizationService.IsGamesProfileDword(games, "Priority"));
            TestSuite.Equal(true, HardwareOptimizationService.IsGamesProfileDword(games, "GPU Priority"));
            TestSuite.Equal(false, HardwareOptimizationService.IsGamesProfileDword(games, "Scheduling Category"));
        });
    }

    private static string TempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"pulse1x-opt-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }
}
