using VehicleTracking.Shared.GeneralDTO;
using VehicleTracking.Shared.InDTO;

namespace VehicleTracking.Domain.Contracts
{
    public interface IUsuarioRepository
    {
        RespuestaDto AutenticarUsuario(UsuarioLoginDto args);
        RespuestaDto RegistrarUsuario(UsuarioRegistroDto args);
    }
}