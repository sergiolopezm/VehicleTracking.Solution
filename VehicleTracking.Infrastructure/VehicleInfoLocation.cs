using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace VehicleTracking.Infrastructure;

[Table("VehicleInfoLocation")]
public partial class VehicleInfoLocation
{
    [Key]
    public long Id { get; set; }

    public int VehicleId { get; set; }

    public int ManifestId { get; set; }

    [Column(TypeName = "decimal(10, 8)")]
    public decimal Latitude { get; set; }

    [Column(TypeName = "decimal(11, 8)")]
    public decimal Longitude { get; set; }

    [Column(TypeName = "decimal(10, 2)")]
    public decimal? Speed { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Timestamp { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string Provider { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime Created { get; set; }

    public bool IsActive { get; set; }

    public Geometry? Location { get; set; }

    [StringLength(100)]
    public string? Driver { get; set; }

    [StringLength(1000)]
    public string? Georeference { get; set; }

    [StringLength(100)]
    public string? InZone { get; set; }

    [StringLength(50)]
    public string? DetentionTime { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? DistanceTraveled { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? Temperature { get; set; }

    [StringLength(2000)]
    public string? Reason { get; set; }

    public short? Angle { get; set; }

    [ForeignKey("ManifestId")]
    [InverseProperty("VehicleInfoLocations")]
    public virtual Manifest Manifest { get; set; } = null!;

    [ForeignKey("VehicleId")]
    [InverseProperty("VehicleInfoLocations")]
    public virtual Vehicle Vehicle { get; set; } = null!;
}
