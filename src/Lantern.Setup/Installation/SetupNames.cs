// Names Setup shares with Lantern 1.x's Inno Setup installer, the app and Windows. Changing one breaks updates.

namespace Lantern.Setup.Installation;

public static class SetupNames
{
    /// <summary>
    /// Inno Setup's AppId of Lantern (installer/Lantern.iss, 1.x). Apps &amp; Features lists Lantern under the uninstall key
    /// "{id}_is1"; Setup keeps that key, so an update from 1.x leaves one entry.
    /// </summary>
    public const string AppGuid = "891211E8-307E-4D05-BF33-98BDC7D7383C";

    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{" + AppGuid + "}_is1";

    /// <summary>Lantern 1.1 and earlier: Velopack installed it per user and registered it here (HKCU).</summary>
    public const string VelopackUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lantern";

    public const string MachineEnvironmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    public const string UserEnvironmentKey = "Environment";

    /// <summary>Lantern's startup entry is the value <see cref="AppName"/> here (HKCU; WindowsPlatformServices writes it).</summary>
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string AppName = "Lantern";
    public const string Publisher = "Lantern contributors";
    public const string AppExe = "Lantern.exe";

    /// <summary>The uninstaller next to Lantern.exe (UpdateService.IsInstalled looks for it).</summary>
    public const string UninstallerExe = "uninstall.exe";

    /// <summary>The list of installed files next to Lantern.exe: what the uninstaller removes and an update replaces.</summary>
    public const string InstallRecordFile = "uninstall.json";

    /// <summary>The "lantern" command (cli\lantern.cmd), the folder Setup puts on PATH.</summary>
    public const string CliFolder = "cli";

    /// <summary>Inno Setup's uninstaller files, removed when Setup takes over a 1.x installation.</summary>
    public static readonly IReadOnlyList<string> InnoUninstallerFiles = ["unins000.exe", "unins000.dat", "unins000.msg"];

    /// <summary>Start menu and desktop shortcut name.</summary>
    public const string ShortcutName = "Lantern.lnk";

    /// <summary>Registry values Setup adds to the uninstall key (Inno Setup kept the same in "Inno Setup: …").</summary>
    public const string TasksValue = "Lantern Setup: Tasks";
    public const string LanguageValue = "Lantern Setup: Language";
    public const string ScopeValue = "Lantern Setup: Scope";
    public const string InnoTasksValue = "Inno Setup: Selected Tasks";
    public const string InnoAppPathValue = "Inno Setup: App Path";
    public const string InnoVersionValue = "Inno Setup: Setup Version";
    public const string InnoLanguageValue = "Inno Setup: Language";

    /// <summary>Windows 10 1809: the oldest Windows Lantern runs on (Inno Setup's MinVersion).</summary>
    public const int MinimumBuild = 17763;

    /// <summary>Where Microsoft offers the Visual C++ Redistributable (x64).</summary>
    public const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
}
