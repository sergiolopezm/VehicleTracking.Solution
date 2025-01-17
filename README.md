# **ESTRUCTURA DE SOFTWARE**

# **SERVICIO VEHICLE TRACKING**

|  |  |
| --- | --- |
| **CAPA** | BACKEND |
| **PLATAFORMA** | SERVER – WINDOWS |
| **TIPO** | .NET |

## 1. DESCRIPCIÓN GENERAL

El servicio Vehicle Tracking proporciona una API para realizar el seguimiento y monitoreo de vehículos a través de la plataforma Detektor GPS. El sistema permite la autenticación de usuarios, gestión de tokens, y operaciones de tracking en tiempo real.

## 2. REQUISITOS PREVIOS

### 2.1. Estructura de Base de Datos

Para el funcionamiento correcto del sistema, es necesario crear las siguientes tablas en la base de datos:

#### 2.1.1. Tabla Acceso
```sql
CREATE TABLE [dbo].[Acceso] (
    [Id] INT IDENTITY(1,1) PRIMARY KEY,
    [Sitio] VARCHAR(50) NOT NULL,
    [Contraseña] VARCHAR(250) NOT NULL,
    [Active] BIT DEFAULT 1,
    [Created] DATETIME DEFAULT GETUTCDATE()
);
```

#### 2.1.2. Tabla Usuario
```sql
CREATE TABLE [dbo].[Usuario] (
    [IdUsuario] VARCHAR(20) PRIMARY KEY,
    [NombreUsuario] VARCHAR(100) NOT NULL UNIQUE,
    [Contraseña] VARCHAR(50) NOT NULL,
    [Nombre] VARCHAR(100) NOT NULL,
    [Apellido] VARCHAR(100) NOT NULL,
    [Email] VARCHAR(100) NOT NULL UNIQUE,
    [RoleId] INT NOT NULL,
    [Active] BIT DEFAULT 1,
    [Created] DATETIME DEFAULT GETUTCDATE(),
    [Updated] DATETIME DEFAULT GETUTCDATE(),
    FOREIGN KEY (RoleId) REFERENCES [dbo].[Role](Id)
);
```

#### 2.1.3. Tabla Token
```sql
CREATE TABLE [dbo].[Token] (
    [IdToken] VARCHAR(500) PRIMARY KEY,
    [IdUsuario] VARCHAR(20) NOT NULL,
    [Ip] VARCHAR(15) NOT NULL,
    [FechaAutenticacion] DATETIME DEFAULT GETUTCDATE(),
    [FechaExpiracion] DATETIME NOT NULL,
    [Observacion] VARCHAR(200),
    [Active] BIT DEFAULT 1,
    [Created] DATETIME DEFAULT GETUTCDATE(),
    FOREIGN KEY (IdUsuario) REFERENCES Usuario(IdUsuario)
);
```

#### 2.1.4. Tabla TokenExpirado
```sql
CREATE TABLE [dbo].[TokenExpirado] (
    [IdToken] VARCHAR(500) PRIMARY KEY,
    [IdUsuario] VARCHAR(20) NOT NULL,
    [Ip] VARCHAR(15) NOT NULL,
    [FechaAutenticacion] DATETIME NOT NULL,
    [FechaExpiracion] DATETIME NOT NULL,
    [Observacion] VARCHAR(200),
    [Created] DATETIME DEFAULT GETUTCDATE(),
    FOREIGN KEY (IdUsuario) REFERENCES Usuario(IdUsuario)
);
```

#### 2.1.5. Tabla Log
```sql
CREATE TABLE [dbo].[Log] (
    [Id] INT IDENTITY(1,1) PRIMARY KEY,
    [IdUsuario] VARCHAR(50),
    [Fecha] DATETIME DEFAULT GETUTCDATE(),
    [Tipo] VARCHAR(3) NOT NULL,
    [Ip] VARCHAR(15),
    [Accion] VARCHAR(100),
    [Detalle] VARCHAR(5000),
    [Created] DATETIME DEFAULT GETUTCDATE()
);
```

