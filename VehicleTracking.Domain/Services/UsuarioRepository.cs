using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VehicleTracking.Domain.Contracts;
using VehicleTracking.Infrastructure;
using VehicleTracking.Shared.GeneralDTO;
using VehicleTracking.Shared.InDTO;
using VehicleTracking.Shared.OutDTO;

namespace VehicleTracking.Domain.Services
{
    public partial class UsuarioRepository : IUsuarioRepository
    {
        private readonly DBContext _context;
        private readonly ITokenRepository _tokenRepository;
        private readonly UsuarioSettings _usuarioSettings;

        public UsuarioRepository(
            DBContext context,
            ITokenRepository tokenRepository,
            IOptions<UsuarioSettings> usuarioSettings)
        {
            _context = context;
            _tokenRepository = tokenRepository;
            _usuarioSettings = usuarioSettings.Value;
        }

        public RespuestaDto AutenticarUsuario(UsuarioLoginDto args)
        {
            var usuario = _context.Usuarios
                .Include(u => u.Role)  
                .FirstOrDefault(u => u.NombreUsuario == args.NombreUsuario &&
                                   u.Contraseña == args.Contraseña);

            if (usuario == null)
            {
                return RespuestaDto.ParametrosIncorrectos(
                    "Sesión fallida",
                    "El usuario o la contraseña son incorrectos");
            }

            if (usuario.Active != true)
            {
                return RespuestaDto.ParametrosIncorrectos(
                    "Sesión fallida",
                    "El usuario no se encuentra activo");
            }

            string token = _tokenRepository.GenerarToken(usuario, args.Ip!);

            var usuarioOut = new UsuarioOutDto
            {
                IdUsuario = usuario.IdUsuario,
                NombreUsuario = usuario.NombreUsuario,
                Nombre = usuario.Nombre,
                Apellido = usuario.Apellido,
                Email = usuario.Email,
                Rol = usuario.Role.Name,
                Token = token
            };

            return new RespuestaDto
            {
                Exito = true,
                Mensaje = "Usuario autenticado",
                Detalle = $"El usuario {usuarioOut.NombreUsuario} se ha autenticado correctamente.",
                Resultado = usuarioOut
            };
        }

        public RespuestaDto RegistrarUsuario(UsuarioRegistroDto args)
        {
            try
            {
                // Validar que el RolId sea válido
                if (!EsRolValido(args.RolId))
                {
                    return RespuestaDto.ParametrosIncorrectos(
                        "Registro fallido",
                        "El rol especificado no es válido");
                }

                if (_context.Usuarios.Any(u => u.NombreUsuario == args.NombreUsuario))
                {
                    return RespuestaDto.ParametrosIncorrectos(
                        "Registro fallido",
                        "El nombre de usuario ya existe");
                }

                if (_context.Usuarios.Any(u => u.Email == args.Email))
                {
                    return RespuestaDto.ParametrosIncorrectos(
                        "Registro fallido",
                        "El email ya está registrado");
                }

                var usuario = new Usuario
                {
                    IdUsuario = GenerarIdUsuario(args.RolId),
                    NombreUsuario = args.NombreUsuario!,
                    Contraseña = args.Contraseña!,
                    Nombre = args.Nombre!,
                    Apellido = args.Apellido!,
                    Email = args.Email!,
                    RoleId = args.RolId,
                    Active = true
                };

                _context.Usuarios.Add(usuario);
                _context.SaveChanges();

                return new RespuestaDto
                {
                    Exito = true,
                    Mensaje = "Usuario registrado",
                    Detalle = "El usuario se ha registrado correctamente",
                    Resultado = new
                    {
                        IdUsuario = usuario.IdUsuario,
                        NombreUsuario = usuario.NombreUsuario,
                        Email = usuario.Email,
                        Rol = ObtenerNombreRol(usuario.RoleId)
                    }
                };
            }
            catch (Exception ex)
            {
                return RespuestaDto.ParametrosIncorrectos(
                    "Error de registro",
                    $"Error al registrar el usuario: {ex.Message}");
            }
        }

        
    }
}