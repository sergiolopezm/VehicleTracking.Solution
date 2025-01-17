namespace VehicleTracking.Domain.Contracts
{
    public interface ILogRepository
    {
        void Accion(string? idUsuario, string? ip, string? accion, string? detalle);
        void Info(string? idUsuario, string? ip, string? accion, string? detalle);
        void Error(string? idUsuario, string? ip, string? accion, string? error);
        void Log(string? idUsuario, string? ip, string? accion, string? detalle, string tipo);
    }
}