using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

[Table("Usuario")]
[Index("NombreUsuario", Name = "UQ__Usuario__6B0F5AE029CDB0D6", IsUnique = true)]
[Index("Email", Name = "UQ__Usuario__A9D10534C6D51644", IsUnique = true)]
public partial class Usuario
{
    [Key]
    [StringLength(20)]
    [Unicode(false)]
    public string IdUsuario { get; set; } = null!;

    [StringLength(100)]
    [Unicode(false)]
    public string NombreUsuario { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string Contraseña { get; set; } = null!;

    [StringLength(100)]
    [Unicode(false)]
    public string Nombre { get; set; } = null!;

    [StringLength(100)]
    [Unicode(false)]
    public string Apellido { get; set; } = null!;

    [StringLength(100)]
    [Unicode(false)]
    public string Email { get; set; } = null!;

    public int RoleId { get; set; }

    public bool? Active { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Created { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Updated { get; set; }

    [ForeignKey("RoleId")]
    [InverseProperty("Usuarios")]
    public virtual Role Role { get; set; } = null!;

    [InverseProperty("IdUsuarioNavigation")]
    public virtual ICollection<TokenExpirado> TokenExpirados { get; set; } = new List<TokenExpirado>();

    [InverseProperty("IdUsuarioNavigation")]
    public virtual ICollection<Token> Tokens { get; set; } = new List<Token>();
}
