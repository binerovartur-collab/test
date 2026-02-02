using System.Runtime.InteropServices;

namespace LazerNi.Interop
{
    /// <summary>
    /// Helper class for DLL path management
    /// CRITICAL: Separate from EzCadInterop to allow SetDllDirectory to execute BEFORE MarkEzd.dll loading
    /// When CLR loads a class with [DllImport], it tries to resolve ALL imported DLLs immediately,
    /// so SetDllDirectory must be in a separate class to set the path before MarkEzd.dll is loaded.
    /// </summary>
    public static class DllHelper
    {
        /// <summary>
        /// Set DLL search directory for Windows to find SDK DLLs
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDllDirectory(string lpPathName);
    }
}
