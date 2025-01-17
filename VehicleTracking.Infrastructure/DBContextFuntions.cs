using System.Linq.Expressions;

namespace VehicleTracking.Infrastructure
{
    public partial class DBContext
    {

        public ListaPaginada<TResult> PaginateListDTO<TResult, T>(
            int page, int pageSize,
            Expression<Func<T, bool>>? filter = null,
            Expression<Func<T, object>>? orderBy = null,
            bool isDescending = false
        ) where TResult : class, new() where T : class
        {
            ListaPaginada<TResult> listaPaginada = new ListaPaginada<TResult>();

            IQueryable<T> query = Set<T>();

            if (filter != null)
            {
                query = query.Where(filter);
            }

            int skip = (page - 1) * pageSize;

            if (orderBy != null)
            {
                query = isDescending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
            }

            var selectExpression = CreateSelectExpression<TResult, T>();

            listaPaginada.totalRegistros = query.Count();
            if (page > 0 && pageSize > 0)
            {
                listaPaginada.pagina = page;
                listaPaginada.totalPaginas = listaPaginada.totalRegistros / pageSize;
                if (listaPaginada.totalRegistros % pageSize > 0)
                {
                    listaPaginada.totalPaginas++;
                }

                listaPaginada.lista = query.Skip(skip).Take(pageSize).Select(selectExpression).ToList();
            }
            else
            {
                listaPaginada.pagina = 1;
                listaPaginada.totalPaginas = 1;
                listaPaginada.lista = query.Select(selectExpression).ToList();
            }

            return listaPaginada;
        }

        public TResult SelectDTO<TResult, T>(Expression<Func<T, bool>>? filter = null)
        where TResult : class, new() where T : class
        {
            IQueryable<T> query = Set<T>();

            if (filter != null)
            {
                query = query.Where(filter);
            }

            var selectExpression = CreateSelectExpression<TResult, T>();

            return query.Select(selectExpression).FirstOrDefault()!;

        }

        public TResult SelectDTO<TResult, TEntity, TJoin>(Expression<Func<TEntity, bool>>? filter = null)
        where TResult : class, new() where TEntity : class, new() where TJoin : class
        {
            IQueryable<TEntity> query = Set<TEntity>();

            if (filter != null)
            {
                query = query.Where(filter);
            }

            var selectExpression = CreateSelectExpression<TResult, TEntity>();

            return query.Select(selectExpression).FirstOrDefault()!;

        }

        private Expression<Func<T, TResult>> CreateSelectExpression<TResult, T>()
        where T : class
        where TResult : class, new()
        {
            var dtoInstance = new TResult();
            var properties = typeof(TResult).GetProperties();

            var parameter = Expression.Parameter(typeof(T), "x");
            var bindings = properties.Select(property =>
            {
                var sourceProperty = typeof(T).GetProperty(property.Name);
                if (sourceProperty != null)
                {
                    var sourcePropertyAccess = Expression.Property(parameter, sourceProperty);
                    return Expression.Bind(property, sourcePropertyAccess);
                }
                return null;
            }).Where(binding => binding != null);

            var memberInit = Expression.MemberInit(Expression.New(typeof(TResult)), bindings!);
            var selectExpression = Expression.Lambda<Func<T, TResult>>(memberInit, parameter);

            return selectExpression;
        }

    }
}
