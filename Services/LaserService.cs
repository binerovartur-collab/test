using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using LazerNi.Interop;

namespace LazerNi.Services
{
    public class LaserService
    {
        private readonly LoggingService _logger;
        private readonly object _lockObject = new();
        private bool _isInitialized;
        private bool _initializationAttempted;
        private double _laserCenterX;
        private double _laserCenterY;

        public bool IsInitialized => _isInitialized;

        public LaserService(LoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Initialize the EzCad SDK
        /// </summary>
        /// <param name="sdkPath">Path to SDK directory (where EzCad2.exe and MarkEzd.dll are located)</param>
        /// <param name="testMode">True for test mode (no actual laser), False for production</param>
        /// <param name="laserCenterX">Center X coordinate of laser work area (mm)</param>
        /// <param name="laserCenterY">Center Y coordinate of laser work area (mm)</param>
        public void Initialize(string sdkPath, bool testMode, double laserCenterX = 50.0, double laserCenterY = 50.0)
        {
            lock (_lockObject)
            {
                // If already initialized, just return success
                if (_isInitialized)
                {
                    _logger.Warning("SDK is already initialized");
                    return;
                }

                // CRITICAL: If previous initialization attempt failed, call Close() to clean up
                // This fixes the "EZCAD is already running" error on retry
                if (_initializationAttempted)
                {
                    _logger.Info("Previous initialization attempt detected, calling Close() to clean up...");
                    try
                    {
                        EzCadInterop.Close();
                        _logger.Info("SDK Close() called successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Close() call failed (this may be normal): {ex.Message}");
                    }
                }
                _initializationAttempted = true;

                _laserCenterX = laserCenterX;
                _laserCenterY = laserCenterY;

                // Convert relative path to absolute path (relative to exe location)
                string absoluteSdkPath = sdkPath;
                if (!Path.IsPathRooted(sdkPath))
                {
                    // Use AppContext.BaseDirectory for single-file publish compatibility
                    string exeDirectory = AppContext.BaseDirectory;
                    absoluteSdkPath = Path.GetFullPath(Path.Combine(exeDirectory, sdkPath));
                }

                // If user specified path to EzCad2.exe or any .exe/.dll file, extract directory
                if (File.Exists(absoluteSdkPath) && (absoluteSdkPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || absoluteSdkPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    absoluteSdkPath = Path.GetDirectoryName(absoluteSdkPath) ?? absoluteSdkPath;
                }

                _logger.Info($"Initializing EzCad SDK...");
                _logger.Info($"SDK Path (original): {sdkPath}");
                _logger.Info($"SDK Path (absolute): {absoluteSdkPath}");
                _logger.Info($"Test Mode: {testMode}");
                _logger.Info($"Laser Center: ({laserCenterX}, {laserCenterY})mm");

                // Verify SDK path exists
                if (!Directory.Exists(absoluteSdkPath))
                {
                    throw new DirectoryNotFoundException($"SDK directory not found: {absoluteSdkPath}");
                }

                // Find MarkEzd.dll - search in root and common subdirectories
                string? markEzdPath = FindMarkEzdDll(absoluteSdkPath);
                if (string.IsNullOrEmpty(markEzdPath))
                {
                    throw new FileNotFoundException(
                        $"MarkEzd.dll not found in SDK directory or subdirectories.\n" +
                        $"Searched in: {absoluteSdkPath}\n" +
                        $"Please ensure the SDK path points to a folder containing MarkEzd.dll, EzCad2.exe, and EZCAD.CFG");
                }

                // Use the directory where MarkEzd.dll was found
                absoluteSdkPath = Path.GetDirectoryName(markEzdPath) ?? absoluteSdkPath;
                _logger.Info($"Found MarkEzd.dll at: {markEzdPath}");
                _logger.Info($"Using SDK directory: {absoluteSdkPath}");

                // Verify other critical files exist in the same directory
                string ezcadCfgPath = Path.Combine(absoluteSdkPath, "EZCAD.CFG");
                if (!File.Exists(ezcadCfgPath))
                {
                    _logger.Warning($"EZCAD.CFG not found at: {ezcadCfgPath}");
                    _logger.Warning("SDK may fail to initialize without this file");
                }

                // CRITICAL: Tell Windows to search for DLLs in SDK directory
                // MUST use DllHelper (separate class) to avoid CLR loading MarkEzd.dll before SetDllDirectory executes
                _logger.Info($"Setting DLL search path to: {absoluteSdkPath}");
                if (!DllHelper.SetDllDirectory(absoluteSdkPath))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"Failed to set DLL directory. Win32 Error: {error}");
                }

                // Pass IntPtr.Zero as the owner window handle (HWND)
                // The SDK documentation says: "If there is no window, set this value to zero"
                int result = EzCadInterop.Initialize(absoluteSdkPath, testMode, IntPtr.Zero);

                if (result != 0)
                {
                    string error = EzCadInterop.GetErrorText(result);
                    _logger.Error($"SDK initialization failed (code {result}): {error}");

                    // Provide specific guidance based on error code
                    string guidance = GetInitializationErrorGuidance(result, absoluteSdkPath);
                    _logger.Error(guidance);

                    throw new Exception($"SDK initialization failed: {error}\n\n{guidance}");
                }

                _isInitialized = true;
                _logger.Info("✓ SDK initialized successfully");
            }
        }

