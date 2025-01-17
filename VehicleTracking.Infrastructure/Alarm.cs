using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace VehicleTracking.Infrastructure;

[Table("Alarm")]
public partial class Alarm
{
    [Key]
    public int Id { get; set; }

    public int AlarmState { get; set; }

    public int AlarmType { get; set; }

    [StringLength(16)]
    [Unicode(false)]
    public string Internal { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime Created { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Updated { get; set; }

    public int ManifestId { get; set; }

    public int? OrderId { get; set; }

    public int? DeliveryId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Attended { get; set; }

    public Geometry? Location { get; set; }

    [StringLength(512)]
    [Unicode(false)]
    public string Description { get; set; } = null!;

    [StringLength(32)]
    [Unicode(false)]
    public string Command { get; set; } = null!;

    public bool Notified { get; set; }
}
