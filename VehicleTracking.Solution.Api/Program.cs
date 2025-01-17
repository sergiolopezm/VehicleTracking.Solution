using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VehicleTracking.Domain.Contracts;
using VehicleTracking.Domain.Contracts.IDetektorGps;
using VehicleTracking.Domain.Services;
using VehicleTracking.Domain.Services.DetektorGps;
using VehicleTracking.Infrastructure;
using VehicleTracking.Shared.InDTO;
using VehicleTracking.Shared.InDTO.DetektorGps;
using VehicleTracking.Solution.Api.Attributes;
using VehicleTracking.Util.Helpers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configuración de CORS
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

// Configuración de la base de datos
builder.Services.AddDbContextFactory<DBContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        x => x.UseNetTopologySuite()
    ));

// Configuración de JWT
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

// Configuración de settings
builder.Services.Configure<UsuarioSettings>(
    builder.Configuration.GetSection("UsuarioSettings")
);

builder.Services.Configure<TrackingSettings>(
    builder.Configuration.GetSection("TrackingSettings")
);

// Registro de servicios principales
builder.Services.AddScoped<IAccesoRepository, AccesoRepository>();
builder.Services.AddScoped<ILogRepository, LogRepository>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<ITokenRepository, TokenRepository>();
builder.Services.AddScoped<IConstantesService, ConstantesService>();

// Registro de servicios de logging
builder.Services.AddSingleton<FileLoggerService>();
builder.Services.AddScoped<IFileLogger, FileLoggerAdapter>();
builder.Services.AddScoped<IRepositoryLogger, LogRepositoryAdapter>();

// Registro de servicios de tracking
builder.Services.AddScoped<ILocationScraperFactory, LocationScraperFactory>();
builder.Services.AddScoped<IVehicleTrackingRepository, VehicleTrackingRepository>();
builder.Services.AddScoped<ITrackingService, TrackingService>();

// Configuraciones adicionales para Selenium
builder.Services.AddHttpClient();

// Configuración del tiempo máximo de ejecución para las solicitudes
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = int.MaxValue;
});

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = int.MaxValue;
});

// Registro de filtros y atributos
builder.Services.AddScoped<AccesoAttribute>();
builder.Services.AddScoped<AutorizacionJwtAttribute>();
builder.Services.AddScoped<LogAttribute>();
builder.Services.AddScoped<ValidarModeloAttribute>();

// Configuración de filtros globales en los controladores
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<LogAttribute>();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Middleware pipeline
app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// Manejo global de excepciones
app.UseExceptionHandler("/Error");
app.UseHsts();

app.MapControllers();

app.Run();