#### 2.1.6. Tabla VehicleInfoLocation
```sql
CREATE TABLE dbo.VehicleInfoLocation (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    VehicleId INT NOT NULL,
    ManifestId INT NOT NULL,
    Latitude DECIMAL(10,8) NOT NULL,
    Longitude DECIMAL(11,8) NOT NULL,
    Speed DECIMAL(10,2) NULL,
    Timestamp DATETIME NOT NULL,
    Provider VARCHAR(50) NOT NULL,
    Created DATETIME NOT NULL DEFAULT GETDATE(),
    IsActive BIT NOT NULL DEFAULT 1,
    Location geography NULL,
    Driver NVARCHAR(100) NULL,
    Georeference NVARCHAR(1000) NULL,
    InZone NVARCHAR(100) NULL,
    DetentionTime NVARCHAR(50) NULL,
    DistanceTraveled DECIMAL(18,2) NULL,
    Temperature DECIMAL(18,2) NULL,
    Reason NVARCHAR(200) NULL,
    FOREIGN KEY (VehicleId) REFERENCES dbo.Vehicle(Id),
    FOREIGN KEY (ManifestId) REFERENCES dbo.Manifest(Id)
);
```

### 2.2. Datos Iniciales

Es necesario insertar los siguientes registros iniciales:

```sql
INSERT INTO [dbo].[Acceso] ([Sitio], [Contraseña])
VALUES ('Detektor', '12345');

INSERT INTO [dbo].[Usuario] (
    [IdUsuario], [NombreUsuario], [Contraseña], [Nombre], [Apellido], 
    [Email], [RoleId], [Active]
)
VALUES (
    'ADMIN000001', 'admin', 'admin123', 'Administrador', 'Sistema',
    'admin@vehicletracking.com', 1, 1
);
```

## 3. MÉTODOS

### 3.1. Autenticación

#### 3.1.1. Login

Autentica un usuario en el sistema.

Acceso: `api/Auth/login`  
Formato: JSON  
Servicio: REST / POST

##### 3.1.1.1. Parámetros de Entrada

| **Nombre** | **Descripción** | **Tipo** | **Requerido** |
|------------|-----------------|----------|---------------|
| nombreUsuario | Nombre de usuario | String | Sí |
| contraseña | Contraseña del usuario | String | Sí |
| ip | Dirección IP del cliente | String | No |

Ejemplo de entrada:
```json
{
  "nombreUsuario": "admin",
  "contraseña": "admin123",
  "ip": "192.168.1.1"
}
```

##### 3.1.1.2. Parámetros de Salida

| **Nombre** | **Descripción** | **Tipo** |
|------------|-----------------|-----------|
| exito | Indica si la operación fue exitosa | Boolean |
| mensaje | Mensaje general de la operación | String |
| detalle | Descripción detallada del resultado | String |
| resultado | Objeto con datos del usuario autenticado | Object |
| resultado.idUsuario | Identificador único del usuario | String |
| resultado.nombreUsuario | Nombre de usuario | String |
| resultado.nombre | Nombre real del usuario | String |
| resultado.apellido | Apellido del usuario | String |
| resultado.email | Correo electrónico del usuario | String |
| resultado.rol | Rol del usuario en el sistema | String |
| resultado.token | Token JWT para autenticación | String |

Ejemplo de salida:
```json
{
  "exito": true,
  "mensaje": "Usuario autenticado",
  "detalle": "El usuario admin se ha autenticado correctamente.",
  "resultado": {
    "idUsuario": "ADMIN000001",
    "nombreUsuario": "admin",
    "nombre": "Administrador",
    "apellido": "Sistema",
    "email": "admin@vehicletracking.com",
    "rol": "Administrador",
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
  }
}
```

### 3.2. Tracking de Vehículos

#### 3.2.1. TrackVehicles

Inicia el proceso de tracking para todos los vehículos activos.

Acceso: `api/Tracking/track`  
Formato: JSON  
Servicio: REST / POST  
Autenticación: JWT requerido

##### 3.2.1.1. Headers Requeridos

