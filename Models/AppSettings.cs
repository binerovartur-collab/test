using System;

namespace LazerNi.Models
{
    /// <summary>
    /// Application settings (saved to JSON file)
    /// </summary>
    public class AppSettings
    {
        // TCP Server settings
        public string TcpServerIp { get; set; } = "0.0.0.0";
        public int TcpServerPort { get; set; } = 9999;

        // SDK settings
        public string SdkPath { get; set; } = "SDK"; // Relative path to SDK folder next to exe
        public bool TestMode { get; set; } = false; // Production mode by default (real hardware)

        // Laser center coordinates (for future use with coordinate transformations)
        public double LaserCenterX { get; set; } = 50.0; // mm
        public double LaserCenterY { get; set; } = 50.0; // mm

        // Marking settings
        public string TemplatePath { get; set; } = ""; // Path to .ezd template file
        public string EntityName { get; set; } = "Code"; // Name of entity in template to rotate/move
        public bool AutoMarkOnData { get; set; } = true; // Auto-mark when camera data received

        // Rotation method selection
        // "RotateEnt" = Rotate individual entity (RotateEnt + MoveEnt)
        // "SetRotateMoveParam" = Rotate entire work area (SetRotateMoveParam)
        public string RotationMethod { get; set; } = "RotateEnt"; // Default to old method

        // Conveyor sensor mode (NEW v2.4.2)
        // true = Wait for IN8/IN9 signal (production with conveyor)
        // false = Mark immediately (testing without conveyor sensor)
        public bool UseConveyorSensor { get; set; } = false; // Default: no sensor (for testing)

        // Database settings
        public string DatabasePath { get; set; } = "codes.db"; // SQLite database file path
        public string CodeTextObjectName { get; set; } = "DM"; // Name of text object in EzCad template for code substitution

        // Code management
        public bool AutoRefillCodes { get; set; } = true; // Auto-refill from file when running low
        public int MinCodesThreshold { get; set; } = 100; // Minimum codes before auto-refill

        // EzCad control mode (NEW v2.4)
        // "SDK" = Use EzCad SDK (lmc1_* functions, EzCad closed)
        // "TCP" = Use EzCad TCP/IP communication (EzCad opened, receives codes via TCP)
        public string EzCadControlMode { get; set; } = "SDK"; // Default: SDK mode
        public int EzCadTcpPort { get; set; } = 1000; // Port for EzCad TCP server (when mode=TCP)
        public string EzCadTcpCommand { get; set; } = "TCP:Give me string"; // Expected command from EzCad

        // Server connection settings
        public bool UseServerMode { get; set; } = false; // false = local files, true = server API
        public string ServerUrl { get; set; } = "http://192.168.1.100:5001"; // LaserMarkingServer URL
        public string ServerApiKey { get; set; } = ""; // API key for authentication
        public string ClientId { get; set; } = "Line-1"; // Unique ID for this laser line
        public int HeartbeatIntervalSeconds { get; set; } = 30; // How often to send heartbeat
    }
}
