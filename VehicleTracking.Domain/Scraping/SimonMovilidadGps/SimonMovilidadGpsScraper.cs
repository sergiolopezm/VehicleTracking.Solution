using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using VehicleTracking.Domain.Contracts.ISimonMovilidadGps;
using VehicleTracking.Shared.InDTO.InDTOGps;
using VehicleTracking.Util.Helpers;
using VehicleTracking.Utils.Helpers;

namespace VehicleTracking.Domain.Scraping.SimonMovilidadGps
{
    public class SimonMovilidadGpsScraper : ILocationScraper
    {
        private readonly IWebDriver _driver;
        private readonly WebDriverWait _wait;
        private readonly ScrapingLogger _logger;
        private readonly ProviderConfig _config;
        private readonly SeleniumConfig _seleniumConfig;
        private bool _isLoggedIn;
        private string _currentPatent;

        public SimonMovilidadGpsScraper(
         IFileLogger fileLogger,
         IRepositoryLogger logRepository,
         IOptions<TrackingSettings> settings,
         string userId,
         string ip)
        {
            _config = settings.Value.Providers.SimonMovilidad;
            _seleniumConfig = settings.Value.Selenium;
            _logger = new ScrapingLogger(fileLogger, logRepository, userId, ip, "SimonMovilidadScrapingGPS");

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
                options.AddArgument("--disable-blink-features=CredentialManagerAPI"); // evita autocompletado interno
                options.AddUserProfilePreference("safebrowsing.enabled", false);      // desactiva Password Leak Detection
                options.AddArgument("--guest");
                options.AddUserProfilePreference("credentials_enable_service", false);
                options.AddUserProfilePreference("profile.password_manager_enabled", false);
                options.AddArgument("--disable-session-crashed-bubble");
                options.AddArgument("--disable-infobars");
                options.AddUserProfilePreference("profile.default_content_setting_values.notifications", 2);

                // COMENTAR O ELIMINAR ESTA CONDICIÓN PARA FORZAR MODO VISIBLE
                if (_seleniumConfig.Headless)
                {
                    options.AddArgument("--headless");
                }

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

        public async Task<bool> LoginAsync(string username, string password)
        {
            try
            {
                // Limpiar espacios en blanco de las credenciales
                username = username?.Trim() ?? string.Empty;
                password = password?.Trim() ?? string.Empty;

                // Validar que las credenciales no estén vacías
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    _logger.Warning($"Credenciales inválidas para el vehículo {_currentPatent}: usuario o contraseña están vacíos", true);
                    return false;
                }

                _logger.Debug($"Iniciando proceso de login para vehículo {_currentPatent}");
                var dynamicWait = new DynamicWaitHelper(_driver);

                // Navegar directamente al login
                _driver.Navigate().GoToUrl(_config.BaseUrl);
                await CheckPageStatus("navegación inicial");

                // Esperar solo por el campo de usuario que es el primer elemento esencial
                var (userInput, userError) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector("input[type='email'][name='email']"),
                    "login_email",
                    ensureClickable: true
                );

                if (userInput == null)
                {
                    _logger.Warning($"No se pudo encontrar el campo de usuario para el vehículo {_currentPatent}. Detalles del error: {userError}", true);
                    return false;
                }

                // Buscar campo de contraseña
                var (passInput, passError) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector("input[type='password'][name='password']"),
                    "login_password",
                    ensureClickable: true
                );

                if (passInput == null)
                {
                    _logger.Warning($"No se pudo encontrar el campo de contraseña para el vehículo {_currentPatent}. Detalles del error: {passError}", true);
                    return false;
                }

                // Buscar botón de login
                var (loginButton, buttonError) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector("button[type='submit']"),
                    "login_button",
                    ensureClickable: true
                );

                if (loginButton == null)
                {
                    _logger.Warning($"No se pudo encontrar el botón de inicio de sesión para el vehículo {_currentPatent}. Detalles del error: {buttonError}", true);
                    return false;
                }

                // Limpiar y llenar campos inmediatamente
                userInput.Clear();
                passInput.Clear();

                userInput.SendKeys(username);
                passInput.SendKeys(password);

                // Hacer clic en el botón de login
                try
                {
                    loginButton.Click();
                }
                catch
                {
                    IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;
                    js.ExecuteScript("arguments[0].click();", loginButton);
                }