| **Nombre** | **Descripción** | **Requerido** |
|------------|-----------------|---------------|
| Authorization | Token JWT (Bearer) | Sí |
| IdUsuario | ID del usuario autenticado | Sí |

##### 3.2.1.2. Parámetros de Salida

| **Nombre** | **Descripción** | **Tipo** |
|------------|-----------------|-----------|
| exito | Indica si la operación fue exitosa | Boolean |
| mensaje | Mensaje general de la operación | String |
| detalle | Descripción detallada del resultado | String |
| resultado | Objeto con resultados del tracking | Object |
| resultado.estadisticas | Resumen estadístico del proceso | Object |
| resultado.estadisticas.totalProcesados | Total de vehículos procesados | Integer |
| resultado.estadisticas.procesadosExitosamente | Cantidad de procesamientos exitosos | Integer |
| resultado.estadisticas.procesadosConError | Cantidad de procesamientos fallidos | Integer |
| resultado.resultados | Objeto con lista paginada de resultados | Object |
| resultado.resultados.lista | Lista de resultados individuales | Array |
| resultado.resultados.lista[].patent | Patente del vehículo | String |
| resultado.resultados.lista[].success | Indicador de éxito individual | Boolean |
| resultado.resultados.lista[].message | Mensaje del resultado individual | String |
| resultado.resultados.lista[].processedAt | Fecha y hora del procesamiento | DateTime |
| resultado.resultados.lista[].latitude | Latitud registrada | Decimal |
| resultado.resultados.lista[].longitude | Longitud registrada | Decimal |
| resultado.resultados.lista[].status | Estado del procesamiento | String |
| resultado.resultados.pagina | Número de página actual | Integer |
| resultado.resultados.totalPaginas | Total de páginas disponibles | Integer |
| resultado.resultados.totalRegistros | Total de registros procesados | Integer |

Ejemplo de salida:
```json
{
  "exito": true,
  "mensaje": "Proceso de tracking",
  "detalle": "Se procesaron 10 vehículos en total. Exitosos: 8, Con errores: 2",
  "resultado": {
    "estadisticas": {
      "totalProcesados": 10,
      "procesadosExitosamente": 8,
      "procesadosConError": 2
    },
    "resultados": {
      "lista": [
        {
          "patent": "ABC123",
          "success": true,
          "message": "Ubicación registrada exitosamente",
          "processedAt": "2024-01-17T10:30:00",
          "latitude": -34.123456,
          "longitude": -58.123456,
          "status": "Procesado"
        }
      ],
      "pagina": 1,
      "totalPaginas": 1,
      "totalRegistros": 10
    }
  }
}
```

#### 3.2.2. GetVehicleStatus

Obtiene el estado actual de un vehículo específico.

Acceso: `api/Tracking/vehicle/{patent}`  
Formato: JSON  
Servicio: REST / GET  
Autenticación: JWT requerido

##### 3.2.2.1. Parámetros de Ruta

| **Nombre** | **Descripción** | **Tipo** | **Requerido** |
|------------|-----------------|----------|---------------|
| patent | Patente del vehículo | String | Sí |

##### 3.2.2.2. Headers Requeridos

| **Nombre** | **Descripción** | **Requerido** |
|------------|-----------------|---------------|
| Authorization | Token JWT (Bearer) | Sí |
| IdUsuario | ID del usuario autenticado | Sí |

##### 3.2.2.3. Parámetros de Salida

| **Nombre** | **Descripción** | **Tipo** |
|------------|-----------------|-----------|
| exito | Indica si la operación fue exitosa | Boolean |
| mensaje | Mensaje general de la operación | String |
| detalle | Descripción detallada del resultado | String |
| resultado | Objeto con información del vehículo | Object |
| resultado.latitude | Latitud actual del vehículo | Decimal |
| resultado.longitude | Longitud actual del vehículo | Decimal |
| resultado.speed | Velocidad actual en km/h | Decimal |
| resultado.timestamp | Fecha y hora de la última actualización | DateTime |
| resultado.reason | Motivo del último estado registrado | String |
| resultado.driver | Nombre del conductor | String |
| resultado.georeference | Referencia geográfica actual | String |
| resultado.inZone | Zona actual del vehículo | String |
| resultado.detentionTime | Tiempo de detención (si aplica) | String |
| resultado.distanceTraveled | Distancia recorrida en km | Decimal |
| resultado.temperature | Temperatura registrada | Decimal |

