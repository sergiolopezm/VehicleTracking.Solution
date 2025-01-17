using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Process")]
[Index("Code", Name = "uk_Code", IsUnique = true)]
public partial class Process
{
    [Key]
    public int Id { get; set; }

    public int Strategy { get; set; }

    public int? Sequence { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string Module { get; set; } = null!;
}
