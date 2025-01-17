using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace VehicleTracking.Infrastructure;

[Table("Zone")]
public partial class Zone
{
    [Key]
    public int Id { get; set; }

    public int TenantId { get; set; }

    public int ZoneType { get; set; }

    public DateOnly Created { get; set; }

    public Geometry Path { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string Info { get; set; } = null!;

    public bool Active { get; set; }

    [ForeignKey("ZoneType")]
    [InverseProperty("Zones")]
    public virtual ZoneType ZoneTypeNavigation { get; set; } = null!;
}
