using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Token")]
public partial class Token
{
    [Key]
    [StringLength(500)]
    [Unicode(false)]
    public string IdToken { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string IdUsuario { get; set; } = null!;

    [StringLength(15)]
    [Unicode(false)]
    public string Ip { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime? FechaAutenticacion { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime FechaExpiracion { get; set; }

    [StringLength(200)]
    [Unicode(false)]
    public string? Observacion { get; set; }

    public bool? Active { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Created { get; set; }

    [ForeignKey("IdUsuario")]
    [InverseProperty("Tokens")]
    public virtual Usuario IdUsuarioNavigation { get; set; } = null!;
}
