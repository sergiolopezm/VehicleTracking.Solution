using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("IdentityType")]
public partial class IdentityType
{
    [Key]
    public int Id { get; set; }

    public DateOnly Created { get; set; }

    public bool Active { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [StringLength(5)]
    [Unicode(false)]
    public string Short { get; set; } = null!;
}
