using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace VehicleTracking.Infrastructure;

[Table("Tracking")]
public partial class Tracking
{
    [Key]
    public int Id { get; set; }

    public int Process { get; set; }

    public int State { get; set; }

    public int? Novelty { get; set; }

    public int UserId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Created { get; set; }

    public int ManifestId { get; set; }

    public int? OrderId { get; set; }

    public int? DeliveryId { get; set; }

    public Geometry? Location { get; set; }

    [StringLength(1024)]
    [Unicode(false)]
    public string Description { get; set; } = null!;

    [StringLength(512)]
    [Unicode(false)]
    public string Internal { get; set; } = null!;
}
