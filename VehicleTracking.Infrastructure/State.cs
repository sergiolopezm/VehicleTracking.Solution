using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("State")]
[Index("Command", Name = "uk_Command", IsUnique = true)]
public partial class State
{
    [Key]
    public int Id { get; set; }

    public int Process { get; set; }

    public int Action { get; set; }

    public int TransitTo { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string Lane { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string Command { get; set; } = null!;

    [StringLength(30)]
    [Unicode(false)]
    public string Icon { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string Message { get; set; } = null!;
}
