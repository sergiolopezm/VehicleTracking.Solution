namespace VehicleTracking.Domain.Contracts
{
    public interface IConstantesService
    {
        string ObtenerPrefijoUsuario(int rolId);
        bool EsRolValido(int rolId);
        int[] ObtenerRolesValidos();
    }
}
