using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("AlarmProtocol")]
public partial class AlarmProtocol
{
    [Key]
    public int Id { get; set; }

    public int AlarmType { get; set; }

    public short Order { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [StringLength(512)]
    [Unicode(false)]
    public string Description { get; set; } = null!;
}
