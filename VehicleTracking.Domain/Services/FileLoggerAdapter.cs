using VehicleTracking.Domain.Contracts;
using VehicleTracking.Util.Helpers;

namespace VehicleTracking.Domain.Services
{
    public class FileLoggerAdapter : IFileLogger
    {
        private readonly FileLoggerService _fileLoggerService;

        public FileLoggerAdapter(FileLoggerService fileLoggerService)
        {
            _fileLoggerService = fileLoggerService;
        }

        public void WriteLog(string? userId, string? ip, string? action, string? detail, string logType)
        {
            _fileLoggerService.WriteLog(userId, ip, action, detail, logType);
        }
    }

    public class LogRepositoryAdapter : IRepositoryLogger
    {
        private readonly ILogRepository _logRepository;

        public LogRepositoryAdapter(ILogRepository logRepository)
        {
            _logRepository = logRepository;
        }

        public void Info(string? userId, string? ip, string? action, string? detail)
        {
            _logRepository.Info(userId, ip, action, detail);
        }

        public void Error(string? userId, string? ip, string? action, string? error)
        {
            _logRepository.Error(userId, ip, action, error);
        }

        public void Action(string? userId, string? ip, string? action, string? detail)
        {
            _logRepository.Accion(userId, ip, action, detail);
        }

        public void Log(string? userId, string? ip, string? action, string? detail, string type)
        {
            _logRepository.Log(userId, ip, action, detail, type);
        }
    }
}
