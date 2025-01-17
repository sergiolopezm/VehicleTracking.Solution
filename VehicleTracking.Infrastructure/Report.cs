using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace VehicleTracking.Infrastructure;

[Table("Report")]
public partial class Report
{
    [Key]
    public int Id { get; set; }

    public int VehicleId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Patent { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime GpsDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime ArriveDate { get; set; }

    public int Course { get; set; }

    public int Speed { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string Event { get; set; } = null!;

    [StringLength(128)]
    [Unicode(false)]
    public string Description { get; set; } = null!;

    public Geometry Location { get; set; } = null!;
}
