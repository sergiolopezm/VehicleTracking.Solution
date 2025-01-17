using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[PrimaryKey("AccountId", "RoleId")]
[Table("AccountRole")]
public partial class AccountRole
{
    [Key]
    public int AccountId { get; set; }

    [Key]
    public int RoleId { get; set; }
}
