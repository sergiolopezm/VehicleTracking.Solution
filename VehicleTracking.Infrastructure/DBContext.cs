using Microsoft.EntityFrameworkCore;

namespace VehicleTracking.Infrastructure;

public partial class DBContext : DbContext
{
    public DBContext()
    {
    }

    public DBContext(DbContextOptions<DBContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Acceso> Accesos { get; set; }

    public virtual DbSet<Account> Accounts { get; set; }

    public virtual DbSet<AccountFilter> AccountFilters { get; set; }

    public virtual DbSet<AccountRole> AccountRoles { get; set; }

    public virtual DbSet<Alarm> Alarms { get; set; }

    public virtual DbSet<AlarmProtocol> AlarmProtocols { get; set; }

    public virtual DbSet<AlarmState> AlarmStates { get; set; }

    public virtual DbSet<AlarmTracing> AlarmTracings { get; set; }

    public virtual DbSet<AlarmType> AlarmTypes { get; set; }

    public virtual DbSet<BlobStore> BlobStores { get; set; }

    public virtual DbSet<City> Cities { get; set; }

    public virtual DbSet<CityAlias> CityAliases { get; set; }

    public virtual DbSet<Client> Clients { get; set; }

    public virtual DbSet<ClientReportContact> ClientReportContacts { get; set; }

    public virtual DbSet<Cliente> Clientes { get; set; }

    public virtual DbSet<DataBi> DataBis { get; set; }

    public virtual DbSet<Delivery> Deliveries { get; set; }

    public virtual DbSet<Driver> Drivers { get; set; }

    public virtual DbSet<Event> Events { get; set; }

    public virtual DbSet<EventAction> EventActions { get; set; }

    public virtual DbSet<IdentityType> IdentityTypes { get; set; }

    public virtual DbSet<KnowLocation> KnowLocations { get; set; }

    public virtual DbSet<KnowPoint> KnowPoints { get; set; }

    public virtual DbSet<Log> Logs { get; set; }

    public virtual DbSet<Manifest> Manifests { get; set; }

    public virtual DbSet<ManifestRestriction> ManifestRestrictions { get; set; }

    public virtual DbSet<News> News { get; set; }

    public virtual DbSet<NewsState> NewsStates { get; set; }

    public virtual DbSet<NewsTracing> NewsTracings { get; set; }

    public virtual DbSet<Novelty> Novelties { get; set; }

    public virtual DbSet<Order> Orders { get; set; }

    public virtual DbSet<Position> Positions { get; set; }

    public virtual DbSet<Process> Processes { get; set; }

    public virtual DbSet<ProcessStrategy> ProcessStrategies { get; set; }

    public virtual DbSet<Report> Reports { get; set; }

    public virtual DbSet<Responsible> Responsibles { get; set; }

    public virtual DbSet<Restriction> Restrictions { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Route> Routes { get; set; }

    public virtual DbSet<State> States { get; set; }

    public virtual DbSet<Tenant> Tenants { get; set; }

    public virtual DbSet<Token> Tokens { get; set; }

    public virtual DbSet<TokenExpirado> TokenExpirados { get; set; }

    public virtual DbSet<Tracking> Trackings { get; set; }

    public virtual DbSet<Usuario> Usuarios { get; set; }

    public virtual DbSet<Vehicle> Vehicles { get; set; }

    public virtual DbSet<VehicleInfoLocation> VehicleInfoLocations { get; set; }

    public virtual DbSet<Zone> Zones { get; set; }

    public virtual DbSet<ZoneType> ZoneTypes { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer(
                "Data Source=KONSISDES;Initial Catalog=monitordb;Integrated Security=True;Trust Server Certificate=True;",
                x => x.UseNetTopologySuite()
            );
        }
        else
        {
            // Si ya está configurado, solo asegurarnos de que use NetTopologySuite
            optionsBuilder.UseSqlServer(
                optionsBuilder.Options.GetExtension<Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal.SqlServerOptionsExtension>().ConnectionString,
                x => x.UseNetTopologySuite()
            );
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("SQL_Latin1_General_CP1_CI_AS");

        modelBuilder.Entity<Acceso>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Acceso__3214EC07DA3391F0");

            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Account__3214EC07E01E91B9");

            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Code).HasDefaultValue("");
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Generated).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Updated).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<AccountRole>(entity =>
        {
            entity.HasKey(e => new { e.AccountId, e.RoleId }).HasName("PK__AccountR__8C3209475B116E97");
        });

