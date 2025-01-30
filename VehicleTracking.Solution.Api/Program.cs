using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VehicleTracking.Domain.Contracts;
using VehicleTracking.Domain.Services;
using VehicleTracking.Infrastructure;
using VehicleTracking.Shared.InDTO;
using VehicleTracking.Shared.InDTO.InDTOGps;
using VehicleTracking.Solution.Api.Attributes;
using VehicleTracking.Util.Helpers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder =>
        {
            builder
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
});

// Database configuration
builder.Services.AddDbContextFactory<DBContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        x => {
            x.UseNetTopologySuite();
            x.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null
            );
        }
    ));

// JWT Configuration
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// Settings Configuration
builder.Services.Configure<UsuarioSettings>(
   builder.Configuration.GetSection("UsuarioSettings")
);

builder.Services.Configure<TrackingSettings>(
   builder.Configuration.GetSection("TrackingSettings")
);

// Core Services
builder.Services.AddScoped<IAccesoRepository, AccesoRepository>();
builder.Services.AddScoped<ILogRepository, LogRepository>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<ITokenRepository, TokenRepository>();
builder.Services.AddScoped<IConstantesService, ConstantesService>();

// Logging Services
builder.Services.AddSingleton<FileLoggerService>();
builder.Services.AddScoped<IFileLogger, FileLoggerAdapter>();
builder.Services.AddScoped<IRepositoryLogger, LogRepositoryAdapter>();

// Detektor GPS Services
builder.Services.AddScoped<VehicleTracking.Domain.Contracts.IDetektorGps.ILocationScraperFactory>(sp => {
    var fileLogger = sp.GetRequiredService<IFileLogger>();
    var logRepository = sp.GetRequiredService<IRepositoryLogger>();
    var settings = sp.GetRequiredService<IOptions<TrackingSettings>>();
    return new VehicleTracking.Domain.Services.DetektorGps.LocationScraperFactory(
        fileLogger,
        logRepository,
        settings
    );
});

builder.Services.AddScoped<VehicleTracking.Domain.Contracts.IDetektorGps.IVehicleTrackingRepository,
   VehicleTracking.Domain.Services.DetektorGps.VehicleTrackingRepository>();

builder.Services.AddScoped<VehicleTracking.Domain.Contracts.IDetektorGps.ITrackingService>(sp => {
    var factory = sp.GetRequiredService<VehicleTracking.Domain.Contracts.IDetektorGps.ILocationScraperFactory>();
    var repository = sp.GetRequiredService<VehicleTracking.Domain.Contracts.IDetektorGps.IVehicleTrackingRepository>();
    var logRepository = sp.GetRequiredService<ILogRepository>();
    var contextFactory = sp.GetRequiredService<IDbContextFactory<DBContext>>();
    return new TrackingService(
        repository,
        factory,
        logRepository,
        contextFactory
    );
});

// Simon Movilidad GPS Services
builder.Services.AddScoped<VehicleTracking.Domain.Contracts.ISimonMovilidadGps.ILocationScraperFactory>(sp => {
    var fileLogger = sp.GetRequiredService<IFileLogger>();
    var logRepository = sp.GetRequiredService<IRepositoryLogger>();
    var settings = sp.GetRequiredService<IOptions<TrackingSettings>>();
    return new VehicleTracking.Domain.Services.SimonMovilidadGps.LocationScraperFactory(
        fileLogger,
        logRepository,
        settings
    );
});

builder.Services.AddScoped<VehicleTracking.Domain.Contracts.ISimonMovilidadGps.IVehicleTrackingRepository>(sp => {
    var contextFactory = sp.GetRequiredService<IDbContextFactory<DBContext>>();
    var settings = sp.GetRequiredService<IOptions<TrackingSettings>>();
    return new VehicleTracking.Domain.Services.SimonMovilidadGps.VehicleTrackingRepository(
        contextFactory,
        settings
    );
});

builder.Services.AddScoped<VehicleTracking.Domain.Contracts.ISimonMovilidadGps.ITrackingService>(sp => {
    var factory = sp.GetRequiredService<VehicleTracking.Domain.Contracts.ISimonMovilidadGps.ILocationScraperFactory>();
    var repository = sp.GetRequiredService<VehicleTracking.Domain.Contracts.ISimonMovilidadGps.IVehicleTrackingRepository>();
    var logRepository = sp.GetRequiredService<ILogRepository>();
    var contextFactory = sp.GetRequiredService<IDbContextFactory<DBContext>>();
    return new VehicleTracking.Domain.Services.SimonMovilidadGps.TrackingService(
        repository,
        factory,
        logRepository,
        contextFactory
    );
});

// Additional configurations for Selenium
builder.Services.AddHttpClient();

// Configure request execution timeout
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = int.MaxValue;
});

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = int.MaxValue;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
});

// Register filters and attributes
builder.Services.AddScoped<AccesoAttribute>();
builder.Services.AddScoped<AutorizacionJwtAttribute>();
builder.Services.AddScoped<LogAttribute>();
builder.Services.AddScoped<ValidarModeloAttribute>();

// Configure global controller filters
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<LogAttribute>();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.UseExceptionHandler("/Error");
app.UseHsts();

app.MapControllers();
app.Run();
