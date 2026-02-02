using System;
using System.Text.Json.Serialization;

namespace LazerNi.Models
{
    /// <summary>
    /// Represents a product that can be marked (15 fields according to requirements)
    /// Имена свойств совпадают с JSON от сервера (camelCase: name, gtin)
    /// </summary>
    public class Product
    {
        public int Id { get; set; }

        // Сервер отдаёт "name", "gtin" в JSON (camelCase от ProductDto)
        public string Name { get; set; } = string.Empty;
        public string Gtin { get; set; } = string.Empty;
        public string PackageGtin { get; set; } = string.Empty;
        public int ItemsPerPackage { get; set; } = 12;
        public int PackagesPerPallet { get; set; } = 6;

        // Дополнительные поля
        public string? DocumentType { get; set; }
        public string? DocumentNumber { get; set; }
        public DateTime? DocumentDate { get; set; }
        public string? WellNumber { get; set; }
        public string? NomenclatureType { get; set; }
        public int? ShelfLifeDays { get; set; }
        public decimal AlcoholPercentage { get; set; } = 0;
        public string? TnvedCode { get; set; }
        public string? SubsoilLicense { get; set; }
        public DateTime? LicenseDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return $"{Name} (GTIN: {Gtin}, {ItemsPerPackage} шт./упак.)";
        }
    }
}
