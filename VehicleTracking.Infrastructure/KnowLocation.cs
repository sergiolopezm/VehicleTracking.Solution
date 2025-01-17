using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace VehicleTracking.Infrastructure;

[Table("KnowLocation")]
public partial class KnowLocation
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Created { get; set; }

    public int KindOf { get; set; }

    public bool Active { get; set; }

    public Geometry Location { get; set; } = null!;

    [StringLength(10)]
    [Unicode(false)]
    public string LocationType { get; set; } = null!;

    [StringLength(150)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [StringLength(300)]
    [Unicode(false)]
    public string Description { get; set; } = null!;
}