        modelBuilder.Entity<Alarm>(entity =>
        {
            entity.Property(e => e.Command).HasDefaultValue("");
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Internal).IsFixedLength();
            entity.Property(e => e.Updated).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<AlarmTracing>(entity =>
        {
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<AlarmType>(entity =>
        {
            entity.Property(e => e.Active).HasDefaultValue(true);
        });

        modelBuilder.Entity<BlobStore>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_FileStore");

            entity.Property(e => e.Created).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<City>(entity =>
        {
            entity.Property(e => e.Created).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<DataBi>(entity =>
        {
            entity.ToView("DataBI");
        });

        modelBuilder.Entity<Delivery>(entity =>
        {
            entity.Property(e => e.Container).HasDefaultValue("");
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Invoice).HasDefaultValue("");
        });

        modelBuilder.Entity<Driver>(entity =>
        {
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<EventAction>(entity =>
        {
            entity.Property(e => e.Active).HasDefaultValue(true);
        });

        modelBuilder.Entity<IdentityType>(entity =>
        {
            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<KnowLocation>(entity =>
        {
            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<KnowPoint>(entity =>
        {
            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<Log>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Log__3214EC079E9063F1");

            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Fecha).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<Manifest>(entity =>
        {
            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Remaining).HasDefaultValue(-1);
            entity.Property(e => e.Trailer).HasDefaultValue("");
            entity.Property(e => e.Updated).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<News>(entity =>
        {
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<NewsTracing>(entity =>
        {
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<Novelty>(entity =>
        {
            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<Position>(entity =>
        {
            entity.Property(e => e.VehicleId).ValueGeneratedNever();
        });

        modelBuilder.Entity<Report>(entity =>
        {
            entity.Property(e => e.Description).IsFixedLength();
            entity.Property(e => e.Event).IsFixedLength();
        });

        modelBuilder.Entity<Responsible>(entity =>
        {
            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Role__3214EC07BFF7313A");

            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Updated).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<Route>(entity =>
        {
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Tenant__3214EC0704A65DC8");

            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<Token>(entity =>
        {
            entity.HasKey(e => e.IdToken).HasName("PK__Token__D6332447B285E008");

            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.FechaAutenticacion).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.IdUsuarioNavigation).WithMany(p => p.Tokens)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Token__IdUsuario__6CD828CA");
        });

        modelBuilder.Entity<TokenExpirado>(entity =>
        {
            entity.HasKey(e => e.IdToken).HasName("PK__TokenExp__D6332447548AD80A");

            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.IdUsuarioNavigation).WithMany(p => p.TokenExpirados)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TokenExpi__IdUsu__70A8B9AE");
        });

        modelBuilder.Entity<Tracking>(entity =>
        {
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasKey(e => e.IdUsuario).HasName("PK__Usuario__5B65BF978C61825D");

            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Updated).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Role).WithMany(p => p.Usuarios)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Usuario__RoleId__671F4F74");
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<VehicleInfoLocation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__VehicleI__3214EC0724907546");

            entity.Property(e => e.Created).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Manifest).WithMany(p => p.VehicleInfoLocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__VehicleIn__Manif__7A3223E8");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.VehicleInfoLocations)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__VehicleIn__Vehic__793DFFAF");
        });

        modelBuilder.Entity<Zone>(entity =>
        {
            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.ZoneTypeNavigation).WithMany(p => p.Zones)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Zone__ZoneType__503BEA1C");
        });

        modelBuilder.Entity<ZoneType>(entity =>
        {
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
