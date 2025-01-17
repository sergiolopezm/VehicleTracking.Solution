using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Account")]
public partial class Account
{
    [Key]
    public int Id { get; set; }

    public DateOnly Created { get; set; }

    public DateOnly Updated { get; set; }

    public int TenantId { get; set; }

    [StringLength(50)]
    public string Email { get; set; } = null!;

    [StringLength(50)]
    public string PasswordHash { get; set; } = null!;

    [StringLength(10)]
    public string Code { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime Generated { get; set; }

    public bool IsAdmin { get; set; }

    public bool Active { get; set; }

    [StringLength(50)]
    public string UserName { get; set; } = null!;
}
