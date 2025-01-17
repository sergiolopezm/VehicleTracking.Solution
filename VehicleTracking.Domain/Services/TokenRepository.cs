using VehicleTracking.Domain.Contracts;
using VehicleTracking.Infrastructure;
using VehicleTracking.Shared.GeneralDTO;
using Microsoft.Extensions.Configuration;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace VehicleTracking.Domain.Services
{
    public class TokenRepository : ITokenRepository
    {
        private readonly IDbContextFactory<DBContext> _contextFactory;
        private readonly byte[] _keyBytes;
        private readonly int _tiempoExpiracion;
        private readonly int _tiempoExpiracionBD;

        public TokenRepository(
            IConfiguration config,
            IDbContextFactory<DBContext> contextFactory)
        {
            _contextFactory = contextFactory;
            string keyJwt = config.GetSection("JwtSettings")["Key"]!;
            _keyBytes = Encoding.UTF8.GetBytes(keyJwt);
            _tiempoExpiracion = int.Parse(config.GetSection("JwtSettings")["TiempoExpiracionMinutos"]!);
            _tiempoExpiracionBD = int.Parse(config.GetSection("JwtSettings")["TiempoExpiracionBDMinutos"]!);
        }

        public string GenerarToken(Usuario usuario, string ip)
        {
            using var context = _contextFactory.CreateDbContext();
            RemoverTokensExpirados(usuario);
            string token = CrearTokenUsuario(usuario, ip);
            GuardarTokenBD(token, usuario, ip);
            return token;
        }

        public bool CancelarToken(string token)
        {
            using var context = _contextFactory.CreateDbContext();
            var tokenBD = context.Tokens.FirstOrDefault(t => t.IdToken == token);
            if (tokenBD != null)
            {
                tokenBD.FechaExpiracion = DateTime.Now;
                context.SaveChanges();
                return true;
            }
            return false;
        }

        public Object ObtenerInformacionToken(string token)
        {
            using var context = _contextFactory.CreateDbContext();
            var tokenBD = context.Tokens.FirstOrDefault(t => t.IdToken == token);
            if (tokenBD != null)
            {
                return new
                {
                    IdToken = tokenBD.IdToken,
                    IdUsuario = tokenBD.IdUsuario,
                    Ip = tokenBD.Ip,
                    FechaAutenticacion = tokenBD.FechaAutenticacion,
                    FechaExpiracion = tokenBD.FechaExpiracion
                };
            }
            return null!;
        }

        public ValidoDTO EsValido(string idToken, string idUsuario, string ip)
        {
            var validarToken = ValidarTokenEnSistema(idToken, idUsuario, ip);
            if (!validarToken.EsValido)
            {
                return validarToken;
            }
            return ValidarTokenEnBD(idToken);
        }

        public void AumentarTiempoExpiracion(string token)
        {
            using var context = _contextFactory.CreateDbContext();
            var tokenBD = context.Tokens.FirstOrDefault(t => t.IdToken == token);
            if (tokenBD != null)
            {
                tokenBD.FechaExpiracion = DateTime.Now.AddMinutes(_tiempoExpiracionBD);
                context.SaveChanges();
            }
        }

        private void GuardarTokenBD(string idToken, Usuario usuario, string ip)
        {
            using var context = _contextFactory.CreateDbContext();
            Token token = new Token
            {
                IdToken = idToken,
                IdUsuario = usuario.IdUsuario,
                Ip = ip,
                FechaAutenticacion = DateTime.Now,
                FechaExpiracion = DateTime.Now.AddMinutes(_tiempoExpiracionBD),
            };

            context.Tokens.Add(token);
            context.SaveChanges();
        }

        private void RemoverTokensExpirados(Usuario usuario)
        {
            using var context = _contextFactory.CreateDbContext();
            var TokenUsuarioYaAutenticado = context.Tokens
                .Where(t => t.IdUsuario == usuario.IdUsuario && t.FechaExpiracion > DateTime.Now)
                .FirstOrDefault();

            var TokensUsuarioExpirado = context.Tokens
                .Where(u => u.IdUsuario == usuario.IdUsuario && u.FechaExpiracion < DateTime.Now)
                .ToList();

            if (TokensUsuarioExpirado.Any())
            {
                var tokensExpirados = ConvertirListJwtUsuarioExpiradoAListJwtUsuario(TokensUsuarioExpirado);
                context.TokenExpirados.AddRange(tokensExpirados);
                context.Tokens.RemoveRange(TokensUsuarioExpirado);
            }

            if (TokenUsuarioYaAutenticado != null)
            {
                TokenUsuarioYaAutenticado.Observacion = "La sesión ha caducado debido a que el usuario ha ingresado desde otro equipo";
                TokenUsuarioYaAutenticado.FechaExpiracion = DateTime.Now;
            }

            context.SaveChanges();
        }

        private ValidoDTO ValidarTokenEnBD(string idToken)
        {
            using var context = _contextFactory.CreateDbContext();
            var token = context.Tokens.FirstOrDefault(t => t.IdToken == idToken);

            if (token == null)
            {
                return ValidoDTO.Invalido("La sesión de usuario no se encuentra activa. Por favor, inicie sesión");
            }

            if (token.FechaExpiracion < DateTime.Now)
            {
                if (string.IsNullOrEmpty(token.Observacion))
                {
                    return ValidoDTO.Invalido("La sesión de usuario a caducado por tiempo de inactividad. Por favor, inicie sesión nuevamente");
                }
                return ValidoDTO.Invalido(token.Observacion);
            }

            return ValidoDTO.Valido();
        }

        private ValidoDTO ValidarTokenEnSistema(string idToken, string idUsuario, string ip)
        {
            if (idToken == null || idUsuario == null || ip == null)
            {
                return ValidoDTO.Invalido("La información referente a la sesión de usuario esta incompleta");
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(_keyBytes),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            try
            {
                ClaimsPrincipal principal = tokenHandler.ValidateToken(idToken, validationParameters, out _);

                string claimIdUsuario = principal.FindFirst("IdUsuario")!.Value;
                string claimIp = principal.FindFirst("Ip")!.Value;
                if (idUsuario != claimIdUsuario || ip != claimIp)
                {
                    return ValidoDTO.Invalido("La información referente a la sesión de usuario es incorrecta");
                }

                return ValidoDTO.Valido();
            }
            catch
            {
                return ValidoDTO.Invalido("La sesión de usuario no se encuentra activa o no existe en el sistema. Por favor, inicie sesión");
            }
        }

        private string CrearTokenUsuario(Usuario usuario, string ip)
        {
            var claims = new ClaimsIdentity();
            claims.AddClaim(new Claim("Random", new Random().Next(100000000, 1000000000).ToString()));
            claims.AddClaim(new Claim("IdUsuario", usuario.IdUsuario));
            claims.AddClaim(new Claim("Usuario", usuario.NombreUsuario));
            claims.AddClaim(new Claim("Nombre", usuario.Nombre));
            claims.AddClaim(new Claim("Apellido", usuario.Apellido));
            claims.AddClaim(new Claim("Rol", usuario.Role.Name));
            claims.AddClaim(new Claim("Ip", ip));

            var credencialesToken = new SigningCredentials(
                new SymmetricSecurityKey(_keyBytes),
                SecurityAlgorithms.HmacSha256Signature);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = claims,
                Expires = DateTime.UtcNow.AddMinutes(_tiempoExpiracion),
                SigningCredentials = credencialesToken
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenConfig = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(tokenConfig);
        }

        private List<TokenExpirado> ConvertirListJwtUsuarioExpiradoAListJwtUsuario(List<Token> tokens)
        {
            return tokens.Select(token => new TokenExpirado
            {
                IdToken = token.IdToken,
                IdUsuario = token.IdUsuario,
                Ip = token.Ip,
                FechaAutenticacion = (DateTime)token.FechaAutenticacion,
                FechaExpiracion = token.FechaExpiracion,
                Observacion = token.Observacion
            }).ToList();
        }
    }
}