# **ESTRUCTURA DE SOFTWARE**

# **SERVICIO VEHICLE TRACKING**

|  |  |
| --- | --- |
| **CAPA** | BACKEND |
| **PLATAFORMA** | SERVER – WINDOWS |
| **TIPO** | .NET |

## 1. DESCRIPCIÓN GENERAL

El servicio Vehicle Tracking proporciona una API para realizar el seguimiento y monitoreo de vehículos a través de dos plataformas:
- Detektor GPS
- Simón Movilidad

El sistema permite la autenticación de usuarios, gestión de tokens, y operaciones de tracking en tiempo real para ambos proveedores.

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
VALUES 
('Detektor', '12345'),
('SimonMovilidad', '12345');

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

#### 3.2.1. Detektor GPS Tracking

##### 3.2.1.1. TrackVehicles (Detektor)

Inicia el proceso de tracking para vehículos Detektor.

Acceso: `api/TrackingDetektor/track`  
Formato: JSON  
Servicio: REST / POST  
Autenticación: JWT requerido

###### Headers Requeridos

| **Nombre** | **Descripción** | **Requerido** |
|------------|-----------------|---------------|
| Authorization | Token JWT (Bearer) | Sí |
| IdUsuario | ID del usuario autenticado | Sí |
| Sitio | Identificador del sitio | Sí |
| Clave | Clave de acceso al sitio | Sí |

###### Parámetros de Salida

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
  "mensaje": "Proceso de tracking Detektor",
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

##### 3.2.1.2. GetVehicleStatus (Detektor)

Obtiene el estado actual de un vehículo específico en Detektor.

Acceso: `api/TrackingDetektor/vehicle/{patent}`  
Formato: JSON  
Servicio: REST / GET  
Autenticación: JWT requerido

###### Parámetros de Ruta

| **Nombre** | **Descripción** | **Tipo** | **Requerido** |
|------------|-----------------|----------|---------------|
| patent | Patente del vehículo | String | Sí |

###### Headers Requeridos

| **Nombre** | **Descripción** | **Requerido** |
|------------|-----------------|---------------|
| Authorization | Token JWT (Bearer) | Sí |
| IdUsuario | ID del usuario autenticado | Sí |
| Sitio | Identificador del sitio | Sí |
| Clave | Clave de acceso al sitio | Sí |

###### Parámetros de Salida

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
| resultado.angle | Ángulo de orientación del vehículo | Decimal |

Ejemplo de salida:
```json
{
  "exito": true,
  "mensaje": "Información de vehículo Detektor",
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
    "temperature": 25.5,
    "angle": 45.0
  }
}
```

#### 3.2.2. Simón Movilidad Tracking

##### 3.2.2.1. TrackVehicles (Simón Movilidad)

Inicia el proceso de tracking para vehículos Simón Movilidad.

Acceso: `api/TrackingSimonMovilidad/track`  
Formato: JSON  
Servicio: REST / POST  
Autenticación: JWT requerido

###### Headers Requeridos

| **Nombre** | **Descripción** | **Requerido** |
|------------|-----------------|---------------|
| Authorization | Token JWT (Bearer) | Sí |
| IdUsuario | ID del usuario autenticado | Sí |
| Sitio | Identificador del sitio | Sí |
| Clave | Clave de acceso al sitio | Sí |

