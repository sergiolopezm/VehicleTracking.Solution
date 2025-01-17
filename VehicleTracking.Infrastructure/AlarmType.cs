using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("AlarmType")]
public partial class AlarmType
{
    [Key]
    public int Id { get; set; }

    [StringLength(32)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    public bool Active { get; set; }
}