                // Esperar a que aparezca el botón de Rastrear o algún elemento que confirme el login exitoso
                var loginSuccess = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        // Verificar si existe el botón de Rastrear
                        var trackButton = d.FindElement(By.XPath("//button[.//span[text()='Rastrear']]"));
                        return trackButton != null && trackButton.Displayed;
                    }
                    catch
                    {
                        try
                        {
                            // Verificar si existe el título "Vehículos activos"
                            var title = d.FindElement(By.XPath("//h1[contains(text(), 'Vehículos activos')]"));
                            return title != null && title.Displayed;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }, "login_verification", TimeSpan.FromSeconds(10));

                if (!loginSuccess)
                {
                    _logger.Error("No se pudo verificar el login exitoso");
                    return false;
                }

                _logger.Info("Login exitoso verificado");
                _isLoggedIn = true;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error durante el proceso de login para usuario: {username} y vehículo {_currentPatent}", ex);
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

                // Navegación a la sección de vehículos
                await NavigateToVehiclesSection();

                // Buscar el vehículo específico
                var vehicleElement = await FindVehicleInList(patent);
                if (vehicleElement == null)
                {
                    throw new InvalidOperationException($"CONFIGURACION_INVALIDA: No se encontró el vehículo con placa {patent}");
                }

                // Obtener coordenadas
                var (latitude, longitude) = await ExtractCoordinates(dynamicWait);

                // Obtener información de ubicación
                var locationInfo = await ExtractVehicleInformation();

                // Asignar coordenadas
                locationInfo.Latitude = latitude;
                locationInfo.Longitude = longitude;

                // Cerrar popup y hacer logout
                await ClosePopupAndLogout();

                _logger.Info($"[Tiempo TOTAL del proceso: {stopwatch.ElapsedMilliseconds}ms] Proceso completado exitosamente", true);
                _logger.Info($"Coordenadas obtenidas: {latitude}, {longitude}", true);

                return locationInfo;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error al obtener ubicación del vehículo {patent}", ex);
                throw;
            }
        }

        private async Task ClosePopupAndLogout()
        {
            try
            {
                _logger.Debug("Iniciando proceso de cierre de popup y logout");
                var dynamicWait = new DynamicWaitHelper(_driver);

                // Primer intento: Cerrar mediante el botón X
                var (closeButton, closeError) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector("img[alt='close'][role='button']"),
                    "close_button",
                    ensureClickable: true
                );

                if (closeButton != null && closeButton.Displayed)
                {
                    _logger.Debug("Botón de cierre encontrado, intentando cerrar popup");
                    await ClickElementWithRetry(closeButton);
                }
                else
                {
                    _logger.Debug("Botón de cierre no encontrado, intentando clic fuera del popup");

                    // Clic fuera del popup
                    try
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    var popup = document.querySelector('.fixed.inset-0');
                    if (popup) {
                        var clickEvent = new MouseEvent('click', {
                            bubbles: true,
                            cancelable: true,
                            view: window,
                            clientX: 0,
                            clientY: 0
                        });
                        document.body.dispatchEvent(clickEvent);
                    }
                ");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error al intentar clic fuera del popup: {ex.Message}");
                    }
                }

                // Esperar a que el popup se cierre
                await dynamicWait.WaitForConditionAsync(
                    d =>
                    {
                        try
                        {
                            var popup = d.FindElement(By.CssSelector("div[role='dialog']"));
                            return !popup.Displayed;
                        }
                        catch
                        {
                            return true;
                        }
                    },
                    "popup_closed"
                );

                // Buscar y hacer clic en el botón de cerrar sesión
                var (logoutButton, logoutError) = await dynamicWait.WaitForElementAsync(
                    By.XPath("//button[contains(text(), 'Cerrar sesión')]"),
                    "logout_button",
                    ensureClickable: true
                );

                if (logoutButton != null && logoutButton.Displayed)
                {
                    _logger.Debug("Botón de cierre de sesión encontrado, procediendo con logout");
                    await ClickElementWithRetry(logoutButton);
                    _isLoggedIn = false;

                    // Esperar a que vuelva a la página de login
                    var loginRedirect = await dynamicWait.WaitForConditionAsync(
                        d =>
                        {
                            try
                            {
                                return d.Url.Contains("/login");
                            }
                            catch
                            {
                                return false;
                            }
                        },
                        "login_redirect"
                    );

                    if (loginRedirect)
                    {
                        _logger.Info("Logout completado exitosamente", true);
                    }
                    else
                    {
                        _logger.Warning("No se pudo verificar el retorno a la página de login");
                    }
                }
                else
                {
                    _logger.Warning($"No se encontró el botón de cierre de sesión. Error: {logoutError}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error durante el proceso de cierre de popup y logout", ex);
                throw;
            }
        }

        private async Task<bool> ClickElementWithRetry(IWebElement element, int maxAttempts = 3)
        {
            var dynamicWait = new DynamicWaitHelper(_driver);

            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    if (!element.Displayed || !element.Enabled)
                    {
                        _logger.Warning($"Elemento no visible/habilitado en intento {i + 1}");
                        continue;
                    }

                    // Verificar interactividad del elemento
                    var isClickable = await dynamicWait.WaitForConditionAsync(d =>
                    {
                        try
                        {
                            return (bool)((IJavaScriptExecutor)d).ExecuteScript(@"
                       var elem = arguments[0];
                       var rect = elem.getBoundingClientRect();
                       var isInViewport = (
                           rect.top >= 0 &&
                           rect.left >= 0 && 
                           rect.bottom <= window.innerHeight &&
                           rect.right <= window.innerWidth
                       );
                       var styles = window.getComputedStyle(elem);
                       return isInViewport && 
                              elem.offsetWidth > 0 &&
                              elem.offsetHeight > 0 &&
                              !elem.disabled &&
                              styles.visibility !== 'hidden' &&
                              styles.display !== 'none' &&
                              parseFloat(styles.opacity) > 0;
                   ", element);
                        }
                        catch
                        {
                            return false;
                        }
                    }, "element_clickable", TimeSpan.FromMilliseconds(100));

                    if (!isClickable) continue;

                    // Intentar clic normal
                    try
                    {
                        element.Click();
                        return true;
                    }
                    catch
                    {
                        // Intentar clic con JavaScript
                        var clicked = await dynamicWait.WaitForConditionAsync(d =>
                        {
                            try
                            {
                                ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", element);
                                return true;
                            }
                            catch
                            {
                                return false;
                            }
                        }, "js_click", TimeSpan.FromMilliseconds(100));

                        if (clicked) return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Intento {i + 1} fallido: {ex.Message}");
                    if (i == maxAttempts - 1) throw;
                }
            }

            return false;
        }

        private async Task NavigateToVehiclesSection()
        {
            try
            {
                _logger.Debug("Iniciando navegación a sección de vehículos");
                var dynamicWait = new DynamicWaitHelper(_driver);

                // Esperar y hacer clic en el botón de rastreo
                var (trackButton, trackButtonError) = await dynamicWait.WaitForElementAsync(
                    By.XPath("//button[.//span[text()='Rastrear']]"),
                    "track_button",
                    ensureClickable: true
                );

                if (trackButton == null)
                {
                    _logger.Warning($"No se pudo encontrar el botón de rastreo. Detalles del error: {trackButtonError}", true);
                    throw new InvalidOperationException("No se pudo encontrar el botón de rastreo");
                }

                // Hacer clic en el botón
                trackButton.Click();

                // Verificar el estado de la página después de la navegación
                await CheckPageStatus("navegación a sección de vehículos");

                // Esperar a que la URL cambie
                var urlChanged = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        var currentUrl = d.Url;
                        return currentUrl.Contains("startPosition") || currentUrl.Contains("zoom=");
                    }
                    catch
                    {
                        return false;
                    }
                }, "url_changed");

                if (!urlChanged)
                {
                    throw new InvalidOperationException("La URL no cambió después de hacer clic en Rastrear");
                }

                // Esperar a que el marcador del vehículo esté presente
                var markerPresent = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        return d.FindElements(By.CssSelector(
                            "div.leaflet-marker-icon.leaflet-zoom-animated.leaflet-interactive[tabindex='0']"
                        )).Any(m => m.Displayed);
                    }
                    catch
                    {
                        return false;
                    }
                }, "marker_visible");

                if (!markerPresent)
                {
                    throw new InvalidOperationException("No se pudo encontrar el marcador del vehículo");
                }

                _logger.Info("Navegación a sección de vehículos completada exitosamente", true);
            }
            catch (Exception ex)
            {
                _logger.Error("Error en NavigateToVehiclesSection", ex);
                throw;
            }
        }

        private async Task<IWebElement?> FindVehicleInList(string patent)
        {
            try
            {
                var dynamicWait = new DynamicWaitHelper(_driver);

                // Esperar al icono del vehículo con los selectores específicos y atributos
                var (vehicleIcon, vehicleIconError) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector("div.leaflet-marker-icon.leaflet-zoom-animated.leaflet-interactive[tabindex='0']"),
                    "vehicle_icon",
                    ensureClickable: true
                );

                if (vehicleIcon == null)
                {
                    _logger.Warning($"No se pudo encontrar el icono del vehículo. Detalles del error: {vehicleIconError}", true);

                    // Intentar con un selector más específico si el primero falla
                    var (specificIcon, specificIconError) = await dynamicWait.WaitForElementAsync(
                        By.CssSelector("div.leaflet-marker-icon:has(img[src*='car.png'])"),
                        "vehicle_icon_specific",
                        ensureClickable: true
                    );

                    if (specificIcon == null)
                    {
                        _logger.Warning($"No se pudo encontrar el icono del vehículo con selector específico. Detalles del error: {specificIconError}", true);
                        throw new InvalidOperationException($"CONFIGURACION_INVALIDA: No se encontró el vehículo con placa {patent}");
                    }

                    vehicleIcon = specificIcon;
                }

                if (vehicleIcon == null)
                {
                    _logger.Warning("No se pudo encontrar el icono del vehículo", true);
                    throw new InvalidOperationException($"CONFIGURACION_INVALIDA: No se encontró el vehículo con placa {patent}");
                }

                // Verificar que el icono esté realmente interactuable
                var iconReady = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        var icon = d.FindElement(By.CssSelector("div.leaflet-marker-icon.leaflet-zoom-animated.leaflet-interactive[tabindex='0']"));
                        var style = icon.GetAttribute("style") ?? "";
                        var transform = style.Contains("transform: translate3d");
                        var hasImage = icon.FindElements(By.TagName("img")).Any(img => img.Displayed);

                        return icon.Displayed &&
                               icon.Enabled &&
                               transform &&
                               hasImage &&
                               icon.GetAttribute("tabindex") == "0";
                    }
                    catch
                    {
                        return false;
                    }
                }, "icon_interactive");

                if (!iconReady)
                {
                    throw new InvalidOperationException("El icono del vehículo no está en un estado interactuable");
                }

                _logger.Debug("Intentando hacer clic en el icono del vehículo");

                // Hacer clic en el icono usando JavaScript para mayor precisión
                ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            var icon = arguments[0];
            var clickEvent = new MouseEvent('click', {
                view: window,
                bubbles: true,
                cancelable: true
            });
            icon.dispatchEvent(clickEvent);
        ", vehicleIcon);

                // Verificar que el clic fue exitoso esperando el popup
                var popupVisible = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        var popup = d.FindElement(By.CssSelector(".leaflet-popup-content"));
                        return popup != null && popup.Displayed;
                    }
                    catch
                    {
                        return false;
                    }
                }, "popup_visible");

                if (!popupVisible)
                {
                    // Intentar clic alternativo si el primer intento falló
                    try
                    {
                        vehicleIcon.Click();
                    }
                    catch
                    {
                        // Si falla el clic directo, intentar con Actions
                        var actions = new OpenQA.Selenium.Interactions.Actions(_driver);
                        actions.MoveToElement(vehicleIcon).Click().Perform();
                    }
                }

                _logger.Info($"Vehículo {patent} encontrado y seleccionado exitosamente", true);
                return vehicleIcon;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error buscando vehículo {patent}", ex);
                throw;
            }
        }

        private async Task<LocationDataInfo> ExtractVehicleInformation()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Iniciando extracción de información del vehículo");
                var dynamicWait = new DynamicWaitHelper(_driver);

                // Primera parte: Obtener información del popup inicial
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Obteniendo información del popup inicial");
                var popupInfo = await GetInitialPopupInfo(dynamicWait);
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Información del popup obtenida");

                // Segunda parte: Navegar y obtener información detallada
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Iniciando navegación a detalles");
                await NavigateToDetails(dynamicWait);
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Navegación a detalles completada");

                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Obteniendo información detallada");
                var detailsInfo = await GetDetailedInfo(dynamicWait);
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Información detallada obtenida");

                // Intentar obtener ubicación si está disponible
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Iniciando obtención de ubicación");
                var georeference = await TryGetLocationReference(dynamicWait);
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Proceso de obtención de ubicación completado");

                // Combinar y mapear la información
                var result = MapToLocationDataInfo(popupInfo, detailsInfo, georeference);
                _logger.Info($"[T+{stopwatch.ElapsedMilliseconds}ms] Proceso de extracción completado", true);

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"[T+{stopwatch.ElapsedMilliseconds}ms] Error extrayendo información del vehículo", ex);
                throw;
            }
        }

        private async Task<string> TryGetLocationReference(DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug("Iniciando obtención de ubicación");

                // Primero verificar si la ubicación ya está visible sin necesidad del botón
                var initialValue = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            var input = document.querySelector('input[aria-label=""Ubicación""]');
            return input ? input.value : '';
        ") as string;

                if (!string.IsNullOrEmpty(initialValue) && !initialValue.Contains("Obtener ubicación"))
                {
                    _logger.Info($"Ubicación encontrada directamente: {initialValue}", true);
                    return initialValue;
                }

                // Si no hay ubicación, intentar encontrar y hacer clic en el botón
                var buttonClicked = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            var button = document.querySelector('button.absolute.m-8.items-center.text-primary');
            if (button) {
                button.click();
                return true;
            }
            return false;
        ") as bool?;

                if (!buttonClicked.GetValueOrDefault())
                {
                    var (locationButton, _) = await dynamicWait.WaitForElementAsync(
                        By.CssSelector("button.absolute.m-8.items-center.text-primary"),
                        "location_button",
                        ensureClickable: true
                    );

                    if (locationButton != null && locationButton.Displayed && locationButton.Enabled)
                    {
                        _logger.Debug("Botón 'Obtener ubicación' encontrado, ejecutando clic");
                        try
                        {
                            locationButton.Click();
                        }
                        catch
                        {
                            ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", locationButton);
                        }
                    }
                }

                // Esperar a que la ubicación se actualice
                var locationUpdated = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        var input = d.FindElement(By.CssSelector("input[aria-label='Ubicación']"));
                        var currentValue = input.GetAttribute("value")?.Trim();
                        return !string.IsNullOrEmpty(currentValue) &&
                               !currentValue.Contains("Obtener ubicación") &&
                               !currentValue.Contains("cargando");
                    }
                    catch
                    {
                        return false;
                    }
                }, "location_update", TimeSpan.FromSeconds(10));

                if (locationUpdated)
                {
                    var finalInput = _driver.FindElement(By.CssSelector("input[aria-label='Ubicación']"));
                    var finalValue = finalInput.GetAttribute("value")?.Trim();

                    if (!string.IsNullOrEmpty(finalValue))
                    {
                        _logger.Info($"Ubicación obtenida después del clic: {finalValue}", true);
                        return finalValue;
                    }
                }

                _logger.Warning("No se pudo obtener la ubicación", true);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.Error("Error en TryGetLocationReference", ex);
                return string.Empty;
            }
        }

        private async Task<Dictionary<string, string>> GetInitialPopupInfo(DynamicWaitHelper dynamicWait)
        {
            var popupInfo = new Dictionary<string, string>();

            // Esperar a que el popup esté presente y visible
            var (popup, _) = await dynamicWait.WaitForElementAsync(
                By.CssSelector(".leaflet-popup-content"),
                "popup_content",
                ensureClickable: false
            );

            if (popup != null)
            {
                _logger.Debug("Popup encontrado, extrayendo información");
                // Extraer información del popup usando los dt/dd pairs
                var dtElements = popup.FindElements(By.TagName("dt"));
                var ddElements = popup.FindElements(By.TagName("dd"));

                for (int i = 0; i < dtElements.Count; i++)
                {
                    var key = dtElements[i].Text.Trim(':');
                    var value = ddElements[i].Text;
                    popupInfo[key] = value;
                    _logger.Debug($"Campo extraído - {key}: {value}");
                }
            }

            return popupInfo;
        }

        private async Task NavigateToDetails(DynamicWaitHelper dynamicWait)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Iniciando navegación a detalles");

            // Encontrar y hacer clic en el botón "Ver detalle"
            var (detailButton, _) = await dynamicWait.WaitForElementAsync(
                By.CssSelector("button a.link-button[href*='/details']"),
                "detail_button",
                ensureClickable: true
            );

            if (detailButton != null)
            {
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Botón 'Ver detalle' encontrado, ejecutando clic");
                detailButton.Click();
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Clic ejecutado, esperando carga de página");

                // Verificar el estado de la página después de navegar a detalles
                await CheckPageStatus("navegación a página de detalles");

                // Esperar primero a que la URL cambie
                var urlChanged = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        return d.Url.Contains("/details");
                    }
                    catch
                    {
                        return false;
                    }
                }, "url_change");

                if (urlChanged)
                {
                    _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] URL cambiada correctamente");

                    // Esperar a que el contenedor principal esté presente
                    var pageLoaded = await dynamicWait.WaitForConditionAsync(d =>
                    {
                        try
                        {
                            // Verificar el contenedor principal que tiene el título y el contenido
                            var container = d.FindElement(By.CssSelector("div.flex.flex-1.flex-col.gap-3"));
                            if (!container.Displayed) return false;

                            return d.FindElements(By.CssSelector("div[aria-label='Ubicación']")).Any() ||
                                   d.FindElements(By.CssSelector("input[aria-label='Ubicación']")).Any();
                        }
                        catch
                        {
                            return false;
                        }
                    }, "page_load");

                    if (!pageLoaded)
                    {
                        throw new InvalidOperationException("La página de detalles no cargó correctamente");
                    }

                    _logger.Info($"[T+{stopwatch.ElapsedMilliseconds}ms] Página de detalles cargada completamente", true);
                    return;
                }

                throw new InvalidOperationException("La URL no cambió a la página de detalles");
            }

            throw new InvalidOperationException("No se encontró el botón de Ver detalle");
        }

        private async Task<Dictionary<string, string>> GetDetailedInfo(DynamicWaitHelper dynamicWait)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var detailsInfo = new Dictionary<string, string>();

            _logger.Info($"[T+{stopwatch.ElapsedMilliseconds}ms] Iniciando obtención de información detallada");

            try
            {
                // Verificación rápida inicial de elementos visibles
                var quickCheck = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        var avlContainer = d.FindElement(By.XPath("//h4[contains(text(), 'Información AVL')]//following-sibling::div"));
                        if (avlContainer != null && avlContainer.Displayed)
                        {
                            var inputs = avlContainer.FindElements(By.CssSelector("input[aria-label]"));
                            return inputs.Any(i => i.Displayed && i.Enabled);
                        }
                        return false;
                    }
                    catch { return false; }
                }, "quick_info_check", TimeSpan.FromSeconds(2));

                if (quickCheck)
                {
                    _logger.Debug("Elementos encontrados en verificación rápida, procesando información");
                    var avlContainer = _driver.FindElement(By.XPath("//h4[contains(text(), 'Información AVL')]//following-sibling::div"));
                    var inputs = avlContainer.FindElements(By.CssSelector("input[aria-label]"));

                    foreach (var input in inputs.Where(i => i.Displayed && i.Enabled))
                    {
                        try
                        {
                            var label = input.GetAttribute("aria-label");
                            var value = input.GetAttribute("value");
                            if (!string.IsNullOrEmpty(label) && value != null)
                            {
                                detailsInfo[label] = value;
                                _logger.Info($"Campo encontrado rápidamente: {label} = {value}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning($"Error al procesar un campo en verificación rápida: {ex.Message}");
                            continue;
                        }
                    }

                    // Verificación rápida de información del vehículo
                    try
                    {
                        var vehicleContainer1 = _driver.FindElement(By.XPath("//h4[contains(text(), 'Información del vehículo')]//following-sibling::div"));
                        if (vehicleContainer1 != null && vehicleContainer1.Displayed)
                        {
                            var vehicleInputs = vehicleContainer1.FindElements(By.CssSelector("input[aria-label]"));
                            foreach (var input in vehicleInputs.Where(i => i.Displayed && i.Enabled))
                            {
                                var label = input.GetAttribute("aria-label");
                                var value = input.GetAttribute("value");
                                if (!string.IsNullOrEmpty(label) && value != null)
                                {
                                    detailsInfo[label] = value;
                                    _logger.Info($"Campo de vehículo encontrado rápidamente: {label} = {value}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error en verificación rápida de información del vehículo: {ex.Message}");
                    }

                    if (detailsInfo.Count > 0)
                    {
                        _logger.Info($"[T+{stopwatch.ElapsedMilliseconds}ms] Información obtenida exitosamente en verificación rápida");
                        return detailsInfo;
                    }
                }

                // Si la verificación rápida no fue suficiente, continuar con el proceso completo
                _logger.Debug($"[T+{stopwatch.ElapsedMilliseconds}ms] Iniciando proceso completo de obtención de información");

                // Esperar a que el contenedor de información AVL esté presente
                var (avlContainerFull, avlContainerError) = await dynamicWait.WaitForElementAsync(
                    By.XPath("//h4[contains(text(), 'Información AVL')]//following-sibling::div"),
                    "avl_container",
                    ensureClickable: false
                );

                if (avlContainerFull != null)
                {
                    var inputs = avlContainerFull.FindElements(By.CssSelector("input[aria-label]"));
                    foreach (var input in inputs)
                    {
                        try
                        {
                            var label = input.GetAttribute("aria-label");
                            var value = input.GetAttribute("value");
                            if (!string.IsNullOrEmpty(label) && value != null)
                            {
                                detailsInfo[label] = value;
                                _logger.Info($"Campo encontrado: {label} = {value}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning($"Error al procesar un campo: {ex.Message}");
                            continue;
                        }
                    }
                }
                else
                {
                    _logger.Warning($"No se encontró el contenedor de información AVL para el vehículo {_currentPatent}. Detalles del error: {avlContainerError}", true);
                }

                // También buscar en la información del vehículo
                var (vehicleContainer, vehicleContainerError) = await dynamicWait.WaitForElementAsync(
                    By.XPath("//h4[contains(text(), 'Información del vehículo')]//following-sibling::div"),
                    "vehicle_container",
                    ensureClickable: false
                );

                if (vehicleContainer != null)
                {
                    var inputs = vehicleContainer.FindElements(By.CssSelector("input[aria-label]"));
                    foreach (var input in inputs)
                    {
                        try
                        {
                            var label = input.GetAttribute("aria-label");
                            var value = input.GetAttribute("value");
                            if (!string.IsNullOrEmpty(label) && value != null)
                            {
                                detailsInfo[label] = value;
                                _logger.Info($"Campo encontrado: {label} = {value}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning($"Error al procesar un campo: {ex.Message}");
                            continue;
                        }
                    }
                }
                else
                {
                    _logger.Warning($"No se encontró el contenedor de información del vehículo para el vehículo {_currentPatent}. Detalles del error: {vehicleContainerError}", true);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error al obtener información detallada", ex);
                throw;
            }

            _logger.Info($"[T+{stopwatch.ElapsedMilliseconds}ms] Información detallada obtenida. Campos encontrados: {detailsInfo.Count}");
            return detailsInfo;
        }

        private LocationDataInfo MapToLocationDataInfo(
        Dictionary<string, string> popupInfo,
        Dictionary<string, string> detailsInfo,
        string georeference)
        {
            // Construir un resumen completo para el campo Reason
            var summaryParts = new List<string>();

            // Información del primer popup
            foreach (var kvp in popupInfo)
            {
                summaryParts.Add($"{kvp.Key}: {kvp.Value}");
            }

            // Información del vehículo
            AddIfExists(detailsInfo, summaryParts, "Placa");
            AddIfExists(detailsInfo, summaryParts, "Vin");
            AddIfExists(detailsInfo, summaryParts, "Modelo");
            AddIfExists(detailsInfo, summaryParts, "Compañía");
            AddIfExists(detailsInfo, summaryParts, "Cliente");
            AddIfExists(detailsInfo, summaryParts, "Marca");
            AddIfExists(detailsInfo, summaryParts, "Línea");

            // Información AVL
            AddIfExists(detailsInfo, summaryParts, "Velocidad");
            AddIfExists(detailsInfo, summaryParts, "Ángulo");
            AddIfExists(detailsInfo, summaryParts, "Satélite");
            AddIfExists(detailsInfo, summaryParts, "Batería Vehículo", "Bateria Vehiculo");
            AddIfExists(detailsInfo, summaryParts, "Batería Dispositivo", "Bateria Dispositivo");
            AddIfExists(detailsInfo, summaryParts, "Cobertura");
            AddIfExists(detailsInfo, summaryParts, "Odómetro AVL", "Odometro AVL");

            // Información CAN
            AddIfExists(detailsInfo, summaryParts, "RPM");
            AddIfExists(detailsInfo, summaryParts, "Temperatura Motor");
            AddIfExists(detailsInfo, summaryParts, "Nivel Combustible");
            AddIfExists(detailsInfo, summaryParts, "Combustible Consumido");
            AddIfExists(detailsInfo, summaryParts, "Odómetro CAN", "Odometro CAN");

            // Agregar ubicación si está disponible
            if (!string.IsNullOrEmpty(georeference))
                summaryParts.Add($"Ubicación: {georeference}");

            // Combinar toda la información
            var reason = string.Join(" | ", summaryParts);
            if (reason.Length > 2000)
                reason = reason.Substring(0, 1997) + "...";

            // Extraer velocidad del popup inicial o de los detalles
            var speed = popupInfo.GetValueOrDefault("Velocidad")?.Replace("km/h", "").Trim()
                      ?? detailsInfo.GetValueOrDefault("Velocidad")?.Replace("Km/h", "").Trim()
                      ?? "0";

            // Extraer el estado/evento del vehículo
            var evento = popupInfo.GetValueOrDefault("Evento") ?? detailsInfo.GetValueOrDefault("Estado");

            // Extraer el odómetro (primero intentar AVL, luego CAN)
            var distanceTraveled = 0m;
            if (detailsInfo.TryGetValue("Odómetro AVL", out var odometerStr) ||
                detailsInfo.TryGetValue("Odometro AVL", out odometerStr))
            {
                distanceTraveled = ParseDecimal(odometerStr?.Replace("Km", "").Replace(",", "").Trim() ?? "0");
                _logger.Info($"Odómetro AVL encontrado: {odometerStr} -> {distanceTraveled}");
            }
            else if (detailsInfo.TryGetValue("Odómetro CAN", out odometerStr) ||
                     detailsInfo.TryGetValue("Odometro CAN", out odometerStr))
            {
                distanceTraveled = ParseDecimal(odometerStr?.Replace("Km", "").Replace(",", "").Trim() ?? "0");
                _logger.Info($"Odómetro CAN encontrado: {odometerStr} -> {distanceTraveled}");
            }
            else
            {
                _logger.Warning("No se encontró el valor del odómetro");
                // Para debugging, mostrar todas las claves disponibles
                foreach (var key in detailsInfo.Keys)
                {
                    _logger.Debug($"Clave disponible: '{key}'");
                }
            }

            // Extraer el ángulo
            var angle = 0m;
            if (detailsInfo.TryGetValue("Ángulo", out var angleStr) || detailsInfo.TryGetValue("Angulo", out angleStr))
            {
                angle = ParseDecimal(angleStr);
                _logger.Info($"Ángulo encontrado: {angleStr} -> {angle}");
            }
            else
            {
                _logger.Warning("No se encontró el valor del ángulo");
            }

            return new LocationDataInfo
            {
                Speed = ParseDecimal(speed),
                Timestamp = ParseDateTime(popupInfo.GetValueOrDefault("Fecha evento") ??
                                        detailsInfo.GetValueOrDefault("Fecha") ??
                                        DateTime.Now.ToString()),
                Driver = evento ?? string.Empty,
                Georeference = georeference,
                InZone = detailsInfo.GetValueOrDefault("Cobertura") ?? "No se encontró información",
                DetentionTime = detailsInfo.GetValueOrDefault("Estado") == "Apagado" ?
                    "Vehículo detenido" : "En movimiento",
                DistanceTraveled = distanceTraveled,
                Temperature = ParseDecimal(
                    detailsInfo.GetValueOrDefault("Temperatura Motor")?.Replace("°C", "").Replace("-", "0").Trim() ??
                    "0"
                ),
                Angle = angle,
                Reason = reason,
                Latitude = 0,
                Longitude = 0
            };
        }

        private async Task<(decimal latitude, decimal longitude)> ExtractCoordinates(DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug("Iniciando extracción de coordenadas");

                // Verificación rápida del marcador
                var quickMarkerCheck = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        var marker = d.FindElement(By.CssSelector("div.leaflet-marker-icon.leaflet-zoom-animated.leaflet-interactive[tabindex='0']"));
                        return marker != null && marker.Displayed;
                    }
                    catch { return false; }
                }, "quick_marker_check", TimeSpan.FromSeconds(2));

                if (quickMarkerCheck)
                {
                    _logger.Debug("Marcador encontrado en verificación rápida");
                    try
                    {
                        var coordinates = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    try {
                        var map = document.querySelector('.leaflet-map-pane');
                        if (!map || !map.leafletMap) return null;
                        
                        var marker = document.querySelector('div.leaflet-marker-icon.leaflet-zoom-animated.leaflet-interactive[tabindex=""0""]');
                        if (!marker) return null;

                        var transform = marker.style.transform;
                        var matches = transform.match(/translate3d\(([^,]+),\s*([^,]+),/);
                        if (!matches) return null;

                        var x = parseFloat(matches[1]);
                        var y = parseFloat(matches[2]);
                        
                        var point = L.point(x, y);
                        var latLng = map.leafletMap.layerPointToLatLng(point);
                        
                        return [latLng.lat, latLng.lng];
                    } catch(e) {
                        console.error('Error:', e);
                        return null;
                    }
                ") as double[];

                        if (coordinates != null && coordinates.Length == 2)
                        {
                            _logger.Info($"Coordenadas extraídas rápidamente: {coordinates[0]}, {coordinates[1]}");
                            return ((decimal)coordinates[0], (decimal)coordinates[1]);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error en extracción rápida de coordenadas: {ex.Message}");
                    }
                }

                // Si la verificación rápida falla, continuar con el proceso original
                var currentUrl = _driver.Url;
                _logger.Debug($"Extrayendo coordenadas de URL: {currentUrl}");

                if (currentUrl.Contains("startPositionAt"))
                {
                    var startPosition = currentUrl.Split(new[] { "startPositionAt=" }, StringSplitOptions.None)[1]
                                               .Split('&')[0]
                                               .Replace("%5B", "[")
                                               .Replace("%2C", ",")
                                               .Replace("%5D", "]")
                                               .Trim('[', ']');

                    var coordinates = startPosition.Split(',');
                    if (coordinates.Length == 2)
                    {
                        if (decimal.TryParse(coordinates[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var lat) &&
                            decimal.TryParse(coordinates[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var lon))
                        {
                            _logger.Info($"Coordenadas extraídas de URL: {lat}, {lon}", true);
                            return (lat, lon);
                        }
                    }
                }

                // Verificación del marcador del vehículo
                var (marker, markerError) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector("div.leaflet-marker-icon.leaflet-zoom-animated.leaflet-interactive"),
                    "vehicle_marker",
                    ensureClickable: false
                );

                if (marker != null)
                {
                    var style = marker.GetAttribute("style");
                    _logger.Info($"Estilo del marcador: {style}");

                    if (style != null && style.Contains("translate3d"))
                    {
                        var map = _driver.FindElement(By.CssSelector(".leaflet-map-pane"));
                        var mapBounds = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    var map = document.querySelector('.leaflet-map-pane');
                    if (!map) return null;
                    var bounds = map.getBoundingClientRect();
                    return {
                        width: bounds.width,
                        height: bounds.height,
                        left: bounds.left,
                        top: bounds.top
                    };
                ") as Dictionary<string, object>;

                        if (mapBounds != null)
                        {
                            var coordinates = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                        var marker = arguments[0];
                        var map = document.querySelector('.leaflet-map-pane');
                        if (!map || !marker) return null;
                        
                        var transform = marker.style.transform;
                        var matches = transform.match(/translate3d\(([^,]+),\s*([^,]+),/);
                        if (!matches) return null;
                        
                        var x = parseFloat(matches[1]);
                        var y = parseFloat(matches[2]);
                        
                        var point = L.point(x, y);
                        var latLng = map.leafletMap.layerPointToLatLng(point);
                        
                        return [latLng.lat, latLng.lng];
                    ", marker) as double[];

                            if (coordinates != null && coordinates.Length == 2)
                            {
                                _logger.Info($"Coordenadas extraídas del marcador: {coordinates[0]}, {coordinates[1]}");
                                return ((decimal)coordinates[0], (decimal)coordinates[1]);
                            }
                        }
                    }
                }

                _logger.Warning($"No se pudieron extraer las coordenadas para el vehículo {_currentPatent}. Detalles del error: {markerError}", true);
                return (0, 0);
            }
            catch (Exception ex)
            {
                _logger.Error("Error extrayendo coordenadas", ex);
                return (0, 0);
            }
        }

        private void AddIfExists(Dictionary<string, string> dict, List<string> parts, string key, string alternateKey = null)
        {
            if (dict.TryGetValue(key, out var value))
            {
                parts.Add($"{key}: {value}");
            }
            else if (alternateKey != null && dict.TryGetValue(alternateKey, out value))
            {
                parts.Add($"{alternateKey}: {value}");
            }
        }

        private decimal ParseDecimal(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0m;
            value = value.Replace("-", "0")
                         .Replace("NaN", "0")
                         .Replace(",", ".");
            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0m;
        }

        private DateTime ParseDateTime(string value)
        {
            return DateTime.TryParse(value, out var result) ? result : DateTime.Now;
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
