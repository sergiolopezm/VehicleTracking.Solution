using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Acceso")]
public partial class Acceso
{
    [Key]
    public int Id { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string Sitio { get; set; } = null!;

    [StringLength(250)]
    [Unicode(false)]
    public string Contraseña { get; set; } = null!;

    public bool? Active { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Created { get; set; }
}
