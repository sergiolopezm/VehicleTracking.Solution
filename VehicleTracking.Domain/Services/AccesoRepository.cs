using VehicleTracking.Domain.Contracts;
using VehicleTracking.Infrastructure;

namespace VehicleTracking.Domain.Services
{
    public class AccesoRepository : IAccesoRepository
    {
        private readonly DBContext _context;

        public AccesoRepository(DBContext context)
        {
            _context = context;
        }

        public bool ValidarAcceso(string sitio, string contraseña)
        {
            return _context.Accesos.Any(a => a.Sitio == sitio && a.Contraseña == contraseña);
        }
    }
}