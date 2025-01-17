using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Driver")]
public partial class Driver
{
    [Key]
    public int Id { get; set; }

    public DateOnly Created { get; set; }

    [StringLength(16)]
    [Unicode(false)]
    public string Identity { get; set; } = null!;

    [StringLength(64)]
    [Unicode(false)]
    public string FullName { get; set; } = null!;

    [StringLength(164)]
    [Unicode(false)]
    public string Phone { get; set; } = null!;
}
