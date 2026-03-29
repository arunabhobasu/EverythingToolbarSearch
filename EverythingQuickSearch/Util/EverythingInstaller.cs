using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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

        /// <summary>
        /// Expected SHA-256 hash (lowercase hex) of the bundled Everything-Setup.exe.
        /// Update this constant whenever the bundled installer is replaced.
        /// Set to empty string to skip hash verification (not recommended for production).
        /// </summary>
        private const string ExpectedInstallerSha256 = "";

        /// <summary>
        /// Expected Authenticode certificate thumbprint (hex, case-insensitive) of the voidtools signing cert.
        /// Update this constant if the voidtools signing certificate is renewed.
        /// Set to empty string to skip thumbprint pinning (falls back to subject/chain verification only).
        /// </summary>
        private const string ExpectedCertThumbprint = "";

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
        /// Verifies the Authenticode digital signature of the installer before executing it.
        /// </summary>
        /// <param name="installerPath">Full path to the bundled <c>Everything-Setup.exe</c>.</param>
        /// <returns><see langword="true"/> if the installer exited with code 0.</returns>
        public static async Task<bool> InstallEverythingAsync(string installerPath)
        {
            if (!File.Exists(installerPath))
                return false;

            if (!VerifyInstallerSignature(installerPath))
                return false;

            if (!VerifyInstallerHash(installerPath))
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

        /// <summary>
        /// Verifies the Authenticode digital signature of <paramref name="filePath"/> and checks
        /// that the certificate subject contains "voidtools", the certificate is not expired,
        /// and the certificate chain is trusted. Also pins the certificate thumbprint if configured.
        /// </summary>
        /// <returns><see langword="true"/> if the signature is valid and issued to voidtools.</returns>
        private static bool VerifyInstallerSignature(string filePath)
        {
            try
            {
                var cert = X509Certificate.CreateFromSignedFile(filePath);
                var cert2 = new X509Certificate2(cert);

                // Check certificate validity period
                if (DateTime.UtcNow < cert2.NotBefore || DateTime.UtcNow > cert2.NotAfter)
                    return false;

                // Check subject contains "voidtools"
                bool subjectOk = cert2.Subject.Contains("voidtools", StringComparison.OrdinalIgnoreCase) ||
                                 cert2.GetNameInfo(X509NameType.SimpleName, false)
                                      .Contains("voidtools", StringComparison.OrdinalIgnoreCase);
                if (!subjectOk)
                    return false;

                // Pin thumbprint if configured (provides defence-in-depth against a different
                // voidtools-signed binary being substituted).
                if (!string.IsNullOrEmpty(ExpectedCertThumbprint))
                {
                    if (!cert2.Thumbprint.Equals(ExpectedCertThumbprint, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // Validate certificate chain
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                return chain.Build(cert2);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Verifies the SHA-256 hash of <paramref name="filePath"/> against
        /// <see cref="ExpectedInstallerSha256"/>. Verification is skipped when the expected hash
        /// constant is empty (e.g., during development before the real installer is bundled).
        /// </summary>
        /// <returns><see langword="true"/> if the hash matches or the expected hash is not configured.</returns>
        private static bool VerifyInstallerHash(string filePath)
        {
            if (string.IsNullOrEmpty(ExpectedInstallerSha256))
                return true; // hash pinning not configured; skip

            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hashBytes = sha256.ComputeHash(stream);
                string actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                return string.Equals(actualHash, ExpectedInstallerSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
