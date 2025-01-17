using System.ComponentModel.DataAnnotations;

namespace VehicleTracking.Shared.InDTO
{
    public class UsuarioRegistroDto
    {
        [Required(ErrorMessage = "El nombre de usuario es requerido")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "El nombre de usuario debe tener entre 3 y 100 caracteres")]
        public string? NombreUsuario { get; set; }

        [Required(ErrorMessage = "La contraseña es requerida")]
        [StringLength(50, MinimumLength = 6, ErrorMessage = "La contraseña debe tener entre 6 y 50 caracteres")]
        public string? Contraseña { get; set; }

        [Required(ErrorMessage = "El nombre es requerido")]
        [StringLength(100, ErrorMessage = "El nombre no puede exceder los 100 caracteres")]
        public string? Nombre { get; set; }

        [Required(ErrorMessage = "El apellido es requerido")]
        [StringLength(100, ErrorMessage = "El apellido no puede exceder los 100 caracteres")]
        public string? Apellido { get; set; }

        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress(ErrorMessage = "El formato del email no es válido")]
        [StringLength(100, ErrorMessage = "El email no puede exceder los 100 caracteres")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "El rol es requerido")]
        [Range(1, int.MaxValue, ErrorMessage = "Debe seleccionar un rol válido")]
        public int RolId { get; set; }
    }
}
