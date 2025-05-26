using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using VehicleTracking.Domain.Contracts.ISatrackGps;
using VehicleTracking.Shared.InDTO.InDTOGps;
using VehicleTracking.Util.Helpers;
using VehicleTracking.Utils.Helpers;

namespace VehicleTracking.Domain.Scraping.SatrackGps
{
    public class SatrackGpsScraper : ILocationScraper
    {
        private readonly IWebDriver _driver;
        private readonly WebDriverWait _wait;
        private readonly ScrapingLogger _logger;
        private readonly ProviderConfig _config;
        private readonly SeleniumConfig _seleniumConfig;
        private bool _isLoggedIn;
        private string _currentPatent;

        public SatrackGpsScraper(
            IFileLogger fileLogger,
            IRepositoryLogger logRepository,
            IOptions<TrackingSettings> settings,
            string userId,
            string ip)
        {
            _config = settings.Value.Providers.Satrack;
            _seleniumConfig = settings.Value.Selenium;
            _logger = new ScrapingLogger(fileLogger, logRepository, userId, ip, "SatrackScrapingGPS");

            try
            {
                var options = new ChromeOptions();
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");
                options.AddArgument("--disable-gpu");
                options.AddArgument($"--window-size={_seleniumConfig.WindowSize}");
                options.AddArgument("--disable-notifications");
                options.AddArgument("--disable-popup-blocking");
                options.AddArgument("--disable-features=PasswordManagerLeakDetection,PasswordElement,PasswordProtectionWarning,AutofillServerCommunication");
                options.AddArgument("--disable-blink-features=CredentialManagerAPI"); // Evita autocompletado interno
                options.AddUserProfilePreference("safebrowsing.enabled", false);      // Desactiva Password Leak Detection
                options.AddArgument("--guest");
                options.AddUserProfilePreference("credentials_enable_service", false);
                options.AddUserProfilePreference("profile.password_manager_enabled", false);

                // COMENTAR O ELIMINAR ESTA CONDICIÓN PARA FORZAR MODO VISIBLE
                //if (_seleniumConfig.Headless)
                //{
                //    options.AddArgument("--headless");
                //}

                var chromeDriverService = string.IsNullOrEmpty(_seleniumConfig.ChromeDriverPath)
                    ? ChromeDriverService.CreateDefaultService()
                    : ChromeDriverService.CreateDefaultService(_seleniumConfig.ChromeDriverPath);

                chromeDriverService.HideCommandPromptWindow = true;

                _driver = new ChromeDriver(chromeDriverService, options);

                // AÑADIR ESTAS LÍNEAS PARA MAXIMIZAR LA VENTANA Y CONFIGURAR TIMEOUTS
                _driver.Manage().Window.Maximize();
                _driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;

                _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(_config.TimeoutSeconds));
                _isLoggedIn = false;

                _logger.Info("Inicialización del ChromeDriver completada exitosamente", true);
            }
            catch (Exception ex)
            {
                _logger.Error("Error inicializando ChromeDriver", ex);
                throw new InvalidOperationException("Error inicializando ChromeDriver", ex);
            }
        }

