using Microsoft.EntityFrameworkCore;
using VehicleTracking.Domain.Contracts;
using VehicleTracking.Infrastructure;

namespace VehicleTracking.Domain.Services
{
    public class LogRepository : ILogRepository
    {
        private readonly IDbContextFactory<DBContext> _contextFactory;
        private readonly FileLoggerService _fileLogger;

        public LogRepository(IDbContextFactory<DBContext> contextFactory, FileLoggerService fileLogger)
        {
            _contextFactory = contextFactory;
            _fileLogger = fileLogger;
        }

        public void Accion(string idUsuario, string ip, string accion, string detalle)
        {
            Task guardarLog = GuardarLogAsync(idUsuario, ip, accion, detalle, "200");
        }

        public void Error(string idUsuario, string ip, string accion, string error)
        {
            Task guardarLog = GuardarLogAsync(idUsuario, ip, accion, error, "500");
        }

        public void Info(string idUsuario, string ip, string accion, string detalle)
        {
            Task guardarLog = GuardarLogAsync(idUsuario, ip, accion, detalle, "400");
        }

        public void Log(string idUsuario, string ip, string accion, string detalle, string tipo)
        {
            Task guardarLog = GuardarLogAsync(idUsuario, ip, accion, detalle, tipo);
        }

        private async Task GuardarLogAsync(string idUsuario, string ip, string accion, string detalle, string tipo)
        {
            // Asegurar que el tipo no exceda los 3 caracteres
            if (tipo != null && tipo.Length > 3)
            {
                // Mapear tipos comunes a sus equivalentes de 3 caracteres
                tipo = tipo switch
                {
                    "INFO" => "400",
                    "WARNING" => "WAR",
                    "ERROR" => "500",
                    "DEBUG" => "DBG",
                    _ => tipo.Length > 3 ? tipo.Substring(0, 3) : tipo
                };
            }

            var log = new Log
            {
                Fecha = DateTime.Now,
                IdUsuario = idUsuario,
                Ip = ip,
                Accion = accion,
                Tipo = tipo,
                Detalle = detalle
            };

            // Guardar en archivo
            _fileLogger.WriteLog(idUsuario, ip, accion, detalle, tipo);

            // Guardar en base de datos
            await using var dbContext = await _contextFactory.CreateDbContextAsync();
            await dbContext.Logs.AddAsync(log);
            await dbContext.SaveChangesAsync();
        }
    }
}