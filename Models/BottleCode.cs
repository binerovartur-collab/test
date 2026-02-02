using System;

namespace LazerNi.Models
{
    /// <summary>
    /// Represents a bottle code in the database
    /// </summary>
    public class BottleCode
    {
        public long Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = "TRANSFERRED"; // TRANSFERRED, ENGRAVED, DELETED
        public DateTime? EngravedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return $"[{Status}] {Code} (ID: {Id})";
        }
    }
}
