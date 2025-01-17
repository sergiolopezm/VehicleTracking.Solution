using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Event")]
public partial class Event
{
    [Key]
    public int Id { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [StringLength(64)]
    [Unicode(false)]
    public string Description { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string Object { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string Command { get; set; } = null!;
}
