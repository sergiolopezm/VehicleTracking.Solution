using VehicleTracking.Domain.Contracts;
using VehicleTracking.Shared.InDTO;
using Microsoft.Extensions.Options;

namespace VehicleTracking.Domain.Services
{
    public class ConstantesService : IConstantesService
    {
        private readonly UsuarioSettings _settings;

        public ConstantesService(IOptions<UsuarioSettings> settings)
        {
            _settings = settings.Value;
        }

        public string ObtenerPrefijoUsuario(int rolId)
        {
            return rolId switch
            {
                var id when id == _settings.Roles.Admin => _settings.Prefijos.Admin,
                var id when id == _settings.Roles.Security => _settings.Prefijos.Security,
                var id when id == _settings.Roles.Operator => _settings.Prefijos.Operator,                
                _ => throw new ArgumentException($"RolId no válido: {rolId}")
            };
        }

        public bool EsRolValido(int rolId)
        {
            return ObtenerRolesValidos().Contains(rolId);
        }

        public int[] ObtenerRolesValidos()
        {
            return new[]
            {
            _settings.Roles.Admin,
            _settings.Roles.Security,
            _settings.Roles.Operator            
        };
        }
    }
}