        public async Task<bool> LoginAsync(string username, string password, string patent)
        {
            try
            {
                // Limpiar espacios en blanco de las credenciales
                username = username?.Trim() ?? string.Empty;
                password = password?.Trim() ?? string.Empty;

                // Validar que las credenciales no estén vacías
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    _logger.Warning($"Credenciales inválidas para el vehículo {patent}: usuario o contraseña están vacíos", true);
                    return false;
                }

                _logger.Debug($"Iniciando proceso de login para vehículo {patent}");
                var dynamicWait = new DynamicWaitHelper(_driver);

                _logger.Debug("Navegando a la URL base...");
                _driver.Navigate().GoToUrl(_config.BaseUrl);

                await CheckPageStatus("navegación inicial");

                _logger.Debug("Esperando que la página cargue completamente...");
                await dynamicWait.WaitForPageLoadAsync();

                // Esperar y buscar el campo de usuario
                _logger.Debug("Buscando campo de usuario...");
                // Para el campo de usuario
                var (userInput, userError) = await dynamicWait.WaitForElementAsync(
                    By.Id("txt_login_username"),  // Cambiado a usar ID
                    "login_username",
                    ensureClickable: true
                );

                if (userInput == null)
                {
                    _logger.Warning($"No se pudo encontrar el campo de usuario para el vehículo {patent}. Detalles del error: {userError}", true);
                    return false;
                }

                // Esperar y buscar el campo de contraseña
                _logger.Debug("Buscando campo de contraseña...");
                // Para el campo de contraseña
                var (passInput, passError) = await dynamicWait.WaitForElementAsync(
                    By.Id("txt_login_password"),  // Cambiado a usar ID
                    "login_password",
                    ensureClickable: true
                );

                if (passInput == null)
                {
                    _logger.Warning($"No se pudo encontrar el campo de contraseña para el vehículo {patent}. Detalles del error: {passError}", true);
                    return false;
                }

                // Buscar el botón de login
                _logger.Debug("Buscando botón de login...");
                // Para el botón de login
                var (loginButton, buttonError) = await dynamicWait.WaitForElementAsync(
                    By.Id("btn_login_login"),  // Cambiado a usar ID
                    "login_button",
                    ensureClickable: true
                );

                if (loginButton == null)
                {
                    _logger.Warning($"No se pudo encontrar el botón de inicio de sesión para el vehículo {patent}. Detalles del error: {buttonError}", true);
                    return false;
                }

                _logger.Debug("Limpiando y llenando campos del formulario...");
                userInput.Clear();
                passInput.Clear();

                await Task.WhenAll(
                    Task.Run(() => userInput.SendKeys(username)),
                    Task.Run(() => passInput.SendKeys(password))
                );

                // Verificar estado antes de intentar el login
                await CheckPageStatus("pre-login");

                _logger.Debug("Intentando hacer clic en el botón de login...");
                await ClickWhenClickableAsync(By.CssSelector("button#iniciarSesion"), cachedElement: loginButton);

                // Manejar posible popup de contraseña de Chrome
                await HandleChromePasswordWarningIfPresent();

                // Verificar si hay errores de login visibles
                try
                {
                    var errorElements = _driver.FindElements(By.CssSelector(".alert-danger, .error-message, #errorMsg, .login-error"));
                    var visibleError = errorElements.FirstOrDefault(e => e.Displayed);
                    if (visibleError != null)
                    {
                        _logger.Warning($"Error de login detectado: {visibleError.Text}", true);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning("Error al buscar mensajes de error de login");
                }

                // Verificar estado después del login
                await CheckPageStatus("post-login");

                _logger.Debug("Login ejecutado, esperando redirección a página principal...");
                await dynamicWait.WaitForPageLoadAsync();
                await dynamicWait.WaitForAjaxCompletionAsync();

                // Verificar si la redirección fue exitosa
                var loginSuccess = await dynamicWait.WaitForConditionAsync(d => {
                    try
                    {
                        // Verificar si hay elementos que indican login exitoso
                        // Posibles indicadores: mapa, menú lateral, controles de usuario, etc.
                        var menuItems = d.FindElements(By.CssSelector(".leaflet-container, .sidebar-menu, .user-panel"));
                        return menuItems.Any(e => e.Displayed);
                    }
                    catch
                    {
                        return false;
                    }
                }, "login_verification", TimeSpan.FromSeconds(10));

                if (loginSuccess)
                {
                    _logger.Info("Login exitoso verificado, usuario autenticado correctamente", true);
                    _isLoggedIn = true;
                    return true;
                }

                // Si llegamos aquí, intentar una verificación alternativa por URL
                var currentUrl = _driver.Url.ToLower();
                if (currentUrl.Contains("/map") || currentUrl.Contains("/dashboard") || !currentUrl.Contains("/login"))
                {
                    _logger.Info("Login verificado por URL, usuario autenticado correctamente", true);
                    _isLoggedIn = true;
                    return true;
                }

                _logger.Warning("No se pudo verificar el login exitoso", true);
                return false;
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("SERVIDOR_CAIDO:"))
            {
                _logger.Error($"Servidor caído detectado durante el login", ex);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error durante el proceso de login para usuario: {username} y vehículo {patent}", ex);
                return false;
            }
        }

        public async Task<LocationDataInfo> GetVehicleLocationAsync(string patent)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                _currentPatent = patent;

                if (!_isLoggedIn)
                    throw new InvalidOperationException("No se ha iniciado sesión");

                // Verificar el estado de la página antes de iniciar el proceso
                await CheckPageStatus("inicio de búsqueda de vehículo");

                var dynamicWait = new DynamicWaitHelper(_driver);

                _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Iniciando búsqueda de vehículo {patent}");

                // Navegar a la sección de vehículos/mapa si es necesario
                await NavigateToVehiclesSection(dynamicWait);

                // Buscar el vehículo específico por su patente
                var vehicleElement = await FindVehicleInList(patent, dynamicWait);
                if (vehicleElement == null)
                {
                    throw new InvalidOperationException($"CONFIGURACION_INVALIDA: No se encontró el vehículo con placa {patent}");
                }

                // Obtener coordenadas y otros datos del vehículo
                var locationInfo = await ExtractVehicleInformation(vehicleElement, dynamicWait);

                _logger.Info($"[Tiempo TOTAL del proceso: {stopwatch.ElapsedMilliseconds}ms] Proceso completado exitosamente", true);
                _logger.Info($"Coordenadas obtenidas: {locationInfo.Latitude}, {locationInfo.Longitude}", true);

