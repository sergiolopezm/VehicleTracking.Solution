using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace VehicleTracking.Infrastructure;

[Table("Position")]
public partial class Position
{
    [Key]
    public int VehicleId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime GpsDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime ArriveDate { get; set; }

    public int Course { get; set; }

    public int Speed { get; set; }

    [StringLength(264)]
    [Unicode(false)]
    public string Event { get; set; } = null!;

    [StringLength(1024)]
    [Unicode(false)]
    public string Description { get; set; } = null!;

    public Geometry Location { get; set; } = null!;
}
