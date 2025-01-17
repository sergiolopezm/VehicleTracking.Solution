using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Role")]
public partial class Role
{
    [Key]
    public int Id { get; set; }

    public int TenantId { get; set; }

    public DateOnly Created { get; set; }

    public DateOnly Updated { get; set; }

    [StringLength(50)]
    public string Name { get; set; } = null!;

    public bool Active { get; set; }

    [InverseProperty("Role")]
    public virtual ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
}
