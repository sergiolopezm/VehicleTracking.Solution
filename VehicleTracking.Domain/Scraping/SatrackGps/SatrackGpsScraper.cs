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
                _currentPatent = patent;

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
                var (userInput, userError) = await dynamicWait.WaitForElementAsync(
                    By.Id("txt_login_username"),
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
                var (passInput, passError) = await dynamicWait.WaitForElementAsync(
                    By.Id("txt_login_password"),
                    "login_password",
                    ensureClickable: true
                );

                if (passInput == null)
                {
                    _logger.Warning($"No se pudo encontrar el campo de contraseña para el vehículo {patent}. Detalles del error: {passError}", true);
                    return false;
                }

                // Buscar el botón de login (SIN exigir que sea clickable inicialmente)
                _logger.Debug("Buscando botón de login (sin exigir que sea clickable)...");
                var (loginButton, buttonError) = await dynamicWait.WaitForElementAsync(
                    By.Id("btn_login_login"),
                    "login_button",
                    ensureClickable: false  // Cambiado a false porque el botón está deshabilitado inicialmente
                );

                if (loginButton == null)
                {
                    _logger.Warning($"No se pudo encontrar el botón de inicio de sesión para el vehículo {patent}. Detalles del error: {buttonError}", true);
                    return false;
                }

                _logger.Debug("Limpiando y llenando campos del formulario...");

                // Rellenar usuario
                userInput.Clear();
                userInput.SendKeys(username);

                // Establecer valor directamente con JavaScript para asegurar que Angular lo detecte
                ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            arguments[0].value = arguments[1];
            arguments[0].dispatchEvent(new Event('input', { bubbles:true }));
        ", userInput, username);

                // Rellenar contraseña
                passInput.Clear();
                passInput.SendKeys(password);

                // Establecer valor directamente con JavaScript para asegurar que Angular lo detecte
                ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            arguments[0].value = arguments[1];
            arguments[0].dispatchEvent(new Event('input', { bubbles:true }));
        ", passInput, password);

                // Forzar blur para que Angular valide el formulario
                _logger.Debug("Enviando Tab para forzar validación de formulario...");
                passInput.SendKeys(Keys.Tab);

                // Esperar a que el botón se habilite
                _logger.Debug("Esperando que el botón de login se habilite...");
                var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
                try
                {
                    wait.Until(drv =>
                    {
                        try
                        {
                            return loginButton.Enabled;
                        }
                        catch (StaleElementReferenceException)
                        {
                            return false;
                        }
                    });
                    _logger.Debug("El botón de login ahora está habilitado");
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.Warning($"El botón de login no se habilitó después de llenar los campos para el vehículo {patent}", true);

                    // Capturar screenshot para diagnóstico
                    try
                    {
                        Screenshot screenshot = ((ITakesScreenshot)_driver).GetScreenshot();
                        var screenshotPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"login_timeout_{DateTime.Now:yyyyMMddHHmmss}.png");
                        screenshot.SaveAsFile(screenshotPath);
                        _logger.Debug($"Captura de pantalla guardada en: {screenshotPath}");
                    }
                    catch { }

                    return false;
                }

                // Verificar estado antes de intentar el login
                await CheckPageStatus("pre-login");

                // Ahora que el botón está habilitado, hacer clic
                _logger.Debug("Intentando hacer clic en el botón de login habilitado...");
                try
                {
                    loginButton.Click();
                    _logger.Debug("Clic en botón de login ejecutado correctamente");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error al hacer clic en el botón de login: {ex.Message}", true);

                    // Intentar clic alternativo con JavaScript
                    try
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", loginButton);
                        _logger.Debug("Clic en botón de login ejecutado con JavaScript");
                    }
                    catch (Exception jsEx)
                    {
                        _logger.Warning($"Error al hacer clic con JavaScript: {jsEx.Message}", true);
                        return false;
                    }
                }

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

                // CAMBIO IMPORTANTE: Espera más robusta para la página post-login
                var loginSuccess = await WaitForPostLoginPage(dynamicWait);

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
                _currentPatent = patent ?? string.Empty;

                if (!_isLoggedIn)
                    throw new InvalidOperationException("No se ha iniciado sesión");

                // 1️⃣  Validar salud de la página
                await CheckPageStatus("inicio de búsqueda de vehículo");

                var dynamicWait = new DynamicWaitHelper(_driver);

                // 2️⃣  Garantizar que la interfaz principal terminó de cargar
                await EnsureMainPageIsFullyLoaded(dynamicWait);

                // 3️⃣  Asegurarnos de estar en la vista de mapa/vehículos
                await NavigateToVehiclesSection(dynamicWait);

                // 4️⃣  Esperar de forma **dinámica** a que la lista de vehículos esté lista
                var listReady = await WaitForVehicleListReady(dynamicWait, TimeSpan.FromSeconds(60));
                if (!listReady)
                {
                    _logger.Warning(
                        "La lista de vehículos no estuvo disponible dentro del tiempo máximo de espera",
                        true);
                }

                _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds} ms] " +
                              $"Iniciando búsqueda de vehículo {patent}");

                // 5️⃣  Buscar el vehículo
                var vehicleElement = await FindVehicleInVehicleList(patent, dynamicWait);
                if (vehicleElement == null)
                    throw new InvalidOperationException(
                        $"CONFIGURACION_INVALIDA: No se encontró el vehículo con placa {patent}");

                // 6️⃣  Clic en la placa para centrar el mapa
                await ClickWhenClickableAsync(
                    By.Id(vehicleElement.GetAttribute("id") ?? string.Empty),
                    cachedElement: vehicleElement);

                _logger.Info($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds} ms] " +
                             $"Vehículo {patent} seleccionado", true);

                // 7️⃣  Esperar a que el mapa termine de actualizar
                await dynamicWait.WaitForConditionAsync(
                    d => !d.FindElements(By.CssSelector(".loading, .spinner, .wait"))
                           .Any(e => e.Displayed),
                    "map_update_complete",
                    TimeSpan.FromSeconds(10));

                // 8️⃣  Extraer toda la información
                var locationInfo = await ExtractVehicleInformation(dynamicWait);

                _logger.Info($"[Tiempo TOTAL: {stopwatch.ElapsedMilliseconds} ms] " +
                             "Proceso completado con éxito", true);
                _logger.Info($"Coordenadas obtenidas: {locationInfo.Latitude}, {locationInfo.Longitude}",
                             true);

                return locationInfo;
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("SERVIDOR_CAIDO:"))
            {
                _logger.Error($"[T+{stopwatch.ElapsedMilliseconds} ms] " +
                              $"Servidor caído al obtener ubicación de {patent}", ex);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"[T+{stopwatch.ElapsedMilliseconds} ms] " +
                              $"Error al obtener ubicación de {patent}", ex);
                throw new InvalidOperationException(
                    $"Error obteniendo ubicación del vehículo: {ex.Message}", ex);
            }
        }

        private async Task<IWebElement?> FindVehicleInVehicleList(
        string patent,
        DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug($"Buscando vehículo con placa {patent} en la lista");

                // 🅰️  Localizar contenedor de la lista (scrollable)
                var vehicleList = await WaitForVehicleListContainer(dynamicWait);
                if (vehicleList == null)
                {
                    _logger.Warning("No se encontró la lista de vehículos en la interfaz", true);
                    return null;
                }

                // 🅱️  Usar el cuadro de búsqueda si existe
                var (searchInput, _) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector(
                        "input[placeholder*='Buscar'], input[aria-label*='buscar'], " +
                        "input[placeholder*='Filtrar'], input.search-input"),
                    "search_input",
                    ensureClickable: true);

                if (searchInput != null)
                {
                    _logger.Debug("Aplicando filtro en el cuadro de búsqueda");
                    searchInput.Clear();
                    searchInput.SendKeys(patent);
                    await dynamicWait.WaitForAjaxCompletionAsync();
                    await dynamicWait.WaitForConditionAsync(
                        d => !d.FindElements(By.CssSelector(".loading, .spinner, .wait"))
                               .Any(e => e.Displayed),
                        "search_filter_complete",
                        TimeSpan.FromSeconds(5));
                }

                // 🅲️  Intento directo
                var vehicleItem = await FindVehicleItemByPatent(patent, dynamicWait);
                if (vehicleItem != null)
                {
                    _logger.Info($"Vehículo {patent} encontrado sin necesidad de scroll", true);
                    return vehicleItem;
                }

                // 🅳️  Scroll si aún no aparece
                _logger.Debug("Placa no visible; iniciando scroll progresivo");
                return await ScrollAndFindVehicle(patent, vehicleList, dynamicWait);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error buscando vehículo {patent} en la lista", ex);
                return null;
            }
        }

        private async Task<IWebElement?> FindVehicleItemByPatent(string patent, DynamicWaitHelper dynamicWait)
        {
            try
            {
                // Intentar múltiples selectores para encontrar el elemento del vehículo
                var selectors = new[]
                {
            $"//div[contains(@class, 'vehicle-item') and contains(., '{patent}')]",
            $"//tr[contains(., '{patent}')]",
            $"//li[contains(., '{patent}')]",
            $"//div[contains(@class, 'item') and contains(., '{patent}')]",
            $"//div[contains(text(), '{patent}')]",
            $"//span[contains(text(), '{patent}')]"
        };

                foreach (var selector in selectors)
                {
                    var (element, _) = await dynamicWait.WaitForElementAsync(
                        By.XPath(selector),
                        $"vehicle_item_{selector.GetHashCode()}",
                        ensureClickable: true
                    );

                    if (element != null)
                    {
                        _logger.Debug($"Vehículo encontrado con selector: {selector}");
                        return element;
                    }
                }

                // Búsqueda adicional usando JavaScript para detectar elementos que contienen la placa
                var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            function findElementWithText(text) {
                const elements = document.querySelectorAll('*');
                for (const element of elements) {
                    if (element.textContent && 
                        element.textContent.includes(text) && 
                        element.offsetWidth > 0 && 
                        element.offsetHeight > 0) {
                        
                        // Verificar si es un elemento interactivo o un contenedor de lista
                        if (element.tagName === 'DIV' || 
                            element.tagName === 'LI' || 
                            element.tagName === 'TR' || 
                            element.tagName === 'SPAN' || 
                            element.tagName === 'A' || 
                            element.tagName === 'BUTTON') {
                            
                            // Preferir elementos con clases relacionadas con vehículos
                            if (element.className.includes('vehicle') || 
                                element.className.includes('item') || 
                                element.className.includes('row')) {
                                return element;
                            }
                            
                            // Si no tiene clases específicas pero contiene el texto, también es candidato
                            if (element.textContent.trim() === text || 
                                element.textContent.includes(' ' + text + ' ')) {
                                return element;
                            }
                        }
                    }
                }
                return null;
            }
            return findElementWithText(arguments[0]);
        ", patent);

                if (jsResult != null)
                {
                    _logger.Debug("Vehículo encontrado mediante JavaScript");
                    return (IWebElement)jsResult;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error en FindVehicleItemByPatent: {ex.Message}");
                return null;
            }
        }

        private async Task<IWebElement?> ScrollAndFindVehicle(string patent, IWebElement container, DynamicWaitHelper dynamicWait)
        {
            _logger.Debug("Iniciando proceso de scroll para buscar el vehículo");

            // Obtener la altura del contenedor
            var containerHeight = Convert.ToInt32(((IJavaScriptExecutor)_driver).ExecuteScript(
                "return arguments[0].scrollHeight", container));

            // Altura visible del contenedor
            var viewportHeight = Convert.ToInt32(((IJavaScriptExecutor)_driver).ExecuteScript(
                "return arguments[0].clientHeight", container));

            // Número aproximado de pasos de scroll necesarios
            var scrollSteps = Math.Ceiling((double)containerHeight / viewportHeight);

            _logger.Debug($"Altura total: {containerHeight}px, Altura visible: {viewportHeight}px, Pasos de scroll estimados: {scrollSteps}");

            // Scroll desde el inicio
            ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollTop = 0", container);
            await Task.Delay(300); // Breve pausa para que se estabilice la vista

            // Intentar encontrar el vehículo en la posición inicial
            var vehicle = await FindVehicleItemByPatent(patent, dynamicWait);
            if (vehicle != null) return vehicle;

            // Realizar scroll progresivamente a través de toda la lista
            for (int i = 1; i <= scrollSteps + 1; i++) // +1 para asegurar que se revise toda la lista
            {
                // Realizar scroll
                ((IJavaScriptExecutor)_driver).ExecuteScript(
                    "arguments[0].scrollTop += arguments[1]", container, viewportHeight * 0.8);

                await Task.Delay(300); // Pausa para que se carguen los elementos

                // Intentar encontrar el vehículo después del scroll
                vehicle = await FindVehicleItemByPatent(patent, dynamicWait);
                if (vehicle != null)
                {
                    _logger.Info($"Vehículo {patent} encontrado después de scroll ({i} de {scrollSteps} pasos)", true);
                    return vehicle;
                }
            }

    // Scroll al inicio y verificación final
    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollTop = 0", container);
            await Task.Delay(300);

            // Última verificación completa de toda la página usando JavaScript
            var finalCheck = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
        function containsText(text) {
            return document.body.textContent.includes(text);
        }
        return containsText(arguments[0]);
    ", patent);

            if (finalCheck is bool checkResult && checkResult)
            {
                _logger.Warning($"La placa {patent} parece estar en el documento pero no se pudo encontrar el elemento específico", true);
            }
            else
            {
                _logger.Warning($"La placa {patent} no se encontró en el documento después de revisar toda la lista", true);
            }

            return null;
        }

        private async Task<LocationDataInfo> ExtractVehicleInformation(DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug("Iniciando extracción de información del vehículo seleccionado");

                // Esperar a que aparezca el panel de información o los datos en el mapa
                var (infoPanel, _) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector(".vehicle-info, .vehicle-details, .info-panel, .popup-content, .leaflet-popup-content"),
                    "vehicle_info_panel",
                    ensureClickable: false
                );

                // Extraer coordenadas
                var coordinates = await ExtractCoordinates(dynamicWait);

                // Extraer el resto de la información desde el panel o el mapa
                var locationInfo = new LocationDataInfo
                {
                    Latitude = coordinates.Latitude,
                    Longitude = coordinates.Longitude,
                    Speed = 0,
                    Timestamp = DateTime.Now,
                    Driver = string.Empty,
                    Georeference = string.Empty,
                    InZone = string.Empty,
                    DetentionTime = string.Empty,
                    DistanceTraveled = 0,
                    Temperature = 0,
                    Angle = 0,
                    Reason = string.Empty
                };

                // Si encontramos un panel de información, extraer datos de él
                if (infoPanel != null)
                {
                    _logger.Debug("Panel de información encontrado, extrayendo datos detallados");
                    await ExtractDataFromInfoPanel(infoPanel, locationInfo, dynamicWait);
                }
                else
                {
                    // Intentar extraer información directamente del mapa
                    _logger.Debug("Panel de información no encontrado, buscando datos en el mapa");
                    await ExtractDataFromMap(locationInfo, dynamicWait);
                }

                return locationInfo;
            }
            catch (Exception ex)
            {
                _logger.Error("Error extrayendo información del vehículo", ex);
                throw;
            }
        }

        private async Task<(decimal Latitude, decimal Longitude)> ExtractCoordinates(DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug("Extrayendo coordenadas del vehículo");

                // Intentar obtener coordenadas desde la URL
                var urlCoordinates = await TryGetCoordinatesFromUrl();
                if (urlCoordinates.HasValue)
                {
                    _logger.Info($"Coordenadas extraídas de la URL: {urlCoordinates.Value.Latitude}, {urlCoordinates.Value.Longitude}", true);
                    return urlCoordinates.Value;
                }

                // Intentar obtener coordenadas desde elementos visibles
                var (coordinatesElement, _) = await dynamicWait.WaitForElementAsync(
                    By.XPath("//div[contains(text(), 'Latitud') or contains(text(), 'Longitud') or contains(text(), 'Coordenadas')]"),
                    "coordinates_element",
                    ensureClickable: false
                );

                if (coordinatesElement != null)
                {
                    var text = coordinatesElement.Text;
                    var latMatch = System.Text.RegularExpressions.Regex.Match(text, @"Lat[^0-9-]*(-?\d+\.?\d*)");
                    var lonMatch = System.Text.RegularExpressions.Regex.Match(text, @"L(on|ng)[^0-9-]*(-?\d+\.?\d*)");

                    if (latMatch.Success && lonMatch.Success)
                    {
                        var lat = decimal.Parse(latMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                        var lon = decimal.Parse(lonMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);

                        _logger.Info($"Coordenadas extraídas del texto: {lat}, {lon}", true);
                        return (lat, lon);
                    }
                }

                // Intentar extraer coordenadas mediante JavaScript desde el mapa
                var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            try {
                // Buscar objeto mapa (Leaflet o Google Maps)
                if (window.map && window.map.getCenter) {
                    var center = window.map.getCenter();
                    return { lat: center.lat, lng: center.lng };
                }
                
                // Buscar específicamente mapa de Leaflet
                var leafletMap = null;
                for (var key in window) {
                    if (window[key] && typeof window[key] === 'object' && window[key]._leaflet_id) {
                        leafletMap = window[key];
                        break;
                    }
                }
                
                if (leafletMap) {
                    // Buscar marcador seleccionado
                    for (var key in leafletMap._layers) {
                        var layer = leafletMap._layers[key];
                        if (layer && layer._icon && 
                            (layer._icon.classList.contains('selected') || 
                             layer._icon.classList.contains('active') ||
                             layer._icon.style.zIndex > 900)) {
                            return { 
                                lat: layer._latlng.lat, 
                                lng: layer._latlng.lng 
                            };
                        }
                    }
                    
                    // Si no hay marcador seleccionado, usar el centro del mapa
                    return { 
                        lat: leafletMap.getCenter().lat, 
                        lng: leafletMap.getCenter().lng 
                    };
                }
                
                return null;
            } catch(e) {
                console.error('Error extracting coordinates:', e);
                return null;
            }
        ");

                if (jsResult != null)
                {
                    var coords = (Dictionary<string, object>)jsResult;
                    var lat = Convert.ToDecimal(coords["lat"]);
                    var lng = Convert.ToDecimal(coords["lng"]);

                    _logger.Info($"Coordenadas extraídas con JavaScript: {lat}, {lng}", true);
                    return (lat, lng);
                }

                // Si todo falla, buscar por cualquier número que parezca coordenada en la página
                var pageSource = _driver.PageSource;
                var latMatches = System.Text.RegularExpressions.Regex.Matches(pageSource, @"lat(itud)?[^0-9-]*(-?\d+\.\d+)");
                var lonMatches = System.Text.RegularExpressions.Regex.Matches(pageSource, @"lo?n(gitud)?[^0-9-]*(-?\d+\.\d+)");

                if (latMatches.Count > 0 && lonMatches.Count > 0)
                {
                    var lat = decimal.Parse(latMatches[0].Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var lon = decimal.Parse(lonMatches[0].Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);

                    _logger.Info($"Coordenadas extraídas del código fuente: {lat}, {lon}", true);
                    return (lat, lon);
                }

                _logger.Warning("No se pudieron extraer coordenadas, usando valores por defecto", true);
                return (0, 0);
            }
            catch (Exception ex)
            {
                _logger.Error("Error extrayendo coordenadas", ex);
                return (0, 0);
            }
        }

        private async Task<(decimal Latitude, decimal Longitude)?> TryGetCoordinatesFromUrl()
        {
            try
            {
                var currentUrl = _driver.Url;

                // Patrones comunes para coordenadas en URLs
                var latMatchUrl = System.Text.RegularExpressions.Regex.Match(currentUrl, @"lat=(-?\d+\.?\d*)");
                var lonMatchUrl = System.Text.RegularExpressions.Regex.Match(currentUrl, @"(lon|lng)=(-?\d+\.?\d*)");

                if (latMatchUrl.Success && lonMatchUrl.Success)
                {
                    var lat = decimal.Parse(latMatchUrl.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var lon = decimal.Parse(lonMatchUrl.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    return (lat, lon);
                }

                // Probar otros formatos (por ejemplo, coordenadas directamente en la ruta)
                var coordsMatch = System.Text.RegularExpressions.Regex.Match(currentUrl, @"@(-?\d+\.?\d*),(-?\d+\.?\d*)");
                if (coordsMatch.Success)
                {
                    var lat = decimal.Parse(coordsMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var lon = decimal.Parse(coordsMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    return (lat, lon);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task ExtractDataFromInfoPanel(IWebElement infoPanel, LocationDataInfo locationInfo, DynamicWaitHelper dynamicWait)
        {
            try
            {
                // Extraer los datos del panel usando JavaScript para mayor robustez
                var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            function extractData(panel) {
                const result = {};
                const text = panel.textContent || '';
                
                // Función para extraer datos con diferentes patrones
                function extract(patterns, defaultValue = '') {
                    for (const pattern of patterns) {
                        const regex = new RegExp(pattern.regex, 'i');
                        const match = text.match(regex);
                        if (match && match[pattern.group]) {
                            return match[pattern.group].trim();
                        }
                    }
                    return defaultValue;
                }
                
                // Extraer velocidad
                result.speed = extract([
                    { regex: 'Velocidad[^0-9]*([0-9.,]+)', group: 1 },
                    { regex: 'Speed[^0-9]*([0-9.,]+)', group: 1 }
                ]);
                
                // Extraer fecha/hora
                result.timestamp = extract([
                    { regex: 'Fecha[^:]*:([^]*?)(?:Veloc|\n|$)', group: 1 },
                    { regex: 'Hora[^:]*:([^]*?)(?:Veloc|\n|$)', group: 1 },
                    { regex: 'Date[^:]*:([^]*?)(?:Speed|\n|$)', group: 1 }
                ]);
                
                // Extraer conductor
                result.driver = extract([
                    { regex: 'Conductor[^:]*:([^]*?)(?:\n|$)', group: 1 },
                    { regex: 'Driver[^:]*:([^]*?)(?:\n|$)', group: 1 }
                ]);
                
                // Extraer georeferencia
                result.georeference = extract([
                    { regex: 'Dirección|Ubicación|Direc[^:]*:([^]*?)(?:\n|$)', group: 1 },
                    { regex: 'Address|Location[^:]*:([^]*?)(?:\n|$)', group: 1 }
                ]);
                
                // Extraer zona
                result.inZone = extract([
                    { regex: 'Zona|Área|Area[^:]*:([^]*?)(?:\n|$)', group: 1 },
                    { regex: 'Zone[^:]*:([^]*?)(?:\n|$)', group: 1 }
                ]);
                
                // Extraer tiempo de detención
                result.detentionTime = extract([
                    { regex: 'Detenido|Parada|Detención[^:]*:([^]*?)(?:\n|$)', group: 1 },
                    { regex: 'Stopped|Detention[^:]*:([^]*?)(?:\n|$)', group: 1 }
                ]);
                
                // Extraer distancia recorrida
                result.distanceTraveled = extract([
                    { regex: 'Distancia|Recorrido|Odómetro[^:]*:([^]*?)(?:km|\n|$)', group: 1 },
                    { regex: 'Distance|Odometer[^:]*:([^]*?)(?:km|\n|$)', group: 1 }
                ]);
                
                // Extraer temperatura
                result.temperature = extract([
                    { regex: 'Temperatura|Temp[^:]*:([^]*?)(?:°C|\n|$)', group: 1 },
                    { regex: 'Temperature[^:]*:([^]*?)(?:°C|\n|$)', group: 1 }
                ]);
                
                // Extraer ángulo
                result.angle = extract([
                    { regex: 'Ángulo|Angulo|Rumbo|Heading[^:]*:([^]*?)(?:°|\n|$)', group: 1 },
                    { regex: 'Angle|Course[^:]*:([^]*?)(?:°|\n|$)', group: 1 }
                ]);
                
                // Extraer estado/evento/motivo
                result.reason = extract([
                    { regex: 'Estado|Evento|Motivo|Status[^:]*:([^]*?)(?:\n|$)', group: 1 },
                    { regex: 'Event|Reason[^:]*:([^]*?)(?:\n|$)', group: 1 }
                ]);
                
                return result;
            }
            return extractData(arguments[0]);
        ", infoPanel);

                if (jsResult != null)
                {
                    var data = (Dictionary<string, object>)jsResult;

                    // Procesar velocidad
                    if (data.ContainsKey("speed") && data["speed"] != null)
                    {
                        var speedStr = data["speed"].ToString();
                        if (!string.IsNullOrEmpty(speedStr) && decimal.TryParse(speedStr.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal speed))
                        {
                            locationInfo.Speed = speed;
                        }
                    }

                    // Procesar timestamp
                    if (data.ContainsKey("timestamp") && data["timestamp"] != null)
                    {
                        var timestampStr = data["timestamp"].ToString();
                        if (!string.IsNullOrEmpty(timestampStr))
                        {
                            // Intentar varios formatos de fecha comunes
                            string[] dateFormats = {
                        "dd/MM/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm:ss",
                        "yyyy-MM-dd HH:mm:ss", "dd-MM-yyyy HH:mm:ss",
                        "dd/MM/yyyy HH:mm", "MM/dd/yyyy HH:mm",
                        "yyyy-MM-dd HH:mm", "dd-MM-yyyy HH:mm"
                    };

                            if (DateTime.TryParseExact(timestampStr.Trim(), dateFormats,
                                                      System.Globalization.CultureInfo.InvariantCulture,
                                                      System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                            {
                                locationInfo.Timestamp = parsedDate;
                            }
                            else if (DateTime.TryParse(timestampStr.Trim(), out parsedDate))
                            {
                                locationInfo.Timestamp = parsedDate;
                            }
                        }
                    }

                    // Procesar otros datos de texto
                    if (data.ContainsKey("driver") && data["driver"] != null)
                        locationInfo.Driver = data["driver"].ToString() ?? string.Empty;

                    if (data.ContainsKey("georeference") && data["georeference"] != null)
                        locationInfo.Georeference = data["georeference"].ToString() ?? string.Empty;

                    if (data.ContainsKey("inZone") && data["inZone"] != null)
                        locationInfo.InZone = data["inZone"].ToString() ?? string.Empty;

                    if (data.ContainsKey("detentionTime") && data["detentionTime"] != null)
                        locationInfo.DetentionTime = data["detentionTime"].ToString() ?? string.Empty;

                    // Procesar valores numéricos
                    if (data.ContainsKey("distanceTraveled") && data["distanceTraveled"] != null)
                    {
                        var distStr = data["distanceTraveled"].ToString();
                        if (!string.IsNullOrEmpty(distStr) && decimal.TryParse(distStr.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal dist))
                        {
                            locationInfo.DistanceTraveled = dist;
                        }
                    }

                    if (data.ContainsKey("temperature") && data["temperature"] != null)
                    {
                        var tempStr = data["temperature"].ToString();
                        if (!string.IsNullOrEmpty(tempStr) && decimal.TryParse(tempStr.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal temp))
                        {
                            locationInfo.Temperature = temp;
                        }
                    }

                    if (data.ContainsKey("angle") && data["angle"] != null)
                    {
                        var angleStr = data["angle"].ToString();
                        if (!string.IsNullOrEmpty(angleStr) && decimal.TryParse(angleStr.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal angle))
                        {
                            locationInfo.Angle = angle;
                        }
                    }

                    if (data.ContainsKey("reason") && data["reason"] != null)
                        locationInfo.Reason = data["reason"].ToString() ?? string.Empty;
                }

                // Si no hay razón, construir una a partir de otros datos
                if (string.IsNullOrEmpty(locationInfo.Reason))
                {
                    var reasonBuilder = new List<string>();

                    if (!string.IsNullOrEmpty(locationInfo.Driver))
                        reasonBuilder.Add($"Conductor: {locationInfo.Driver}");

                    if (!string.IsNullOrEmpty(locationInfo.Georeference))
                        reasonBuilder.Add($"Ubicación: {locationInfo.Georeference}");

                    if (!string.IsNullOrEmpty(locationInfo.InZone))
                        reasonBuilder.Add($"Zona: {locationInfo.InZone}");

                    if (locationInfo.Speed > 0)
                        reasonBuilder.Add($"Velocidad: {locationInfo.Speed} km/h");

                    if (!string.IsNullOrEmpty(locationInfo.DetentionTime))
                        reasonBuilder.Add($"Tiempo Detenido: {locationInfo.DetentionTime}");

                    locationInfo.Reason = string.Join(" | ", reasonBuilder);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error extrayendo datos del panel de información: {ex.Message}");
            }
        }

        private async Task ExtractDataFromMap(LocationDataInfo locationInfo, DynamicWaitHelper dynamicWait)
        {
            try
            {
                // Extraer datos directamente del mapa o la interfaz usando JavaScript
                var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            function extractMapData() {
                const result = {};
                
                // Buscar elementos que podrían contener información del vehículo
                const elements = document.querySelectorAll('.vehicle-data, .info-box, .map-popup, .leaflet-popup-content');
                
                for (const element of elements) {
                    const text = element.textContent || '';
                    
                    // Extraer velocidad
                    const speedMatch = text.match(/Velocidad[^0-9]*(\d+\.?\d*)/i);
                    if (speedMatch) result.speed = parseFloat(speedMatch[1]);
                    
                    // Extraer dirección/ubicación
                    const geoMatch = text.match(/Dirección|Ubicación[^:]*:([^]*?)(?:\n|$)/i);
                    if (geoMatch) result.georeference = geoMatch[1].trim();
                    
                    // Extraer estado/evento
                    const stateMatch = text.match(/Estado|Evento[^:]*:([^]*?)(?:\n|$)/i);
                    if (stateMatch) result.reason = stateMatch[1].trim();
                }
                
                return result;
            }
            return extractMapData();
        ");

                if (jsResult != null)
                {
                    var data = (Dictionary<string, object>)jsResult;

                    if (data.ContainsKey("speed") && data["speed"] != null)
                        locationInfo.Speed = Convert.ToDecimal(data["speed"]);

                    if (data.ContainsKey("georeference") && data["georeference"] != null)
                        locationInfo.Georeference = data["georeference"].ToString() ?? string.Empty;

                    if (data.ContainsKey("reason") && data["reason"] != null)
                        locationInfo.Reason = data["reason"].ToString() ?? string.Empty;
                }

                // Si no hay razón, intentar construir una básica
                if (string.IsNullOrEmpty(locationInfo.Reason))
                {
                    locationInfo.Reason = locationInfo.Speed > 0
                        ? $"En movimiento a {locationInfo.Speed} km/h"
                        : "Detenido";

                    if (!string.IsNullOrEmpty(locationInfo.Georeference))
                        locationInfo.Reason += $" | Ubicación: {locationInfo.Georeference}";
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error extrayendo datos del mapa: {ex.Message}");
            }
        }

        private async Task NavigateToVehiclesSection(DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug("Iniciando navegación a sección de vehículos/mapa");

                // Verificar si ya estamos en la sección de mapa
                var alreadyInMapSection = await dynamicWait.WaitForConditionAsync(d =>
                {
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
                var mapLoaded = await dynamicWait.WaitForConditionAsync(d =>
                {
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
                var markersLoaded = await dynamicWait.WaitForConditionAsync(d =>
                {
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

        // Nuevo método para esperar a que la página post-login cargue completamente
        private async Task<bool> WaitForPostLoginPage(DynamicWaitHelper dynamicWait)
        {
            _logger.Debug("Esperando a que la página post-login cargue completamente...");

            try
            {
                // 1. Esperar básicamente a que la página cargue
                await dynamicWait.WaitForPageLoadAsync("post_login");

                // 2. Verificar que Angular esté estable (mejor práctica 2025)
                var angularStable = await WaitForAngularStability();
                if (!angularStable)
                {
                    _logger.Warning("Angular no se estabilizó en el tiempo esperado", true);
                }

                // 3. Esperar específicamente al componente virtual scroll crítico
                var virtualScrollReady = await WaitForVehicleVirtualScrollReady(dynamicWait);
                if (virtualScrollReady)
                {
                    _logger.Info("Virtual scroll de vehículos cargado exitosamente", true);
                    return true;
                }

                // 4. Verificación fallback por URL
                var currentUrl = _driver.Url.ToLower();
                if (currentUrl.Contains("/map") || currentUrl.Contains("/dashboard") || !currentUrl.Contains("/login"))
                {
                    _logger.Info("Login verificado por URL como fallback", true);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error("Error en WaitForPostLoginPage optimizado", ex);
                return false;
            }
        }

        // Nuevo método específico para esperar que Angular esté estable
        private async Task<bool> WaitForAngularStability(int timeoutSeconds = 30)
        {
            try
            {
                _logger.Debug("Verificando estabilidad de Angular...");

                var deadline = DateTime.Now.AddSeconds(timeoutSeconds);

                while (DateTime.Now < deadline)
                {
                    try
                    {
                        // JavaScript moderno para verificar estabilidad de Angular (2025)
                        var angularStable = (bool)((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    try {
                        // Para Angular 5+ (método más confiable)
                        if (typeof window.getAllAngularTestabilities === 'function') {
                            return window.getAllAngularTestabilities().findIndex(x => !x.isStable()) === -1;
                        }
                        
                        // Fallback para versiones anteriores
                        if (typeof window.angular !== 'undefined') {
                            var injector = window.angular.element(document).injector();
                            if (injector) {
                                var $rootScope = injector.get('$rootScope');
                                var $http = injector.get('$http');
                                return $rootScope.$$phase !== '$apply' && 
                                       $rootScope.$$phase !== '$digest' && 
                                       $http.pendingRequests.length === 0;
                            }
                        }
                        
                        // Si no hay Angular, considerar estable
                        return true;
                    } catch (e) {
                        console.log('Error verificando Angular:', e);
                        return false;
                    }
                ");

                        if (angularStable)
                        {
                            _logger.Debug("Angular confirmado como estable");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error verificando Angular stability: {ex.Message}");
                    }

                    await Task.Delay(100); // Verificar cada 100ms
                }

                _logger.Warning("Timeout esperando estabilidad de Angular", true);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error en WaitForAngularStability: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> WaitForVehicleVirtualScrollReady(DynamicWaitHelper dynamicWait, int timeoutSeconds = 45)
        {
            try
            {
                _logger.Debug("Esperando que el virtual scroll de vehículos esté listo...");

                var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
                var lastLogTime = DateTime.MinValue;

                while (DateTime.Now < deadline)
                {
                    // Log de progreso cada 5 segundos
                    if (DateTime.Now.Subtract(lastLogTime).TotalSeconds >= 5)
                    {
                        var remaining = deadline.Subtract(DateTime.Now).TotalSeconds;
                        _logger.Debug($"Esperando virtual scroll... {remaining:F0}s restantes");
                        lastLogTime = DateTime.Now;
                    }

                    try
                    {
                        // Verificar usando JavaScript optimizado
                        var scrollStatus = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    try {
                        // 1. Verificar que el contenedor principal existe y es visible
                        const scrollContainer = document.querySelector('[id*=""cdk_scroll_location_vehicles_list""], cdk-virtual-scroll-viewport, .cdk-virtual-scroll-viewport');
                        if (!scrollContainer || scrollContainer.offsetHeight === 0) {
                            return { ready: false, reason: 'Container not found or not visible' };
                        }

                        // 2. Verificar que no hay indicadores de carga específicos del virtual scroll
                        const loadingIndicators = document.querySelectorAll('.cdk-virtual-scroll-content-wrapper .loading, .cdk-virtual-scroll-content-wrapper .spinner');
                        const visibleLoading = Array.from(loadingIndicators).some(el => 
                            el.offsetHeight > 0 && el.offsetWidth > 0 && 
                            getComputedStyle(el).display !== 'none'
                        );
                        
                        if (visibleLoading) {
                            return { ready: false, reason: 'Loading indicators still visible' };
                        }

                        // 3. Verificar que hay elementos de vehículos renderizados (placas)
                        const vehicleElements = document.querySelectorAll(
                            '[class*=""item-veh""], [class*=""vehicle""], [id*=""GVS""], [id*=""alias""], ' +
                            '.cdk-virtual-scroll-content-wrapper [class*=""item""]'
                        );
                        
                        const visibleVehicles = Array.from(vehicleElements).filter(el => {
                            // Verificar que el elemento es visible
                            if (el.offsetHeight === 0 || el.offsetWidth === 0) return false;
                            
                            // Verificar que contiene texto que parece una placa
                            const text = el.textContent || '';
                            return text.match(/[A-Z]{2,3}\d{3,4}/) || text.match(/[A-Z]{3}\d{3}/) || text.match(/GVS\d+/);
                        });

                        if (visibleVehicles.length === 0) {
                            return { ready: false, reason: 'No vehicle plates found', vehicleElements: vehicleElements.length };
                        }

                        // 4. Verificación adicional: que el virtual scroll haya terminado de renderizar
                        const contentWrapper = document.querySelector('.cdk-virtual-scroll-content-wrapper');
                        if (contentWrapper) {
                            const transform = getComputedStyle(contentWrapper).transform;
                            // Si está en transición, esperar
                            if (transform && transform.includes('matrix')) {
                                // Verificar estabilidad comparando en diferentes momentos
                                if (window.lastTransform && window.lastTransform === transform) {
                                    delete window.lastTransform;
                                } else {
                                    window.lastTransform = transform;
                                    return { ready: false, reason: 'Virtual scroll still positioning' };
                                }
                            }
                        }

                        return { 
                            ready: true, 
                            vehicleCount: visibleVehicles.length,
                            reason: 'Virtual scroll ready with vehicles loaded'
                        };
                        
                    } catch (error) {
                        return { ready: false, reason: 'JavaScript error: ' + error.message };
                    }
                ");

                        if (scrollStatus != null)
                        {
                            var status = scrollStatus as Dictionary<string, object>;
                            if (status != null && status.ContainsKey("ready"))
                            {
                                var isReady = Convert.ToBoolean(status["ready"]);
                                var reason = status.ContainsKey("reason") ? status["reason"].ToString() : "Unknown";

                                if (isReady)
                                {
                                    var vehicleCount = status.ContainsKey("vehicleCount") ? status["vehicleCount"].ToString() : "unknown";
                                    _logger.Info($"Virtual scroll listo con {vehicleCount} vehículos detectados", true);
                                    return true;
                                }
                                else
                                {
                                    _logger.Debug($"Virtual scroll no listo: {reason}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error verificando virtual scroll: {ex.Message}");
                    }

                    await Task.Delay(200); // Verificar cada 200ms para ser más responsivo
                }

                _logger.Warning("Timeout esperando que el virtual scroll esté listo", true);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error("Error en WaitForVehicleVirtualScrollReady", ex);
                return false;
            }
        }

        // Nuevo método para esperar a que los indicadores de carga desaparezcan
        private async Task<bool> WaitForSpecificLoadingToComplete(int timeoutSeconds = 20)
        {
            try
            {
                _logger.Debug("Esperando que las cargas específicas terminen...");

                var deadline = DateTime.Now.AddSeconds(timeoutSeconds);

                while (DateTime.Now < deadline)
                {
                    try
                    {
                        // Verificación más específica y menos genérica
                        var hasSpecificLoading = (bool)((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    try {
                        // Solo verificar indicadores de carga que realmente importan
                        const criticalLoaders = document.querySelectorAll(
                            // Indicadores específicos de la aplicación de tracking
                            '.main-loading, .app-loading, .login-loading, ' +
                            // Indicadores de mapa
                            '.leaflet-loading, .map-loading, ' +
                            // Indicadores de lista de vehículos
                            '.vehicle-list-loading, .sidebar-loading, ' +
                            // Solo spinners que están EN EL CENTRO de la pantalla (indicadores principales)
                            '.spinner[style*=""position: fixed""], .loading[style*=""position: fixed""]'
                        );
                        
                        // Verificar solo los que están visibles Y centrados (indicadores principales)
                        const visibleCriticalLoaders = Array.from(criticalLoaders).filter(el => {
                            if (el.offsetHeight === 0 || el.offsetWidth === 0) return false;
                            
                            const rect = el.getBoundingClientRect();
                            const centerX = window.innerWidth / 2;
                            const centerY = window.innerHeight / 2;
                            
                            // Solo considerar elementos que están cerca del centro
                            return Math.abs(rect.left + rect.width/2 - centerX) < 100 && 
                                   Math.abs(rect.top + rect.height/2 - centerY) < 100;
                        });

                        return visibleCriticalLoaders.length > 0;
                    } catch (e) {
                        return false;
                    }
                ");

                        if (!hasSpecificLoading)
                        {
                            _logger.Debug("No se detectaron indicadores de carga críticos");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error verificando indicadores de carga: {ex.Message}");
                    }

                    await Task.Delay(200);
                }

                _logger.Debug("Timeout en verificación de indicadores de carga específicos");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error en WaitForSpecificLoadingToComplete: {ex.Message}");
                return false;
            }
        }

        // Nuevo método para asegurar que la página principal ha cargado completamente
        private async Task EnsureMainPageIsFullyLoaded(DynamicWaitHelper dynamicWait)
        {
            _logger.Debug("Verificando que la página principal haya cargado completamente...");

            // Esperar a que la página cargue básicamente
            await dynamicWait.WaitForPageLoadAsync("main_page");

            // Esperar a que las peticiones AJAX terminen
            await dynamicWait.WaitForAjaxCompletionAsync();

            // Esperar a que los indicadores de carga desaparezcan
            await WaitForSpecificLoadingToComplete();

            // Verificar si la interfaz principal está visible
            var interfaceReady = await WaitForMainInterfaceToBeReady();
            if (!interfaceReady)
            {
                _logger.Warning("No se pudo verificar que la interfaz principal esté lista después del tiempo de espera máximo");

                // Intentar refrescar la página si parece que se ha quedado atascada
                var pageSource = _driver.PageSource?.ToLower() ?? "";
                var currentUrl = _driver.Url.ToLower();

                if (!currentUrl.Contains("login") &&
                    (pageSource.Contains("error") ||
                     pageSource.Contains("timeout") ||
                     !pageSource.Contains("vehicle")))
                {
                    _logger.Debug("Intentando refrescar la página para resolver posible problema...");
                    _driver.Navigate().Refresh();

                    // Esperar nuevamente después del refresh
                    await dynamicWait.WaitForPageLoadAsync("refresh_page");
                    await WaitForSpecificLoadingToComplete();
                    await WaitForMainInterfaceToBeReady();
                }
            }

            // Dar un tiempo adicional para que la interfaz se estabilice
            await Task.Delay(1000);

            _logger.Debug("Verificación de carga de página principal completada");
        }

        // Nuevo método para esperar a que la interfaz principal esté lista
        private async Task<bool> WaitForMainInterfaceToBeReady()
        {
            try
            {
                _logger.Debug("Esperando a que la interfaz principal esté lista...");

                // Tiempo máximo total para esperar (45 segundos)
                var timeout = DateTime.Now.AddSeconds(45);

                while (DateTime.Now < timeout)
                {
                    // Verificar mediante JavaScript si los elementos principales de la interfaz son visibles
                    try
                    {
                        var jsResult = (bool)((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    try {
                        // Verificar elementos del mapa
                        if (document.querySelector('.leaflet-container') && 
                            document.querySelector('.leaflet-container').offsetHeight > 100) {
                            return true;
                        }
                        
                        // Verificar listas o paneles de vehículos
                        const vehicleLists = document.querySelectorAll('.vehicle-list, .vehicles-panel, [class*=""vehicle""][class*=""list""]');
                        for (const list of vehicleLists) {
                            if (list.offsetHeight > 100) {
                                return true;
                            }
                        }
                        
                        // Verificar menús o paneles laterales
                        const sidebars = document.querySelectorAll('.sidebar, .side-panel, .control-panel');
                        for (const sidebar of sidebars) {
                            if (sidebar.offsetWidth > 100) {
                                return true;
                            }
                        }
                        
                        // Verificar si hay elementos con nombres de placas visibles
                        const elements = document.querySelectorAll('*');
                        for (const el of elements) {
                            const text = el.textContent || '';
                            if ((text.match(/[A-Z]{3}\d{3}/) || // Formato común de placas
                                 text.match(/[A-Z]{2}\d{4}/) ||
                                 text.match(/[A-Z]{2}\d{3}[A-Z]/)) &&
                                el.offsetWidth > 0 && 
                                el.offsetHeight > 0) {
                                return true;
                            }
                        }
                        
                        return false;
                    } catch(e) {
                        console.error('Error en verificación de interfaz:', e);
                        return false;
                    }
                ");

                        if (jsResult)
                        {
                            _logger.Debug("Interfaz principal verificada como lista");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error en verificación JavaScript de interfaz principal: {ex.Message}");
                    }

                    // Esperar un momento antes de verificar nuevamente
                    await Task.Delay(1000);
                }

                // Si llegamos aquí, se agotó el tiempo máximo
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error al verificar estado de interfaz principal: {ex.Message}");
                return false;
            }
        }      

        private async Task<bool> WaitForVehicleListReady(
        DynamicWaitHelper dynamicWait,
        TimeSpan? maxWait = null)
            {
                maxWait ??= TimeSpan.FromSeconds(60);
                var deadline = DateTime.Now + maxWait.Value;

                while (DateTime.Now < deadline)
                {
                    if (await WaitForVehicleListContainer(dynamicWait) != null)
                        return true;

                    await Task.Delay(500);
                }

                return false;
            }

        private async Task<IWebElement?> WaitForVehicleListContainer(DynamicWaitHelper dynamicWait)
        {
            _logger.Debug("Buscando contenedor de la lista de vehículos…");

            // Selectores observados en la UI clásica y en la versión más reciente
            var selectors = new[]
            {
        ".leaflet-sidebar-content",
        ".vehicle-list",
        ".vehicles-panel",
        ".ng-side-list",
        "#sidebar-content",
        ".vehicle-list-container",
        ".side-nav",
        ".item-veh",
        "div[aria-label*=vehículo]",
        "div[aria-label*=vehículos]"
    };

            foreach (var sel in selectors)
            {                
                // ► el helper se encarga de la espera dinámica.
                var (container, _) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector(sel),
                    $"vehicle_list_container_{sel.GetHashCode()}",
                    ensureClickable: false   // solo queremos visibilidad
                );

                if (container is not null && container.Displayed)
                {
                    _logger.Debug($"Contenedor encontrado con selector: {sel}");
                    return container;
                }
            }

            _logger.Warning("No se localizó el contenedor de la lista de vehículos con los selectores conocidos.");
            return null;
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
