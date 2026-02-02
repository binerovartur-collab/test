using System;

namespace LazerNi.Models
{
    /// <summary>
    /// Represents coordinate data received from the VisionMaster camera
    /// Format: X;Y;Angle;#CR#LF
    /// </summary>
    public class CameraData
    {
        /// <summary>
        /// X coordinate in millimeters (after VisionMaster transformation)
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y coordinate in millimeters (after VisionMaster transformation)
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// Rotation angle in degrees (-180 to +180)
        /// </summary>
        public double AngleDegrees { get; set; }

        /// <summary>
        /// Rotation angle converted to radians (for SDK)
        /// </summary>
        public double AngleRadians => AngleDegrees * Math.PI / 180.0;

        /// <summary>
        /// Timestamp when data was received
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Raw data string received from camera
        /// </summary>
        public string? RawData { get; set; }

        public override string ToString()
        {
            return $"X={X:F2}mm, Y={Y:F2}mm, Angle={AngleDegrees:F2}° ({AngleRadians:F4} rad) at {Timestamp:HH:mm:ss.fff}";
        }
    }
}