        /// <summary>
        /// Find MarkEzd.dll in the given path or its subdirectories
        /// </summary>
        private string? FindMarkEzdDll(string basePath)
        {
            // First check in the root path
            string directPath = Path.Combine(basePath, "MarkEzd.dll");
            if (File.Exists(directPath))
            {
                return directPath;
            }

            // Check common subdirectories
            string[] commonSubdirs = { "Debug", "Release", "bin", "x86", "x64" };
            foreach (var subdir in commonSubdirs)
            {
                string subdirPath = Path.Combine(basePath, subdir, "MarkEzd.dll");
                if (File.Exists(subdirPath))
                {
                    return subdirPath;
                }
            }

            // Search recursively (limited depth)
            try
            {
                var found = Directory.GetFiles(basePath, "MarkEzd.dll", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    // Prefer paths with EZCAD.CFG in the same directory
                    foreach (var path in found)
                    {
                        string? dir = Path.GetDirectoryName(path);
                        if (dir != null && File.Exists(Path.Combine(dir, "EZCAD.CFG")))
                        {
                            return path;
                        }
                    }
                    return found[0];
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error searching for MarkEzd.dll: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Get helpful guidance message based on initialization error code
        /// </summary>
        private string GetInitializationErrorGuidance(int errorCode, string sdkPath)
        {
            return errorCode switch
            {
                1 => // LMC1_ERR_EZCADRUN - EZCAD is running
                    "EZCAD is reported as already running.\n" +
                    "Check that:\n" +
                    "1. EzCad2.exe is not running (check Task Manager)\n" +
                    "2. No other application is using the laser SDK\n" +
                    "3. Try restarting this application\n" +
                    "4. If problem persists, restart the computer",

                2 => // LMC1_ERR_NOFINDCFGFILE - No EZCAD.CFG
                    $"EZCAD.CFG configuration file not found.\n" +
                    $"Ensure EZCAD.CFG exists in: {sdkPath}",

                3 => // LMC1_ERR_FAILEDOPEN - Open LMC board failed
                    "Failed to open LMC control board.\n" +
                    "Check that:\n" +
                    "1. The laser controller is connected via USB\n" +
                    "2. USB drivers are installed correctly\n" +
                    "3. The device is recognized in Device Manager\n" +
                    "4. Test mode is enabled if no hardware is connected",

                4 => // LMC1_ERR_NODEVICE - No LMC device
                    "No LMC control board found.\n" +
                    "If you're testing without hardware, enable 'Test Mode' in settings.\n" +
                    "Otherwise, check USB connection and drivers.",

                5 => // LMC1_ERR_HARDVER - LMC version error
                    "LMC driver version mismatch!\n\n" +
                    "The SDK files are not compatible with the LMC board driver on this computer.\n\n" +
                    "Solutions:\n" +
                    "1. Use SDK files from YOUR working EzCad installation\n" +
                    "   (Point SDK path to where your EzCad2.exe is installed)\n" +
                    "2. Or reinstall LMC drivers from the SDK folder\n" +
                    $"   Check: {sdkPath}\\*.rar for driver archives\n" +
                    "3. Contact your laser equipment vendor for compatible SDK",

                6 => // LMC1_ERR_DEVCFG - No MarkCfg in Plug
                    $"Configuration files not found.\n" +
                    $"Ensure the 'plug' folder with markcfg files exists in: {sdkPath}",

                _ => $"Error code {errorCode}: {EzCadInterop.GetErrorText(errorCode)}"
            };
        }

        /// <summary>
        /// Close and release SDK resources
        /// </summary>
        public void Shutdown()
        {
            lock (_lockObject)
            {
                if (!_isInitialized)
                {
                    return;
                }

                _logger.Info("Shutting down SDK...");

                int result = EzCadInterop.Close();

                if (result != 0)
                {
                    string error = EzCadInterop.GetErrorText(result);
                    _logger.Warning($"SDK close returned error: {error}");
                }
                else
                {
                    _logger.Info("✓ SDK closed successfully");
                }

                _isInitialized = false;
            }
        }
    }
}
