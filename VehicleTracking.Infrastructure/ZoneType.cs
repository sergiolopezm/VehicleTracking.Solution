using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("ZoneType")]
public partial class ZoneType
{
    [Key]
    public int Id { get; set; }

    public DateOnly Created { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    public int Profile { get; set; }

    [InverseProperty("ZoneTypeNavigation")]
    public virtual ICollection<Zone> Zones { get; set; } = new List<Zone>();
}
