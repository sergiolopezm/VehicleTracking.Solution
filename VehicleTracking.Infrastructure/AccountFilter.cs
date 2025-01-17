using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("AccountFilter")]
public partial class AccountFilter
{
    [Key]
    public int Id { get; set; }

    public int AccountId { get; set; }

    public int ClientId { get; set; }
}