Ejemplo de salida:
```json
{
  "exito": true,
  "mensaje": "Información de vehículo",
  "detalle": "Información obtenida exitosamente para el vehículo ABC123",
  "resultado": {
    "latitude": -34.123456,
    "longitude": -58.123456,
    "speed": 60.5,
    "timestamp": "2024-01-17T10:30:00",
    "reason": "En movimiento",
    "driver": "Juan Pérez",
    "georeference": "Av. Principal 123",
    "inZone": "Zona Norte",
    "detentionTime": "0",
    "distanceTraveled": 150.5,
    "temperature": 25.5
  }
}
```

## 4. CONSIDERACIONES TÉCNICAS

### 4.1. Configuración

El sistema requiere la siguiente configuración en el archivo appsettings.json:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=SERVER;Initial Catalog=DB;Integrated Security=True;"
  },
  "JwtSettings": {
    "Key": "YOUR_SECRET_KEY",
    "TiempoExpiracionMinutos": 540,
    "TiempoExpiracionBDMinutos": 240
  },
  "TrackingSettings": {
    "Providers": {
      "Detektor": {
        "Name": "DETEKTOR SECURITY S.A.S",
        "BaseUrl": "https://co.tracking.detektorgps.com/AppEboras/login_co/login_co.html#login",
        "PollingIntervalSeconds": 300,
        "MaxRetryAttempts": 3,
        "TimeoutSeconds": 60
      }
    },
    "Selenium": {
      "ChromeDriverPath": "",
      "Headless": true,
      "WindowSize": "1920,1080",
      "ImplicitWaitSeconds": 10
    }
  }
}
```

### 4.2. Dependencias

El sistema requiere las siguientes dependencias principales:

- Selenium WebDriver
- ChromeDriver
- Entity Framework Core
- NetTopologySuite
- JWT Bearer Authentication

### 4.3. Seguridad

- Todas las operaciones requieren autenticación mediante JWT
- Los tokens tienen una validez limitada configurada en JwtSettings
- Se registran todas las operaciones en la tabla de Logs
- Se implementa manejo de tokens expirados
- Se valida la IP del cliente en cada operación

### 4.4. Manejo de Errores

El sistema implementa los siguientes códigos de respuesta HTTP:

- 200: Operación exitosa
- 400: Error de validación o solicitud incorrecta
- 401: Error de autenticación
- 403: Error de autorización
- 500: Error interno del servidor
- 503: Error de conectividad con el servidor de tracking

### 4.5. Limitaciones

- El sistema está diseñado para funcionar con Chrome/Chromium
- Requiere acceso a Internet para conectarse al servicio de Detektor
- Las credenciales de Detektor deben estar configuradas en la base de datos
- El servicio debe ejecutarse en un ambiente Windows

## 5. SOLUCIÓN DE PROBLEMAS

### 5.1. Problemas Comunes

1. Error de ChromeDriver:
   - Verificar que ChromeDriver está instalado y accesible
   - Comprobar compatibilidad de versiones con Chrome

2. Error de autenticación:
   - Verificar credenciales en tabla Acceso
   - Comprobar formato del token JWT

3. Error de tracking:
   - Verificar conectividad con Detektor
   - Comprobar estado de las credenciales de vehículos
   - Revisar logs para detalles específicos

### 5.2. Logs

El sistema implementa logging en dos niveles:

1. Archivo:
   - Ubicación: Carpeta Logs en la raíz del proyecto
   - Formato: {fecha}.txt
   - Contiene detalles técnicos y trazas de error

2. Base de datos:
   - Tabla: Log
   - Registra operaciones de negocio y errores críticos
   - Permite seguimiento de auditoría
