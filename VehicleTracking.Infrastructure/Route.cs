using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace VehicleTracking.Infrastructure;

[Table("Route")]
public partial class Route
{
    [Key]
    public int Id { get; set; }

    public DateOnly Created { get; set; }

    public DateOnly? Ended { get; set; }

    [StringLength(150)]
    [Unicode(false)]
    public string Origin { get; set; } = null!;

    [Column("OLatitude")]
    public double Olatitude { get; set; }

    [Column("OLongitude")]
    public double Olongitude { get; set; }

    [StringLength(150)]
    [Unicode(false)]
    public string Destiny { get; set; } = null!;

    [Column("DLatitude")]
    public double Dlatitude { get; set; }

    [Column("DLongitude")]
    public double Dlongitude { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string Variant { get; set; } = null!;

    public Geometry Path { get; set; } = null!;

    public int Distance { get; set; }

    public int TripTime { get; set; }
}