###### Parámetros de Salida

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
  "mensaje": "Proceso de tracking Simón Movilidad",
  "detalle": "Se procesaron 5 vehículos en total. Exitosos: 4, Con errores: 1",
  "resultado": {
    "estadisticas": {
      "totalProcesados": 5,
      "procesadosExitosamente": 4,
      "procesadosConError": 1
    },
    "resultados": {
      "lista": [
        {
          "patent": "XYZ789",
          "success": true,
          "message": "Ubicación registrada exitosamente",
          "processedAt": "2024-01-17T11:45:00",
          "latitude": -34.654321,
          "longitude": -58.654321,
          "status": "Procesado"
        }
      ],
      "pagina": 1,
      "totalPaginas": 1,
      "totalRegistros": 5
    }
  }
}
```

##### 3.2.2.2. GetVehicleStatus (Simón Movilidad)

Obtiene el estado actual de un vehículo específico en Simón Movilidad.

Acceso: `api/TrackingSimonMovilidad/vehicle/{patent}`  
Formato: JSON  
Servicio: REST / GET  
Autenticación: JWT requerido

###### Parámetros de Ruta

| **Nombre** | **Descripción** | **Tipo** | **Requerido** |
|------------|-----------------|----------|---------------|
| patent | Patente del vehículo | String | Sí |

###### Headers Requeridos

| **Nombre** | **Descripción** | **Requerido** |
|------------|-----------------|---------------|
| Authorization | Token JWT (Bearer) | Sí |
| IdUsuario | ID del usuario autenticado | Sí |
| Sitio | Identificador del sitio | Sí |
| Clave | Clave de acceso al sitio | Sí |

###### Parámetros de Salida

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
| resultado.angle | Ángulo de orientación del vehículo | Decimal |

Ejemplo de salida:
```json
{
  "exito": true,
  "mensaje": "Información de vehículo Simón Movilidad",
  "detalle": "Información obtenida exitosamente para el vehículo XYZ789",
  "resultado": {
    "latitude": -34.654321,
    "longitude": -58.654321,
    "speed": 45.8,
    "timestamp": "2024-01-17T11:45:00",
    "reason": "En ruta",
    "driver": "María González",
    "georeference": "Ruta 9 km 53",
    "inZone": "Zona Sur",
    "detentionTime": "0",
    "distanceTraveled": 234.7,
    "temperature": 27.3,
    "angle": 180.0
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
      },
      "SimonMovilidad": {
        "Name": "SIMÓN MOVILIDAD",
        "BaseUrl": "https://www.simonmovilidad.com/app/login",
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

### 4.2. Diferencias entre Proveedores

#### 4.2.1. Detektor Security S.A.S
- Interfaz web tradicional basada en frames
- Navegación compleja entre diferentes secciones
- Sistema de popup para información del vehículo
- Información detallada sobre ángulos y ubicación
- Autenticación basada en sesiones web
- Mayor cantidad de datos históricos disponibles
- Mejor precisión en coordenadas geográficas

#### 4.2.2. Simón Movilidad
- Interfaz web moderna basada en React
- Navegación simplificada y directa
- Panel lateral para información del vehículo
- Mapas interactivos con marcadores en tiempo real
- Autenticación basada en tokens
- Actualizaciones más frecuentes de posición
- Mejor rendimiento en tiempo real

### 4.3. Dependencias

El sistema requiere las siguientes dependencias principales:

#### 4.3.1. Dependencias Comunes
- Entity Framework Core 6.0 o superior
- Microsoft.AspNetCore.Authentication.JwtBearer
- Microsoft.EntityFrameworkCore.SqlServer
- NetTopologySuite
- Serilog para logging avanzado

#### 4.3.2. Dependencias Específicas para Detektor
- Selenium.WebDriver 4.8 o superior
- Selenium.Support
- Selenium.WebDriver.ChromeDriver (versión compatible con Chrome instalado)
- DotNetSeleniumExtras.WaitHelpers
- OpenQA.Selenium.Support.UI

#### 4.3.3. Dependencias Específicas para Simón Movilidad
- HtmlAgilityPack
- Newtonsoft.Json
- Microsoft.AspNetCore.SignalR.Client (para actualizaciones en tiempo real)
- Leaflet.js (para mapas interactivos)

### 4.4. Seguridad

#### 4.4.1. Autenticación y Autorización
- JWT (JSON Web Tokens) para autenticación de API
- Tokens con tiempo de expiración configurable
- Validación de IP en cada solicitud
- Registro de intentos de acceso fallidos
- Bloqueo automático después de múltiples intentos fallidos
- Rotación automática de tokens

#### 4.4.2. Almacenamiento Seguro
- Encriptación de credenciales en base de datos
- Separación de tokens activos y expirados
- Limpieza automática de tokens antiguos
- Registro de auditoría completo

#### 4.4.3. Comunicación
- HTTPS obligatorio para todas las comunicaciones
- Certificados SSL/TLS actualizados
- Headers de seguridad configurados
- Prevención de CSRF
- Limitación de rate (throttling)

### 4.5. Manejo de Errores

#### 4.5.1. Códigos de Respuesta HTTP
- 200: Operación exitosa
- 400: Error de validación o solicitud incorrecta
- 401: Error de autenticación
- 403: Error de autorización
- 404: Recurso no encontrado
- 429: Demasiadas solicitudes
- 500: Error interno del servidor
- 503: Error de conectividad con el proveedor de tracking

#### 4.5.2. Tipos de Errores Específicos por Proveedor

##### Detektor Security S.A.S
```json
{
  "exito": false,
  "mensaje": "Error de conexión Detektor",
  "detalle": "Error específico del proveedor",
  "codigoError": "DET_001",
  "errores": {
    "tipo": "CONEXION",
    "descripcion": "No se pudo establecer conexión con el servidor Detektor",
    "recomendacion": "Verificar conectividad y credenciales"
  }
}
```

##### Simón Movilidad
```json
{
  "exito": false,
  "mensaje": "Error de autenticación Simón Movilidad",
  "detalle": "Error específico del proveedor",
  "codigoError": "SM_001",
  "errores": {
    "tipo": "AUTENTICACION",
    "descripcion": "Token de acceso expirado",
    "recomendacion": "Renovar credenciales de acceso"
  }
}
```

### 4.6. Solución de Problemas

#### 4.6.1. Problemas Comunes y Soluciones

##### Detektor Security S.A.S

| Problema | Causa Probable | Solución |
|----------|---------------|-----------|
| Error de frames | Cambios en la estructura del sitio | Actualizar selectores de Selenium |
| Timeout en carga | Red lenta o servidor sobrecargado | Aumentar timeouts en configuración |
| Error de coordenadas | Fallo en extracción del mapa | Verificar formato de respuesta |
| Sesión inválida | Expiración de cookies | Implementar reintento con nuevas credenciales |
| Error de popup | Bloqueo del navegador | Configurar Chrome para permitir popups |

##### Simón Movilidad

| Problema | Causa Probable | Solución |
|----------|---------------|-----------|
| Token expirado | Tiempo de sesión excedido | Renovar token automáticamente |
| Error de API | Límite de rate excedido | Implementar exponential backoff |
| Mapa no carga | Problema con Leaflet | Verificar carga de recursos externos |
| Datos desactualizados | Caché del navegador | Forzar recarga de datos |
| WebSocket cerrado | Timeout de conexión | Reconectar automáticamente |

#### 4.6.2. Logging y Diagnóstico

##### 4.6.2.1. Estructura de Logs
```json
{
  "timestamp": "2024-01-31T10:00:00Z",
  "level": "ERROR",
  "provider": "DETEKTOR",
  "operation": "TRACK_VEHICLE",
  "vehicleId": "ABC123",
  "error": {
    "code": "DET_001",
    "message": "Error de conexión",
    "stackTrace": "...",
    "context": {
      "attempt": 2,
      "lastSuccess": "2024-01-31T09:55:00Z",
      "browserInfo": "Chrome 120.0.0"
    }
  }
}
```

##### 4.6.2.2. Niveles de Log
- DEBUG: Información detallada para desarrollo
- INFO: Operaciones normales del sistema
- WARNING: Situaciones anómalas pero no críticas
- ERROR: Errores que requieren atención
- FATAL: Errores que impiden la operación del sistema

### 4.7. Comparativa Detallada de Proveedores

#### 4.7.1. Rendimiento

| Aspecto | Detektor | Simón Movilidad |
|---------|----------|-----------------|
| Tiempo de respuesta promedio | 2-3 segundos | 0.5-1 segundo |
| Frecuencia de actualización | Cada 5 minutos | Cada 30 segundos |
| Precisión de coordenadas | ±5 metros | ±10 metros |
| Uso de recursos | Alto | Medio |
| Confiabilidad | 99.9% | 99.5% |

#### 4.7.2. Características

| Característica | Detektor | Simón Movilidad |
|----------------|----------|-----------------|
| Histórico de rutas | Completo | Últimas 24h |
| Alertas en tiempo real | Sí | Sí |
| Geofencing | Avanzado | Básico |
| Reportes personalizados | Sí | Limitado |
| API REST | No | Sí |
| Exportación de datos | Múltiples formatos | Solo CSV |

#### 4.7.3. Recomendaciones de Uso

##### Usar Detektor cuando:
- Se requiere máxima precisión en coordenadas
- Es necesario acceder a histórico completo
- Se necesitan reportes detallados
- El monitoreo es crítico para la operación
- Se requieren funciones avanzadas de geofencing

##### Usar Simón Movilidad cuando:
- La prioridad es el tiempo real
- Se necesita una interfaz moderna y responsiva
- El volumen de vehículos es alto
- Se requiere integración vía API
- Los recursos del servidor son limitados

### 4.8. Mejores Prácticas

#### 4.8.1. Implementación
- Usar patrón Repository para acceso a datos
- Implementar caché para reducir llamadas al proveedor
- Manejar reintentos con exponential backoff
- Implementar circuit breaker para llamadas externas
- Mantener logs detallados de operaciones

#### 4.8.2. Monitoreo
- Configurar alertas para errores críticos
- Monitorear tiempo de respuesta de proveedores
- Verificar uso de recursos periódicamente
- Implementar health checks
- Mantener métricas de uso y rendimiento

#### 4.8.3. Mantenimiento
- Actualizar ChromeDriver regularmente
- Revisar y ajustar timeouts según necesidad
- Limpiar logs y datos históricos antiguos
- Mantener documentación actualizada
- Realizar pruebas de carga periódicas
