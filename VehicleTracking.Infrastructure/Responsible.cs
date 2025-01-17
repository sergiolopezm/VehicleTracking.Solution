using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Responsible")]
public partial class Responsible
{
    [Key]
    public int Id { get; set; }

    public DateTime Created { get; set; }

    public bool Active { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string Name { get; set; } = null!;
}
