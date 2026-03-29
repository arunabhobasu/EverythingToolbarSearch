using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace EverythingQuickSearch.Util
{
    /// <summary>
    /// Provides helpers to detect, install, and start the Everything search service.
    /// </summary>
    public static class EverythingInstaller
    {
        private const int IpcPollMaxAttempts = 10;
        private const int IpcPollIntervalMs = 1000;
        private static readonly string[] _registryPaths =
        [
            @"HKEY_LOCAL_MACHINE\SOFTWARE\voidtools\Everything",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\voidtools\Everything",
        ];

        private static readonly string[] _commonInstallPaths =
        [
            @"C:\Program Files\Everything\Everything.exe",
            @"C:\Program Files (x86)\Everything\Everything.exe",
        ];

        /// <summary>Returns <see langword="true"/> if an Everything process is currently running.</summary>
        public static bool IsEverythingRunning()
        {
            return Process.GetProcessesByName("Everything").Length > 0;
        }

        /// <summary>
        /// Returns <see langword="true"/> if Everything is installed on this machine,
        /// determined by checking the registry and common installation paths.
        /// </summary>
        public static bool IsEverythingInstalled()
        {
            foreach (string regPath in _registryPaths)
            {
                string? installDir = Registry.GetValue(regPath, "Install Dir", null) as string;
                if (installDir != null && File.Exists(Path.Combine(installDir, "Everything.exe")))
                    return true;
            }

            return _commonInstallPaths.Any(File.Exists);
        }

        /// <summary>
        /// Runs the bundled Everything setup silently with UAC elevation and waits for it to complete.
        /// </summary>
        /// <param name="installerPath">Full path to the bundled <c>Everything-Setup.exe</c>.</param>
        /// <returns><see langword="true"/> if the installer exited with code 0.</returns>
        public static async Task<bool> InstallEverythingAsync(string installerPath)
        {
            if (!File.Exists(installerPath))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/S",
                UseShellExecute = true,
                Verb = "runas",
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null)
                    return false;
                await Task.Run(() => process.WaitForExit());
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Locates the installed Everything.exe and launches it with <c>-startup</c>,
        /// then polls until the Everything IPC becomes available (up to ~10 seconds).
        /// </summary>
        /// <returns><see langword="true"/> if Everything is running after this call.</returns>
        public static async Task<bool> StartEverythingServiceAsync()
        {
            string? everythingExe = FindEverythingExe();
            if (everythingExe == null)
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = everythingExe,
                Arguments = "-startup",
                UseShellExecute = true,
            };

            try
            {
                Process.Start(psi);
            }
            catch
            {
                return false;
            }

            for (int i = 0; i < IpcPollMaxAttempts; i++)
            {
                await Task.Delay(IpcPollIntervalMs);
                if (IsEverythingRunning())
                    return true;
            }

            return IsEverythingRunning();
        }

        /// <summary>
        /// Ensures Everything is installed and running.
        /// If Everything is not installed, runs the bundled installer (with UAC prompt).
        /// If Everything is installed but not running, starts it automatically.
        /// </summary>
        /// <param name="installerPath">Full path to the bundled <c>Everything-Setup.exe</c>.</param>
        /// <returns><see langword="true"/> if Everything is ready to accept queries.</returns>
        public static async Task<bool> EnsureEverythingReadyAsync(string installerPath)
        {
            if (IsEverythingRunning())
                return true;

            if (!IsEverythingInstalled())
            {
                bool installed = await InstallEverythingAsync(installerPath);
                if (!installed)
                    return false;
            }

            return await StartEverythingServiceAsync();
        }

        /// <summary>Returns the full path to Everything.exe, or <see langword="null"/> if not found.</summary>
        public static string? FindEverythingExe()
        {
            foreach (string regPath in _registryPaths)
            {
                string? installDir = Registry.GetValue(regPath, "Install Dir", null) as string;
                if (installDir != null)
                {
                    string exe = Path.Combine(installDir, "Everything.exe");
                    if (File.Exists(exe))
                        return exe;
                }
            }

            return _commonInstallPaths.FirstOrDefault(File.Exists);
        }
    }
}
