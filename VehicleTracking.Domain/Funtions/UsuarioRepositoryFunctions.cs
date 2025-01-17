namespace VehicleTracking.Domain.Services
{
    public partial class UsuarioRepository
    {
        private string GenerarIdUsuario(int rolId)
        {
            string prefix = ObtenerPrefijoPorRol(rolId);
            var usuarios = _context.Usuarios
                .Where(u => u.IdUsuario.StartsWith(prefix))
                .ToList();

            if (!usuarios.Any())
            {
                return $"{prefix}000001";
            }

            var maxId = usuarios
                .Select(u =>
                {
                    if (int.TryParse(u.IdUsuario.Substring(prefix.Length), out int id))
                        return id;
                    return 0;
                })
                .Max();

            return $"{prefix}{(maxId + 1):D6}";
        }

        private string ObtenerPrefijoPorRol(int rolId)
        {
            if (rolId == _usuarioSettings.Roles.Admin)
                return _usuarioSettings.Prefijos.Admin;
            if (rolId == _usuarioSettings.Roles.Security)
                return _usuarioSettings.Prefijos.Security;
            if (rolId == _usuarioSettings.Roles.Operator)
                return _usuarioSettings.Prefijos.Operator;            

            throw new ArgumentException($"RolId no válido: {rolId}");
        }

        private bool EsRolValido(int rolId)
        {
            return rolId == _usuarioSettings.Roles.Admin ||
                   rolId == _usuarioSettings.Roles.Security ||
                   rolId == _usuarioSettings.Roles.Operator;
        }

        private string ObtenerNombreRol(int rolId)
        {
            return rolId switch
            {
                var id when id == _usuarioSettings.Roles.Admin => "Administrador",
                var id when id == _usuarioSettings.Roles.Security => "Seguridad",
                var id when id == _usuarioSettings.Roles.Operator => "Operador",                
                _ => "Rol Desconocido"
            };
        }
    }
}
