using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace VehicleTracking.Infrastructure;

[Table("City")]
public partial class City
{
    [Key]
    public int Id { get; set; }

    public DateOnly Created { get; set; }

    [StringLength(2)]
    [Unicode(false)]
    public string CodDepto { get; set; } = null!;

    [StringLength(5)]
    [Unicode(false)]
    public string CodCountry { get; set; } = null!;

    [StringLength(8)]
    [Unicode(false)]
    public string CodCity { get; set; } = null!;

    [StringLength(150)]
    [Unicode(false)]
    public string Depto { get; set; } = null!;

    [StringLength(150)]
    [Unicode(false)]
    public string Country { get; set; } = null!;

    [Column("City")]
    [StringLength(150)]
    [Unicode(false)]
    public string City1 { get; set; } = null!;

    public Geometry? Location { get; set; }
}
