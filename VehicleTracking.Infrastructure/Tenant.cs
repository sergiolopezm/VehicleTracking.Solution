using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Tenant")]
public partial class Tenant
{
    [Key]
    public int Id { get; set; }

    public DateOnly Created { get; set; }

    [StringLength(50)]
    public string Name { get; set; } = null!;

    [StringLength(10)]
    public string Alias { get; set; } = null!;

    [StringLength(50)]
    public string Domine { get; set; } = null!;

    [StringLength(50)]
    public string Directory { get; set; } = null!;

    public bool Active { get; set; }
}
