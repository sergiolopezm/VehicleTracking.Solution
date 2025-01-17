using System.Reflection;

namespace VehicleTracking.Util
{
    public class Maping
    {
        public static TDestino Convertir<TOrigen, TDestino>(TOrigen origen)
        where TOrigen : class
        where TDestino : class, new()
        {
            if (origen == null)
            {
                throw new ArgumentNullException(nameof(origen));
            }

            var destino = new TDestino();

            CopiarPropiedades(origen, destino);

            return destino;
        }

        public static TDestino Convertir<TOrigen1, TOrigen2, TDestino>(TOrigen1 origen1, TOrigen2 origen2)
        where TOrigen1 : class
        where TOrigen2 : class
        where TDestino : class, new()
        {
            if (origen1 == null)
            {
                throw new ArgumentNullException(nameof(origen1));
            }

            if (origen2 == null)
            {
                throw new ArgumentNullException(nameof(origen2));
            }

            var destino = new TDestino();

            CopiarPropiedades(origen1, destino);
            CopiarPropiedades(origen2, destino);

            return destino;
        }

        public static TDestino Convertir<TOrigen1, TOrigen2, TOrigen3, TDestino>(TOrigen1 origen1, TOrigen2 origen2, TOrigen3 origen3)
        where TOrigen1 : class
        where TOrigen2 : class
        where TOrigen3 : class
        where TDestino : class, new()
        {
            if (origen1 == null)
            {
                throw new ArgumentNullException(nameof(origen1));
            }

            if (origen2 == null)
            {
                throw new ArgumentNullException(nameof(origen2));
            }

            if (origen3 == null)
            {
                throw new ArgumentNullException(nameof(origen3));
            }

            var destino = new TDestino();

            CopiarPropiedades(origen1, destino);
            CopiarPropiedades(origen2, destino);
            CopiarPropiedades(origen3, destino);

            return destino;
        }

        private static void CopiarPropiedades<TOrigen, TDestino>(TOrigen origen, TDestino destino)
        {
            foreach (PropertyInfo propiedadOrigen in typeof(TOrigen).GetProperties())
            {
                PropertyInfo propiedadDestino = typeof(TDestino).GetProperty(propiedadOrigen.Name)!;
                if (propiedadDestino != null && propiedadDestino.CanWrite)
                {
                    propiedadDestino.SetValue(destino, propiedadOrigen.GetValue(origen));
                }
            }
        }
    }
}
