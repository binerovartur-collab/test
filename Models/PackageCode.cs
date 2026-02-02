using System;

namespace LazerNi.Models
{
    /// <summary>
    /// Represents a package code in the database (for future use)
    /// </summary>
    public class PackageCode
    {
        public long Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = "TRANSFERRED"; // TRANSFERRED, USED
        public DateTime? UsedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return $"[{Status}] {Code} (ID: {Id})";
        }
    }
}
