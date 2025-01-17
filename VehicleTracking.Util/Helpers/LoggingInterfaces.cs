namespace VehicleTracking.Util.Helpers
{
    public interface IFileLogger
    {
        void WriteLog(string? userId, string? ip, string? action, string? detail, string logType);
    }

    public interface IRepositoryLogger
    {
        void Info(string? userId, string? ip, string? action, string? detail);
        void Error(string? userId, string? ip, string? action, string? error);
        void Action(string? userId, string? ip, string? action, string? detail);
        void Log(string? userId, string? ip, string? action, string? detail, string type);
    }

    public class ScrapingLogger
    {
        private readonly IFileLogger _fileLogger;
        private readonly IRepositoryLogger _logRepository;
        private readonly string _userId;
        private readonly string _ip;
        private readonly string _context;

        public ScrapingLogger(
            IFileLogger fileLogger,
            IRepositoryLogger logRepository,
            string userId,
            string ip,
            string context)
        {
            _fileLogger = fileLogger;
            _logRepository = logRepository;
            _userId = userId;
            _ip = ip;
            _context = context;
        }

        public void Debug(string message)
        {
            _fileLogger.WriteLog(_userId, _ip, _context, message, "DEBUG");
        }

        public void Info(string message, bool logToDb = false)
        {
            _fileLogger.WriteLog(_userId, _ip, _context, message, "INFO");

            if (logToDb)
            {
                _logRepository.Info(_userId, _ip, _context, message);
            }
        }

        public void Warning(string message, bool logToDb = false)
        {
            _fileLogger.WriteLog(_userId, _ip, _context, message, "WARNING");

            if (logToDb)
            {
                _logRepository.Log(_userId, _ip, _context, message, "WARNING");
            }
        }

        public void Error(string message, Exception? ex = null)
        {
            var fullMessage = ex != null ? $"{message}. Error: {ex.Message}" : message;

            _fileLogger.WriteLog(_userId, _ip, _context, fullMessage, "ERROR");
            _logRepository.Error(_userId, _ip, _context, fullMessage);
        }

        public void Action(string message)
        {
            _fileLogger.WriteLog(_userId, _ip, _context, message, "ACTION");
            _logRepository.Action(_userId, _ip, _context, message);
        }
    }
}
