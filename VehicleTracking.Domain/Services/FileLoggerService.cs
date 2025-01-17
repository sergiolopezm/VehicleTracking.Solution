using System.Text;
using Microsoft.Extensions.Configuration;

namespace VehicleTracking.Domain.Services
{
    public class FileLoggerService
    {
        private readonly string _logPath;
        private static readonly object _lock = new object();

        public FileLoggerService(IConfiguration configuration)
        {
            // La carpeta Logs estará en la raíz del proyecto
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            // Subimos un nivel para salir de la carpeta bin
            string projectPath = Directory.GetParent(basePath)?.Parent?.Parent?.Parent?.FullName
                ?? throw new InvalidOperationException("No se pudo determinar la ruta del proyecto");

            // Crear la carpeta Logs en la raíz del proyecto
            _logPath = Path.Combine(projectPath, "Logs");

            if (!Directory.Exists(_logPath))
            {
                Directory.CreateDirectory(_logPath);
            }
        }

        public void WriteLog(string idUsuario, string ip, string accion, string detalle, string tipo)
        {
            try
            {

                var serverDateTime = DateTime.Now;

                var logFileName = $"{serverDateTime:dd-MM-yyyy}.txt";
                var logFilePath = Path.Combine(_logPath, logFileName);

                // Crear el mensaje de log
                var logMessage = new StringBuilder();
                logMessage.AppendLine($"Fecha: {serverDateTime:dd/MM/yyyy hh:mm:ss tt}");
                logMessage.AppendLine($"Usuario: {idUsuario}");
                logMessage.AppendLine($"IP: {ip}");
                logMessage.AppendLine($"Tipo: {tipo}");
                logMessage.AppendLine($"Acción: {accion}");
                logMessage.AppendLine($"Detalle: {detalle}");
                logMessage.AppendLine(new string('-', 50));
                logMessage.AppendLine();

                // Escribir el log en el archivo de manera segura
                lock (_lock)
                {
                    File.AppendAllText(logFilePath, logMessage.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error escribiendo log en archivo: {ex.Message}. Ruta: {_logPath}");
            }
        }

    }
}