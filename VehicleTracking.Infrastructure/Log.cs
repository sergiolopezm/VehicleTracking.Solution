using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Log")]
public partial class Log
{
    [Key]
    public int Id { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? IdUsuario { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Fecha { get; set; }

    [StringLength(3)]
    [Unicode(false)]
    public string Tipo { get; set; } = null!;

    [StringLength(15)]
    [Unicode(false)]
    public string? Ip { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? Accion { get; set; }

    [StringLength(5000)]
    [Unicode(false)]
    public string? Detalle { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Created { get; set; }
}