                return locationInfo;
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("SERVIDOR_CAIDO:"))
            {
                _logger.Error($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Servidor caído detectado al obtener ubicación del vehículo {patent}", ex);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Error al obtener ubicación del vehículo {patent}", ex);
                throw new InvalidOperationException($"Error obteniendo ubicación del vehículo: {ex.Message}", ex);
            }
        }

        private async Task NavigateToVehiclesSection(DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug("Iniciando navegación a sección de vehículos/mapa");

                // Verificar si ya estamos en la sección de mapa
                var alreadyInMapSection = await dynamicWait.WaitForConditionAsync(d => {
                    try
                    {
                        return d.FindElements(By.CssSelector(".leaflet-container")).Any(e => e.Displayed);
                    }
                    catch
                    {
                        return false;
                    }
                }, "map_section_check", TimeSpan.FromMilliseconds(500));

                if (alreadyInMapSection)
                {
                    _logger.Debug("Ya estamos en la sección de mapa, no es necesario navegar");
                    return;
                }

                // Buscar y hacer clic en el botón/enlace de mapa o vehículos
                var (mapButton, mapButtonError) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector("a[href*='/map'], a[href*='/vehicles'], button[data-target='map']"),
                    "map_button",
                    ensureClickable: true
                );

                if (mapButton == null)
                {
                    _logger.Warning($"No se pudo encontrar el botón de mapa/vehículos. Detalles del error: {mapButtonError}", true);
                    throw new InvalidOperationException("No se pudo encontrar el botón de mapa/vehículos");
                }

                // Hacer clic en el botón de mapa
                await ClickWhenClickableAsync(By.CssSelector("a[href*='/map'], a[href*='/vehicles'], button[data-target='map']"), cachedElement: mapButton);

                // Esperar a que la página del mapa cargue
                var mapLoaded = await dynamicWait.WaitForConditionAsync(d => {
                    try
                    {
                        return d.FindElements(By.CssSelector(".leaflet-container")).Any(e => e.Displayed);
                    }
                    catch
                    {
                        return false;
                    }
                }, "map_loaded", TimeSpan.FromSeconds(10));

                if (!mapLoaded)
                {
                    throw new InvalidOperationException("La sección del mapa no cargó correctamente");
                }

                // Verificar que el mapa esté completamente cargado esperando los marcadores
                var markersLoaded = await dynamicWait.WaitForConditionAsync(d => {
                    try
                    {
                        return d.FindElements(By.CssSelector(".leaflet-marker-icon, .vehicle-marker, .marker-cluster")).Any(e => e.Displayed);
                    }
                    catch
                    {
                        return false;
                    }
                }, "map_markers_loaded", TimeSpan.FromSeconds(5));

                if (!markersLoaded)
                {
                    _logger.Warning("Los marcadores del mapa no cargaron completamente", true);
                }

                _logger.Info("Navegación a sección de vehículos/mapa completada exitosamente", true);
            }
            catch (Exception ex)
            {
                _logger.Error("Error en NavigateToVehiclesSection", ex);
                throw;
            }
        }

        private async Task<IWebElement?> FindVehicleInList(string patent, DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug($"Iniciando búsqueda del vehículo con placa {patent}");

                // Verificar si hay un buscador de vehículos y usarlo si existe
                var (searchInput, _) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector("input[placeholder*='Buscar'], input[aria-label*='buscar']"),
                    "search_input",
                    ensureClickable: true
                );

                if (searchInput != null)
                {
                    _logger.Debug("Campo de búsqueda encontrado, intentando buscar el vehículo");
                    searchInput.Clear();
                    searchInput.SendKeys(patent);
                    searchInput.SendKeys(Keys.Enter);

                    // Esperar a que se filtre la búsqueda
                    await dynamicWait.WaitForConditionAsync(
                        d => !d.FindElements(By.CssSelector(".loading, .spinner, .wait")).Any(e => e.Displayed),
                        "search_filter_complete",
                        TimeSpan.FromSeconds(5)
                    );
                }

                // Intentar encontrar el vehículo en el mapa o en la lista
                var vehicleFound = await FindVehicleByPatent(patent, dynamicWait);
                if (vehicleFound != null)
                {
                    _logger.Info($"Vehículo {patent} encontrado exitosamente", true);
                    return vehicleFound;
                }

                // Si no se encuentra el vehículo, comprobar si hay una lista desplegable de vehículos
                var (vehiclesList, _) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector("select[id*='vehicle'], select[name*='vehicle'], button[aria-label*='vehículo']"),
                    "vehicles_dropdown",
                    ensureClickable: true
                );

                if (vehiclesList != null)
                {
                    _logger.Debug("Lista desplegable de vehículos encontrada, intentando seleccionar vehículo");
                    await ClickWhenClickableAsync(By.CssSelector("select[id*='vehicle'], select[name*='vehicle'], button[aria-label*='vehículo']"), cachedElement: vehiclesList);

                    // Buscar y seleccionar el vehículo en la lista desplegable
                    var (vehicleOption, _) = await dynamicWait.WaitForElementAsync(
                        By.XPath($"//option[contains(text(), '{patent}')] | //li[contains(text(), '{patent}')]"),
                        "vehicle_option",
                        ensureClickable: true
                    );

                    if (vehicleOption != null)
                    {
                        await ClickWhenClickableAsync(By.XPath($"//option[contains(text(), '{patent}')] | //li[contains(text(), '{patent}')]"), cachedElement: vehicleOption);

                        // Esperar a que se actualice la visualización
                        await dynamicWait.WaitForConditionAsync(
                            d => !d.FindElements(By.CssSelector(".loading, .spinner, .wait")).Any(e => e.Displayed),
                            "vehicle_selection_complete",
                            TimeSpan.FromSeconds(5)
                        );

                        // Intentar encontrar nuevamente el vehículo seleccionado
                        vehicleFound = await FindVehicleByPatent(patent, dynamicWait);
                        if (vehicleFound != null)
                        {
                            _logger.Info($"Vehículo {patent} seleccionado exitosamente de la lista desplegable", true);
                            return vehicleFound;
                        }
                    }
                }

                _logger.Warning($"No se pudo encontrar el vehículo con placa {patent}", true);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error buscando vehículo {patent}", ex);
                throw;
            }
        }

        private async Task<IWebElement?> FindVehicleByPatent(string patent, DynamicWaitHelper dynamicWait)
        {
            try
            {
                // Buscar en marcadores de mapa
                var (mapMarker, _) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector(".leaflet-marker-icon[title*='" + patent + "'], .vehicle-marker[data-plate='" + patent + "']"),
                    "map_marker",
                    ensureClickable: true
                );

                if (mapMarker != null)
                {
                    return mapMarker;
                }

                // Buscar en la lista de vehículos
                var (listItem, _) = await dynamicWait.WaitForElementAsync(
                    By.XPath($"//tr[contains(., '{patent}')] | //div[contains(@class, 'vehicle-item')][contains(., '{patent}')]"),
                    "list_item",
                    ensureClickable: true
                );

                if (listItem != null)
                {
                    return listItem;
                }

                // Usar JavaScript para buscar elementos que contengan la patente
                var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    function findVehicleElement(patent) {
                        // Buscar en elementos del DOM que contengan la patente
                        const elements = Array.from(document.querySelectorAll('*'));
                        for (const element of elements) {
                            if (element.textContent && element.textContent.includes(patent) && 
                                element.offsetWidth > 0 && element.offsetHeight > 0) {
                                // Verificar que es un elemento interactivo
                                const tag = element.tagName.toLowerCase();
                                if (tag === 'button' || tag === 'a' || tag === 'tr' || tag === 'div') {
                                    return element;
                                }
                                // Buscar en los padres cercanos
                                let parent = element.parentElement;
                                for (let i = 0; i < 3 && parent; i++) {
                                    if (parent.tagName.toLowerCase() === 'button' || 
                                        parent.tagName.toLowerCase() === 'a' || 
                                        parent.tagName.toLowerCase() === 'tr' || 
                                        (parent.tagName.toLowerCase() === 'div' && 
                                         (parent.className.includes('item') || 
                                          parent.className.includes('vehicle') || 
                                          parent.className.includes('marker')))) {
                                        return parent;
                                    }
                                    parent = parent.parentElement;
                                }
                            }
                        }
                        return null;
                    }
                    return findVehicleElement(arguments[0]);
                ", patent);

                if (jsResult != null)
                {
                    return (IWebElement)jsResult;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error en FindVehicleByPatent: {ex.Message}");
                return null;
            }
        }

        private async Task<LocationDataInfo> ExtractVehicleInformation(IWebElement vehicleElement, DynamicWaitHelper dynamicWait)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Iniciando extracción de información del vehículo");

                // Hacer clic en el elemento del vehículo para mostrar detalles
                await ClickWhenClickableAsync(By.Id(vehicleElement.GetAttribute("id") ?? ""), cachedElement: vehicleElement);

                // Esperar a que aparezca el panel de información
                var (infoPanel, _) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector(".vehicle-info-panel, .vehicle-details, .info-window, .popup-content"),
                    "info_panel",
                    ensureClickable: false
                );

                if (infoPanel == null)
                {
                    // Intentar nuevamente el clic si no aparece el panel
                    await ClickWhenClickableAsync(By.Id(vehicleElement.GetAttribute("id") ?? ""), cachedElement: vehicleElement);

                    (infoPanel, _) = await dynamicWait.WaitForElementAsync(
                        By.CssSelector(".vehicle-info-panel, .vehicle-details, .info-window, .popup-content"),
                        "info_panel_retry",
                        ensureClickable: false
                    );

                    if (infoPanel == null)
                    {
                        throw new InvalidOperationException("No se pudo obtener el panel de información del vehículo");
                    }
                }

                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Panel de información encontrado, extrayendo datos");

                // Extraer coordenadas
                var coordinates = await ExtractCoordinates(dynamicWait);

                // Extraer el resto de la información desde el panel
                var vehicleData = await ExtractVehicleData(infoPanel, dynamicWait);

                // Combinar la información
                var locationInfo = new LocationDataInfo
                {
                    Latitude = coordinates.Latitude,
                    Longitude = coordinates.Longitude,
                    Speed = vehicleData.Speed,
                    Timestamp = vehicleData.Timestamp,
                    Driver = vehicleData.Driver,
                    Georeference = vehicleData.Georeference,
                    InZone = vehicleData.InZone,
                    DetentionTime = vehicleData.DetentionTime,
                    DistanceTraveled = vehicleData.DistanceTraveled,
                    Temperature = vehicleData.Temperature,
                    Angle = vehicleData.Angle,
                    Reason = vehicleData.Reason
                };

                _logger.Info($"[T+{stopwatch.ElapsedMilliseconds}ms] Información extraída exitosamente", true);
                return locationInfo;
            }
            catch (Exception ex)
            {
                _logger.Error($"[T+{stopwatch.ElapsedMilliseconds}ms] Error extrayendo información del vehículo", ex);
                throw;
            }
        }

        private async Task<(decimal Latitude, decimal Longitude)> ExtractCoordinates(DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug("Iniciando extracción de coordenadas");

                // Intentar obtener coordenadas desde la URL o panel
                var coordinates = await TryGetCoordinatesFromPage();
                if (coordinates.HasValue)
                {
                    _logger.Info($"Coordenadas extraídas: {coordinates.Value.Latitude}, {coordinates.Value.Longitude}", true);
                    return coordinates.Value;
                }

                // Si no se pueden obtener de la URL, intentar extraerlas del panel de información
                var (coordinatesText, _) = await dynamicWait.WaitForElementAsync(
                    By.XPath("//div[contains(text(), 'Coord') or contains(text(), 'Latitud') or contains(text(), 'Longitud')]"),
                    "coordinates_text",
                    ensureClickable: false
                );

                if (coordinatesText != null)
                {
                    var coordsText = coordinatesText.Text;
                    var latMatch = Regex.Match(coordsText, @"lat[^0-9-]*(-?\d+\.?\d*)");
                    var lonMatch = Regex.Match(coordsText, @"lon[^0-9-]*(-?\d+\.?\d*)");

                    if (latMatch.Success && lonMatch.Success)
                    {
                        var lat = decimal.Parse(latMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                        var lon = decimal.Parse(lonMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                        _logger.Info($"Coordenadas extraídas del texto: {lat}, {lon}", true);
                        return (lat, lon);
                    }
                }

                // Intentar obtener coordenadas mediante JavaScript desde el mapa
                var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    try {
                        // Buscar objeto mapa Leaflet
                        var map = null;
                        for (var key in window) {
                            if (window[key] && 
                                typeof window[key] === 'object' && 
                                window[key].hasOwnProperty('_leaflet_id')) {
                                map = window[key];
                                break;
                            }
                        }
                        
                        if (!map) return null;
                        
                        // Buscar marcador seleccionado
                        var selectedMarker = null;
                        for (var key in map._layers) {
                            var layer = map._layers[key];
                            if (layer && layer._icon && 
                                (layer._icon.className.includes('selected') || 
                                 layer._icon.className.includes('active') || 
                                 layer._icon.style.zIndex > 1000)) {
                                selectedMarker = layer;
                                break;
                            }
                        }
                        
                        if (selectedMarker && selectedMarker._latlng) {
                            return {
                                lat: selectedMarker._latlng.lat,
                                lng: selectedMarker._latlng.lng
                            };
                        }
                        return null;
                    } catch(e) {
                        console.error('Error:', e);
                        return null;
                    }
                ");

                if (jsResult != null)
                {
                    var resultObj = (Dictionary<string, object>)jsResult;
                    var lat = Convert.ToDecimal(resultObj["lat"]);
                    var lon = Convert.ToDecimal(resultObj["lng"]);
                    _logger.Info($"Coordenadas extraídas con JavaScript: {lat}, {lon}", true);
                    return (lat, lon);
                }

                // Si no se pueden extraer coordenadas, usar valores por defecto
                _logger.Warning("No se pudieron extraer las coordenadas, usando valores por defecto", true);
                return (0, 0);
            }
            catch (Exception ex)
            {
                _logger.Error("Error extrayendo coordenadas", ex);
                return (0, 0);
            }
        }

        private async Task<(decimal Latitude, decimal Longitude)?> TryGetCoordinatesFromPage()
        {
            try
            {
                // Intentar extraer coordenadas de la URL
                var currentUrl = _driver.Url;
                var latMatch = Regex.Match(currentUrl, @"lat=(-?\d+\.?\d*)");
                var lngMatch = Regex.Match(currentUrl, @"lng=(-?\d+\.?\d*)|lon=(-?\d+\.?\d*)");

                if (latMatch.Success && lngMatch.Success)
                {
                    var lat = decimal.Parse(latMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    var lng = decimal.Parse(lngMatch.Groups[1].Value.Length > 0 ? lngMatch.Groups[1].Value : lngMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                    return (lat, lng);
                }

                // Intentar extraer coordenadas de atributos de datos
                var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    var elements = document.querySelectorAll('[data-lat], [data-latitude]');
                    for (var i = 0; i < elements.length; i++) {
                        var el = elements[i];
                        var lat = el.getAttribute('data-lat') || el.getAttribute('data-latitude');
                        var lng = el.getAttribute('data-lng') || el.getAttribute('data-longitude') || el.getAttribute('data-lon');
                        
                        if (lat && lng) {
                            return { lat: parseFloat(lat), lng: parseFloat(lng) };
                        }
                    }
                    return null;
                ");

                if (jsResult != null)
                {
                    var resultObj = (Dictionary<string, object>)jsResult;
                    var lat = Convert.ToDecimal(resultObj["lat"]);
                    var lon = Convert.ToDecimal(resultObj["lng"]);
                    return (lat, lon);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error en TryGetCoordinatesFromPage: {ex.Message}");
                return null;
            }
        }

        private async Task<LocationDataInfo> ExtractVehicleData(IWebElement infoPanel, DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug("Extrayendo datos del vehículo desde el panel de información");

                // Datos a extraer
                decimal speed = 0;
                DateTime timestamp = DateTime.Now;
                string driver = string.Empty;
                string georeference = string.Empty;
                string inZone = string.Empty;
                string detentionTime = string.Empty;
                decimal distanceTraveled = 0;
                decimal temperature = 0;
                decimal angle = 0;
                string reason = string.Empty;

                // Extraer información usando JavaScript para mayor robustez
                var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    function extractData(panel) {
                        const result = {};
                        const text = panel.textContent || '';
                        
                        // Extraer velocidad
                        const speedMatch = text.match(/Velocidad[^\d]*(\d+\.?\d*)/i);
                        if (speedMatch) result.speed = parseFloat(speedMatch[1]);
                        
                        // Extraer fecha/hora
                        const dateMatch = text.match(/Fecha[^:]*:([^]*?)(?:Veloc|\n|$)/i);
                        if (dateMatch) result.timestamp = dateMatch[1].trim();
                        
                        // Extraer conductor
                        const driverMatch = text.match(/Conductor[^:]*:([^]*?)(?:\n|$)/i);
                        if (driverMatch) result.driver = driverMatch[1].trim();
                        
                        // Extraer georeferencia
                        const geoMatch = text.match(/Dirección|Ubicación|Direc[^:]*:([^]*?)(?:\n|$)/i);
                        if (geoMatch) result.georeference = geoMatch[1].trim();
                        
                        // Extraer zona
                        const zoneMatch = text.match(/Zona|Área|Area[^:]*:([^]*?)(?:\n|$)/i);
                        if (zoneMatch) result.inZone = zoneMatch[1].trim();
                        
                        // Extraer tiempo de detención
                        const detentionMatch = text.match(/Detenido|Parada|Detención[^:]*:([^]*?)(?:\n|$)/i);
                        if (detentionMatch) result.detentionTime = detentionMatch[1].trim();
                        
                        // Extraer distancia recorrida
                        const distanceMatch = text.match(/Distancia|Recorrido|Odómetro[^:]*:([^]*?)(?:km|\n|$)/i);
                        if (distanceMatch) result.distanceTraveled = parseFloat(distanceMatch[1].trim().replace(',', '.'));
                        
                        // Extraer temperatura
                        const tempMatch = text.match(/Temperatura|Temp[^:]*:([^]*?)(?:°C|\n|$)/i);
                        if (tempMatch) result.temperature = parseFloat(tempMatch[1].trim().replace(',', '.'));
                        
                        // Extraer ángulo
                        const angleMatch = text.match(/Ángulo|Angulo|Rumbo|Heading[^:]*:([^]*?)(?:°|\n|$)/i);
                        if (angleMatch) result.angle = parseFloat(angleMatch[1].trim().replace(',', '.'));
                        
                        // Extraer estado/evento/motivo
                        const reasonMatch = text.match(/Estado|Evento|Motivo|Status[^:]*:([^]*?)(?:\n|$)/i);
                        if (reasonMatch) result.reason = reasonMatch[1].trim();
                        
                        return result;
                    }
                    return extractData(arguments[0]);
                ", infoPanel);

                if (jsResult != null)
                {
                    var data = (Dictionary<string, object>)jsResult;

                    // Procesar los datos extraídos
                    if (data.ContainsKey("speed") && data["speed"] != null)
                        speed = Convert.ToDecimal(data["speed"]);

                    if (data.ContainsKey("timestamp") && data["timestamp"] != null)
                    {
                        var timestampStr = data["timestamp"].ToString();
                        if (DateTime.TryParse(timestampStr, out var parsedDate))
                            timestamp = parsedDate;
                    }

                    if (data.ContainsKey("driver") && data["driver"] != null)
                        driver = data["driver"].ToString() ?? string.Empty;

                    if (data.ContainsKey("georeference") && data["georeference"] != null)
                        georeference = data["georeference"].ToString() ?? string.Empty;

                    if (data.ContainsKey("inZone") && data["inZone"] != null)
                        inZone = data["inZone"].ToString() ?? string.Empty;

                    if (data.ContainsKey("detentionTime") && data["detentionTime"] != null)
                        detentionTime = data["detentionTime"].ToString() ?? string.Empty;

                    if (data.ContainsKey("distanceTraveled") && data["distanceTraveled"] != null)
                        distanceTraveled = Convert.ToDecimal(data["distanceTraveled"]);

                    if (data.ContainsKey("temperature") && data["temperature"] != null)
                        temperature = Convert.ToDecimal(data["temperature"]);

                    if (data.ContainsKey("angle") && data["angle"] != null)
                        angle = Convert.ToDecimal(data["angle"]);

                    if (data.ContainsKey("reason") && data["reason"] != null)
                        reason = data["reason"].ToString() ?? string.Empty;
                }

                // Si no se pudo extraer algunos datos mediante JavaScript, intentar métodos alternativos
                if (string.IsNullOrEmpty(georeference))
                {
                    var (addressElement, _) = await dynamicWait.WaitForElementAsync(
                        By.XPath("//div[contains(text(), 'Dirección') or contains(text(), 'Ubicación')]/following-sibling::div"),
                        "address_element",
                        ensureClickable: false
                    );

                    if (addressElement != null)
                    {
                        georeference = addressElement.Text;
                    }
                }

                // Construir el campo reason combinando toda la información
                var reasonBuilder = new List<string>();

                if (!string.IsNullOrEmpty(reason))
                    reasonBuilder.Add($"Estado: {reason}");

                if (!string.IsNullOrEmpty(driver))
                    reasonBuilder.Add($"Conductor: {driver}");

                if (!string.IsNullOrEmpty(georeference))
                    reasonBuilder.Add($"Ubicación: {georeference}");

                if (!string.IsNullOrEmpty(inZone))
                    reasonBuilder.Add($"Zona: {inZone}");

                if (speed > 0)
                    reasonBuilder.Add($"Velocidad: {speed} km/h");

                if (!string.IsNullOrEmpty(detentionTime))
                    reasonBuilder.Add($"Tiempo Detenido: {detentionTime}");

                if (distanceTraveled > 0)
                    reasonBuilder.Add($"Distancia: {distanceTraveled} km");

                if (temperature > 0)
                    reasonBuilder.Add($"Temperatura: {temperature} °C");

                reason = string.Join(" | ", reasonBuilder);

                return new LocationDataInfo
                {
                    Speed = speed,
                    Timestamp = timestamp,
                    Driver = driver,
                    Georeference = georeference,
                    InZone = inZone,
                    DetentionTime = detentionTime,
                    DistanceTraveled = distanceTraveled,
                    Temperature = temperature,
                    Angle = angle,
                    Reason = reason,
                    Latitude = 0, // Se completará luego con los datos de coordenadas
                    Longitude = 0 // Se completará luego con los datos de coordenadas
                };
            }
            catch (Exception ex)
            {
                _logger.Error("Error extrayendo datos del vehículo", ex);

                // Devolver información básica en caso de error
                return new LocationDataInfo
                {
                    Speed = 0,
                    Timestamp = DateTime.Now,
                    Driver = string.Empty,
                    Georeference = string.Empty,
                    InZone = string.Empty,
                    DetentionTime = string.Empty,
                    DistanceTraveled = 0,
                    Temperature = 0,
                    Angle = 0,
                    Reason = $"Error al extraer información: {ex.Message}",
                    Latitude = 0,
                    Longitude = 0
                };
            }
        }

        private async Task CheckPageStatus(string context = "")
        {
            try
            {
                // Verificar si hay errores HTTP en la página actual
                var currentUrl = _driver.Url;
                var pageSource = _driver.PageSource?.ToLower() ?? "";

                // Detectar errores HTTP comunes en el título o contenido
                if (pageSource.Contains("404 - not found") ||
                    pageSource.Contains("403 forbidden") ||
                    pageSource.Contains("500 internal server error") ||
                    pageSource.Contains("502 bad gateway") ||
                    pageSource.Contains("503 service unavailable") ||
                    pageSource.Contains("504 gateway timeout"))
                {
                    var errorMessage = $"Error de servidor detectado en {context}. URL: {currentUrl}";
                    _logger.Error(errorMessage);
                    throw new InvalidOperationException($"SERVIDOR_CAIDO: {errorMessage}");
                }

                // Verificar si la página está completamente cargada
                var pageState = ((IJavaScriptExecutor)_driver).ExecuteScript("return document.readyState");
                if (pageState?.ToString() != "complete")
                {
                    _logger.Warning($"La página no se cargó completamente en {context}", true);
                    throw new InvalidOperationException($"SERVIDOR_CAIDO: La página no se cargó completamente en {context}");
                }

                // Verificar errores específicos de la aplicación
                if (pageSource.Contains("glassfish server") ||
                    pageSource.Contains("apache tomcat") ||
                    pageSource.Contains("server error"))
                {
                    var errorMessage = $"Error de aplicación detectado en {context}. URL: {currentUrl}";
                    _logger.Error(errorMessage);
                    throw new InvalidOperationException($"SERVIDOR_CAIDO: {errorMessage}");
                }
            }
            catch (WebDriverException ex)
            {
                _logger.Error($"Error de conexión en {context}", ex);
                throw new InvalidOperationException($"SERVIDOR_CAIDO: Error de conexión en {context}", ex);
            }
        }

        private async Task HandleChromePasswordWarningIfPresent()
        {
            try
            {
                _logger.Debug("Verificando si existe un popup de advertencia de contraseña de Chrome...");

                // Volver al documento principal
                _driver.SwitchTo().DefaultContent();

                // Verificar si existe un popup de advertencia de contraseña usando JavaScript
                var popupExists = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                // Función para buscar en shadow DOM y DOM regular
                function findInShadowAndRegularDOM(rootNode, selector) {
                    // Primero buscar en DOM normal
                    const regularDomElement = document.querySelector(selector);
                    if (regularDomElement) return regularDomElement;
                    
                    // Función recursiva para buscar en shadow DOM
                    function searchInNode(node) {
                        if (node.shadowRoot) {
                            // Buscar dentro del shadow root
                            const found = node.shadowRoot.querySelector(selector);
                            if (found) return found;
                            
                            // Buscar en los hijos del shadow root
                            const shadowChildren = node.shadowRoot.querySelectorAll('*');
                            for (const child of shadowChildren) {
                                const deepFound = searchInNode(child);
                                if (deepFound) return deepFound;
                            }
                        }
                        
                        // Buscar en los hijos del nodo regular
                        const children = node.querySelectorAll('*');
                        for (const child of children) {
                            const deepFound = searchInNode(child);
                            if (deepFound) return deepFound;
                        }
                        
                        return null;
                    }
                    
                    return searchInNode(rootNode);
                }
                
                // Buscar elementos que indiquen un popup de contraseña
                const passwordChangeTitle = document.querySelector('h1, div.title, .header-title, .modal-title');
                if (passwordChangeTitle && 
                    (passwordChangeTitle.textContent.includes('Cambia la contraseña') || 
                     passwordChangeTitle.textContent.includes('Change password'))) {
                    return true;
                }
                
                // Buscar botón de Aceptar en popups posibles
                const acceptButton = findInShadowAndRegularDOM(
                    document, 
                    'button.primary-button, cr-button[dialog-confirm], button.mat-button-base'
                );
                
                // Si encontramos un botón y contiene texto de aceptar
                if (acceptButton && 
                    (acceptButton.textContent.includes('Aceptar') || 
                     acceptButton.textContent.includes('Accept'))) {
                    return true;
                }
                
                // Buscar por contenido general que indique un popup de seguridad
                const securityText = document.body.innerText;
                return securityText.includes('Cambia la contraseña') || 
                       securityText.includes('Gestor de contraseñas') ||
                       securityText.includes('seguridad de datos');
                ");

                if (popupExists is bool && (bool)popupExists)
                {
                    _logger.Info("Popup de cambio de contraseña detectado, intentando cerrar", true);

                    // Intentar hacer clic en el botón Aceptar
                    var clickSuccess = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    function findButtonInShadowDOM(rootNode, buttonTexts) {
                        // Primero buscar en DOM normal
                        for (const text of buttonTexts) {
                            const buttons = Array.from(document.querySelectorAll('button'));
                            for (const button of buttons) {
                                if (button.textContent.includes(text) && 
                                    button.style.display !== 'none' && 
                                    button.offsetParent !== null) {
                                    button.click();
                                    return true;
                                }
                            }
                        }
                        
                        // Función recursiva para buscar en shadow DOM
                        function searchInNode(node) {
                            if (!node) return false;
                            
                            if (node.shadowRoot) {
                                // Buscar botones dentro del shadow root
                                const shadowButtons = node.shadowRoot.querySelectorAll('button, cr-button');
                                for (const button of shadowButtons) {
                                    for (const text of buttonTexts) {
                                        if (button.textContent.includes(text)) {
                                            button.click();
                                            return true;
                                        }
                                    }
                                }
                                
                                // Buscar en los hijos del shadow root
                                const shadowChildren = node.shadowRoot.querySelectorAll('*');
                                for (const child of shadowChildren) {
                                    if (searchInNode(child)) return true;
                                }
                            }
                            
                            // Buscar en los hijos del nodo regular
                            const children = node.querySelectorAll('*');
                            for (const child of children) {
                                if (searchInNode(child)) return true;
                            }
                            
                            return false;
                        }
                        
                        return searchInNode(rootNode);
                    }
                    
                    // Lista de textos posibles para el botón de aceptar
                    const buttonTexts = ['Aceptar', 'Accept', 'OK', 'Continuar', 'Continue'];
                    
                    // Buscar y hacer clic en el botón
                    return findButtonInShadowDOM(document, buttonTexts);
                    ") as bool? ?? false;

                    if (clickSuccess)
                    {
                        _logger.Info("Se cerró exitosamente el popup de cambio de contraseña", true);

                        // Esperar a que el popup se cierre
                        var dynamicWait = new DynamicWaitHelper(_driver);
                        await dynamicWait.WaitForConditionAsync(
                            d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                                .Any(e => e.Displayed),
                            "after_chrome_warning_close",
                            TimeSpan.FromMilliseconds(500)
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error al intentar manejar el popup de advertencia de contraseña: {ex.Message}");
            }
        }

        private async Task<bool> ClickWhenClickableAsync(
            By locator,
            IWebElement? cachedElement = null,
            TimeSpan? timeout = null,
            int maxAttempts = 3,
            CancellationToken ct = default)
        {
            timeout ??= TimeSpan.FromSeconds(8);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    // Obtener (o reutilizar) el elemento
                    IWebElement element;
                    if (cachedElement is not null)
                    {
                        element = cachedElement;
                    }
                    else
                    {
                        var wait = new WebDriverWait(_driver, timeout.Value)
                        {
                            PollingInterval = TimeSpan.FromMilliseconds(100)
                        };
                        wait.IgnoreExceptionTypes(
                            typeof(NoSuchElementException),
                            typeof(StaleElementReferenceException));

                        element = wait.Until(drv =>
                        {
                            var el = drv.FindElement(locator);
                            return (el.Displayed && el.Enabled) ? el : null;
                        });
                    }

                    // Scroll al centro
                    ((IJavaScriptExecutor)_driver)
                        .ExecuteScript(
                            "arguments[0].scrollIntoView({block:'center',inline:'center'});",
                            element);

                    // Clic normal
                    try { element.Click(); return true; }
                    catch (Exception ex) { _logger.Debug($"Clic nativo falló: {ex.Message}"); }

                    // Clic JavaScript
                    try
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", element);
                        return true;
                    }
                    catch (Exception ex) { _logger.Debug($"Clic JS falló: {ex.Message}"); }

                    // Clic Actions
                    try
                    {
                        new Actions(_driver).MoveToElement(element).Click().Perform();
                        return true;
                    }
                    catch (Exception ex) { _logger.Debug($"Clic Actions falló: {ex.Message}"); }

                    // dispatchEvent como último recurso
                    try
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                        const r = arguments[0].getBoundingClientRect();
                        arguments[0].dispatchEvent(new MouseEvent('click',{
                            bubbles:true,cancelable:true,view:window,
                            clientX:r.left+r.width/2,clientY:r.top+r.height/2}));
                    ", element);
                        return true;
                    }
                    catch (Exception ex) { _logger.Debug($"dispatchEvent falló: {ex.Message}"); }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Intento {attempt}/{maxAttempts} falló: {ex.Message}");
                }

                // Breve pausa antes de reintentar
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Min(300, timeout.Value.TotalMilliseconds / 25)),
                    ct);
            }

            return false; // Agotados los intentos
        }

        public void Dispose()
        {
            try
            {
                _logger.Debug("Iniciando proceso de dispose del ChromeDriver");
                _driver?.Quit();
                _driver?.Dispose();
                _logger.Info("ChromeDriver disposed exitosamente", true);
            }
            catch (Exception ex)
            {
                _logger.Error("Error durante el dispose del ChromeDriver", ex);
            }
        }
    }
}
