using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using VehicleTracking.Domain.Contracts.IDetektorGps;
using VehicleTracking.Shared.InDTO.InDTOGps;
using VehicleTracking.Util.Helpers;
using VehicleTracking.Utils.Helpers;

namespace VehicleTracking.Domain.Scraping.DetektorGps;

public class DetektorGpsScraper : ILocationScraper
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly ScrapingLogger _logger;
    private readonly ProviderConfig _config;
    private readonly SeleniumConfig _seleniumConfig;
    private bool _isLoggedIn;
    private string _currentPatent;
    private bool _isBetaFlow;

    public DetektorGpsScraper(
        IFileLogger fileLogger,
        IRepositoryLogger logRepository,
        IOptions<TrackingSettings> settings,
        string userId,
        string ip)
    {
        _config = settings.Value.Providers.Detektor;
        _seleniumConfig = settings.Value.Selenium;
        _logger = new ScrapingLogger(fileLogger, logRepository, userId, ip, "DetektorScraping");

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

            _logger.Debug("Navegando a la URL base...");
            _driver.Navigate().GoToUrl(_config.BaseUrl);

            await CheckPageStatus("navegación inicial");

            _logger.Debug("Esperando que la página cargue completamente...");
            await dynamicWait.WaitForPageLoadAsync();

            _logger.Debug("Buscando campo de usuario...");
            var (userInput, userError) = await dynamicWait.WaitForElementAsync(
                By.CssSelector("input[name='username'].form-control"),
                "login_username",
                ensureClickable: true
            );

            if (userInput == null)
            {
                _logger.Warning($"No se pudo encontrar el campo de usuario para el vehículo {_currentPatent}. Detalles del error: {userError}", true);
                return false;
            }

            _logger.Debug("Buscando campo de contraseña...");
            var (passInput, passError) = await dynamicWait.WaitForElementAsync(
                By.CssSelector("input[name='password'].form-control"),
                "login_password",
                ensureClickable: true);

            if (passInput == null)
            {
                _logger.Warning($"No se pudo encontrar el campo de contraseña para el vehículo {_currentPatent}. Detalles del error: {passError}", true);
                return false;
            }

            _logger.Debug("Buscando botón de login...");
            var (loginButton, buttonError) = await dynamicWait.WaitForElementAsync(
                By.Id("initSession"),
                "login_button",
                ensureClickable: true);

            if (loginButton == null)
            {
                _logger.Warning($"No se pudo encontrar el botón de inicio de sesión para el vehículo {_currentPatent}. Detalles del error: {buttonError}", true);
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
            await ClickWhenClickableAsync(By.Id("initSession"));

            // Manejar posible popup de contraseña
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

            _logger.Debug("Login exitoso, esperando carga de página principal...");
            await dynamicWait.WaitForPageLoadAsync();
            await dynamicWait.WaitForAjaxCompletionAsync();

            _logger.Debug("Buscando botón Skytrack...");
            var skytrackButton = await FindSkytrackButtonWithRetry(dynamicWait);
            if (skytrackButton == null)
            {
                _logger.Warning("No se pudo encontrar el botón de Skytrack después de múltiples intentos", true);
                return false;
            }

            _logger.Debug("Botón Skytrack encontrado, intentando hacer clic...");
            await ClickWhenClickableAsync(By.Id("idBtnProductSkytrack"), cachedElement: skytrackButton);

            _logger.Debug("Esperando apertura de nueva pestaña...");
            await dynamicWait.WaitForConditionAsync(
                d => d.WindowHandles.Count > 1,
                "new_tab_opened",
                TimeSpan.FromSeconds(3)
            );

            var windows = _driver.WindowHandles;
            _driver.SwitchTo().Window(windows.Last());

            // Verificar estado después del cambio de ventana
            await CheckPageStatus("cambio de ventana post-login");

            _logger.Debug("Esperando carga de página post-login...");

            // Verificación post-login con múltiples intentos
            int maxAttempts = 3;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    // Esperar que la página cargue con contexto específico
                    var pageLoaded = await dynamicWait.WaitForPageLoadAsync("post_login");
                    if (!pageLoaded && attempt == maxAttempts - 1)
                    {
                        _logger.Warning("Timeout esperando carga de página post-login", true);
                        return false;
                    }

                    // Intentar verificar el menú con diferentes selectores
                    var (menuElement, _) = await dynamicWait.WaitForElementAsync(
                        By.CssSelector("td.myMenu, div.myMenu, .myMenu"),
                        "post_login_menu",
                        ensureClickable: false
                    );

                    if (menuElement != null && menuElement.Displayed)
                    {
                        _logger.Info("Verificación post-login exitosa", true);
                        _isLoggedIn = true;
                        return true;
                    }

                    // Verificación alternativa por URL
                    var currentUrl = _driver.Url.ToLower();
                    if (currentUrl.Contains("skytrack") || currentUrl.Contains("detektor"))
                    {
                        // Verificación adicional del estado de la página
                        var isPageReady = await dynamicWait.WaitForConditionAsync(driver =>
                        {
                            try
                            {
                                var readyState = ((IJavaScriptExecutor)driver)
                                    .ExecuteScript("return document.readyState")
                                    ?.ToString();

                                if (readyState != "complete")
                                    return false;

                                // Verificar que no hay elementos de carga visibles
                                var loadingElements = driver.FindElements(By.CssSelector(".loading, .wait, .spinner, [class*='loading']"));
                                return !loadingElements.Any(e => e.Displayed);
                            }
                            catch
                            {
                                return false;
                            }
                        }, "page_state_check");

                        if (isPageReady)
                        {
                            _logger.Info("Login verificado por URL y estado de página", true);
                            _isLoggedIn = true;

                            await HandleChangePasswordPopupIfPresent();

                            return true;
                        }
                    }

                    // Verificar estado después de cada intento
                    await CheckPageStatus($"verificación post-login intento {attempt + 1}");

                    if (attempt < maxAttempts - 1)
                    {
                        _logger.Warning($"Intento {attempt + 1} fallido, reintentando verificación...");
                        await dynamicWait.WaitForConditionAsync(
                            d => d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                                .All(e => !e.Displayed),
                            "wait_for_spinners",
                            TimeSpan.FromSeconds(1)
                        );
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("SERVIDOR_CAIDO:"))
                {
                    throw; // Propagar error de servidor caído
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error en intento {attempt + 1}: {ex.Message}");
                    if (attempt == maxAttempts - 1)
                    {
                        _logger.Error("Error en todos los intentos de verificación post-login");
                        return false;
                    }
                }
            }

            _logger.Error("No se pudo verificar el acceso exitoso a la página principal");
            return false;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("SERVIDOR_CAIDO:"))
        {
            _logger.Error($"Servidor caído detectado durante el login", ex);
            return false;
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

            await HandleChromePasswordWarningIfPresent();

            await HandleChangePasswordPopupIfPresent();

            // Crear un DynamicWaitHelper para manejar esperas dinámicas
            var dynamicWait = new DynamicWaitHelper(_driver);

            // Iniciar monitoreo del popup en segundo plano
            var popupMonitoringTask = Task.Run(async () =>
            {
                try
                {
                    await MonitorAndHandlePopup();
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error en monitoreo de popup: {ex.Message}");
                }
            });

            _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Iniciando navegación al tracking");

            // Navegar al tracking y obtener el resultado de la búsqueda en tabla
            var (tableLoaded, vehicleInfo) = await NavigateToTracking();

            if (!tableLoaded)
            {
                throw new InvalidOperationException("No se pudo cargar la tabla después de múltiples intentos");
            }

            _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Navegación al tracking completada");

            if (_isBetaFlow)
            {
                if (!vehicleInfo.HasValue)
                {
                    throw new InvalidOperationException($"CONFIGURACION_INVALIDA: El vehículo con placa {patent} no está disponible con las credenciales proporcionadas");
                }

                _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Vehículo encontrado, procediendo a hacer clic en botón");

                // Intentar el clic con diferentes estrategias
                bool clickSuccess = false;
                for (int clickAttempt = 0; clickAttempt < 3 && !clickSuccess; clickAttempt++)
                {
                    try
                    {
                        // Verificar que no hay overlay de carga
                        await WaitForNoOverlayAsync();

                        // Refrescar el elemento antes de cada intento
                        var buttons = _driver.FindElements(By.CssSelector("button.ui-button-danger[icon='pi pi-compass']"))
                                            .Where(b => b.Displayed && b.Enabled)
                                            .ToList();

                        var button = buttons.FirstOrDefault();
                        if (button == null)
                        {
                            _logger.Warning($"Intento {clickAttempt + 1}: Botón no encontrado, reintentando...");
                            await dynamicWait.WaitForConditionAsync(
                                d => d.FindElements(By.CssSelector("button.ui-button-danger[icon='pi pi-compass']"))
                                    .Any(b => b.Displayed && b.Enabled),
                                "compass_button",
                                TimeSpan.FromMilliseconds(500)
                            );
                            continue;
                        }

                        // Intentar el clic
                        await ClickWhenClickableAsync(By.CssSelector("button.ui-button-danger[icon='pi pi-compass']"), cachedElement: button);
                        clickSuccess = true;

                        _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Esperando carga del mapa");

                        // Esperar dinámicamente a que el mapa se cargue
                        await dynamicWait.WaitForConditionAsync(
                            condition: d =>
                            {
                                try
                                {
                                    return d.FindElements(By.TagName("svg")).Any();
                                }
                                catch
                                {
                                    return false;
                                }
                            },
                            conditionId: "mapa_cargado",
                            timeout: TimeSpan.FromSeconds(3)
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error en intento {clickAttempt + 1} de clic: {ex.Message}");
                        if (clickAttempt == 2) throw;
                        await dynamicWait.WaitForConditionAsync(
                            d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                                .Any(e => e.Displayed),
                            "wait_for_retry",
                            TimeSpan.FromMilliseconds(300)
                        );
                    }
                }

                if (!clickSuccess)
                {
                    throw new InvalidOperationException("No se pudo hacer clic en el botón después de múltiples intentos");
                }

                _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Mapa cargado");
            }

            // Verificar estado antes de acceder al frame
            await CheckPageStatus("pre-acceso al frame del mapa");

            _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Accediendo al frame del mapa");

            // Esperar y cambiar al frame correcto
            _logger.Debug("Intentando acceder al frame del mapa");
            int maxFrameAttempts = 3;
            IWebElement? mainFrame = null;

            for (int attempt = 0; attempt < maxFrameAttempts; attempt++)
            {
                try
                {
                    _driver.SwitchTo().DefaultContent();

                    var frames = _driver.FindElements(By.CssSelector("iframe[id^='ttab']"));
                    _logger.Debug($"Frames encontrados: {frames.Count}");

                    mainFrame = frames.FirstOrDefault(f => f.Displayed);

                    if (mainFrame != null)
                    {
                        try
                        {
                            _driver.SwitchTo().Frame(mainFrame);

                            // Verificar estado dentro del frame
                            await CheckPageStatus("dentro del frame del mapa");

                            // Verificar si el frame cargó correctamente buscando elementos SVG
                            if (_driver.FindElements(By.TagName("svg")).Any())
                            {
                                _logger.Info($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Frame del mapa encontrado y verificado", true);
                                break;
                            }
                        }
                        catch (InvalidOperationException ex) when (ex.Message.StartsWith("SERVIDOR_CAIDO:"))
                        {
                            throw;
                        }
                        catch
                        {
                            _logger.Warning($"Error al verificar contenido del frame en intento {attempt + 1}");
                            mainFrame = null;
                        }
                    }

                    if (attempt == maxFrameAttempts - 1)
                    {
                        throw new InvalidOperationException("No se encontró el frame principal del mapa después de múltiples intentos");
                    }

                    _logger.Warning($"Reintentando acceso al frame, intento {attempt + 1}");
                    await dynamicWait.WaitForConditionAsync(d =>
                        d.FindElements(By.CssSelector("iframe[id^='ttab']")).Any(f => f.Displayed),
                        "frame_wait"
                    );
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("SERVIDOR_CAIDO:"))
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error en intento {attempt + 1} de acceder al frame", ex);
                    if (attempt == maxFrameAttempts - 1) throw;
                }
            }

            // Verificar estado antes de buscar el icono
            await CheckPageStatus("pre-búsqueda de icono");

            _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Buscando icono del vehículo");

            // Buscar el icono del vehículo
            var vehicleIcon = await FindVehicleIcon();
            if (vehicleIcon == null)
            {
                throw new InvalidOperationException("No se pudo encontrar el icono del vehículo en el mapa");
            }

            _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Icono encontrado, haciendo clic");

            // Hacer clic en el icono del vehículo
            var angle = await ExtractVehicleAngle(vehicleIcon);

            // ①   clic usando el elemento ya localizado, con un pequeño timeout extra
            await ClickWhenClickableAsync(
                    By.XPath("//*[name()='svg']//*[name()='image'][@width='35'][@height='35']"),
                    cachedElement: vehicleIcon,                      // reutilizamos el WebElement
                    timeout: TimeSpan.FromSeconds(4)                 // 1 s más holgado
            );

            // ②   ahora SÍ esperamos a que aparezca el popup (flu-wait 250 ms)
            _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Esperando popup de información");

            IWebElement infoWindow;
            try
            {
                infoWindow = await WaitForVehiclePopupAsync(TimeSpan.FromSeconds(12));

                if (infoWindow == null)                 // si sigue sin verse, usamos el rescate
                {
                    _logger.Debug("Popup no visible tras espera primaria — iniciando rescate");
                    infoWindow = await FindPopupWithRetry();    // rutina de arrastre/zoom
                }
            }
            catch (WebDriverTimeoutException)       // si el popup quedó fuera de vista
            {
                _logger.Debug("Popup no visible tras la espera principal — procediendo con rescate");
                infoWindow = await FindPopupWithRetry();            // tu rescate existente
            }
            if (infoWindow == null)
            {
                throw new InvalidOperationException("No se pudo obtener la información del vehículo después de múltiples intentos");
            }

            _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Popup encontrado, extrayendo información");

            // Verificar estado antes de extraer la información
            await CheckPageStatus("pre-extracción de información");

            // Extraer y retornar la información del vehículo
            var result = await ExtractVehicleInformation(infoWindow, angle);

            _logger.Info($"[Tiempo TOTAL del proceso: {stopwatch.ElapsedMilliseconds}ms] Proceso completado exitosamente del vehículo {patent}", true);

            return result;
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

    private async Task<(bool TableLoaded, (IWebElement Button, string Location)? VehicleInfo)> NavigateToTracking(int maxAttempts = 3)
    {
        int attempt = 0;
        var dynamicWait = new DynamicWaitHelper(_driver);
        var globalStopwatch = new Stopwatch();
        globalStopwatch.Start();

        while (attempt < maxAttempts)
        {
            try
            {
                attempt++;
                _logger.Debug($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Intento {attempt} de navegar al tracking");

                await CheckPageStatus($"pre-navegación tracking intento {attempt}");

                await HandleChromePasswordWarningIfPresent();

                await HandleChangePasswordPopupIfPresent();

                var popupMonitoringTask = MonitorAndHandlePopup();

                // Hacer clic en MENU PRINCIPAL y verificar despliegue
                var menuExpanded = false;
                int menuAttempts = 0;
                const int maxMenuAttempts = 3;

                while (!menuExpanded && menuAttempts < maxMenuAttempts)
                {
                    try
                    {
                        menuAttempts++;
                        _logger.Debug($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Intento {menuAttempts} de expandir menú principal");

                        // Verificar si el menú Informes ya está visible
                        var informesVisible = await dynamicWait.WaitForConditionAsync(
                            condition: d =>
                            {
                                try
                                {
                                    return d.FindElements(By.XPath("//a[contains(text(), 'Informes')]"))
                                            .Any(e => e.Displayed);
                                }
                                catch
                                {
                                    return false;
                                }
                            },
                            conditionId: "informes_visible",
                            timeout: TimeSpan.FromMilliseconds(500)
                        );

                        if (informesVisible)
                        {
                            menuExpanded = true;
                            _logger.Debug($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Menú Informes ya visible, procediendo...");
                            break;
                        }

                        // Si no está visible, intentar hacer clic en MENU PRINCIPAL
                        var menuPrincipal = await WaitForElementWithRetry(By.CssSelector("td.myMenu"), timeout: 5);
                        if (menuPrincipal == null)
                        {
                            menuPrincipal = await WaitForElementWithRetry(
                                By.XPath("//td[contains(@class, 'myMenu') and not(ancestor::*[contains(@style,'display: none')])]"),
                                timeout: 5
                            );
                        }

                        if (menuPrincipal == null)
                        {
                            _logger.Warning($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] No se encontró el menú principal, reintentando...");
                            await dynamicWait.WaitForConditionAsync(
                                d => d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                                    .All(e => !e.Displayed),
                                "wait_for_retry",
                                TimeSpan.FromMilliseconds(300)
                            );
                            continue;
                        }

                        await ClickWhenClickableAsync(By.CssSelector("td.myMenu"), cachedElement: menuPrincipal);

                        // Verificación agresiva del despliegue del menú
                        for (int verificationAttempt = 0; verificationAttempt < 6; verificationAttempt++)
                        {
                            var menuExpansionCheck = await dynamicWait.WaitForConditionAsync(
                                 condition: d =>
                                 {
                                     try
                                     {
                                         return d.FindElements(By.XPath("//a[contains(text(), 'Informes')]"))
                                                 .Any(e => e.Displayed);
                                     }
                                     catch
                                     {
                                         return false;
                                     }
                                 },
                                 conditionId: "menu_expansion",
                                 timeout: TimeSpan.FromMilliseconds(200)
                             );

                            if (menuExpansionCheck)
                            {
                                menuExpanded = true;
                                break;
                            }

                            try
                            {
                                await ClickWhenClickableAsync(By.CssSelector("td.myMenu"), timeout: TimeSpan.FromMilliseconds(500));
                            }
                            catch { }

                            await dynamicWait.WaitForConditionAsync(
                                d => true, // Solo para esperar un breve momento
                                "menu_click_retry",
                                TimeSpan.FromMilliseconds(100)
                            );
                        }

                        if (!menuExpanded && menuAttempts == maxMenuAttempts)
                        {
                            throw new InvalidOperationException("No se pudo expandir el menú principal después de múltiples intentos");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Error en intento {menuAttempts} de expandir menú: {ex.Message}");
                        if (menuAttempts == maxMenuAttempts) throw;
                        await dynamicWait.WaitForConditionAsync(
                            d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                                .Any(e => e.Displayed),
                            "wait_for_retry",
                            TimeSpan.FromMilliseconds(200)
                        );
                    }
                }

                int informesAttempts = 0;
                const int maxInformesAttempts = 3;
                bool informesClicked = false;

                while (!informesClicked && informesAttempts < maxInformesAttempts)
                {
                    try
                    {
                        informesAttempts++;
                        _logger.Debug($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Intento {informesAttempts} de clic en Informes");

                        var informesButton = await FindInformesButton();
                        if (informesButton == null)
                        {
                            var menuPrincipal = await WaitForElementWithRetry(By.CssSelector("td.myMenu"), timeout: 2);
                            if (menuPrincipal != null)
                            {
                                await ClickWhenClickableAsync(By.CssSelector("td.myMenu"), timeout: TimeSpan.FromMilliseconds(500));
                                await dynamicWait.WaitForConditionAsync(
                                    d => true,
                                    "menu_click_retry",
                                    TimeSpan.FromMilliseconds(200)
                                );
                                continue;
                            }

                            throw new InvalidOperationException("No se pudo encontrar el botón de Informes");
                        }

                        await CheckPageStatus($"pre-clic informes intento {informesAttempts}");
                        var informesBtn = await FindInformesButton();
                        await ClickWhenClickableAsync(
                              By.XPath("//a[contains(text(),'Informes')]"),
                              cachedElement: informesBtn);

                        var informesLoaded = await dynamicWait.WaitForConditionAsync(
                            condition: d =>
                            {
                                try
                                {
                                    return d.FindElements(By.XPath("//a[contains(text(), 'Ultimo Punto')]"))
                                            .Any(e => e.Displayed);
                                }
                                catch
                                {
                                    return false;
                                }
                            },
                            conditionId: "informes_panel_load",
                            timeout: TimeSpan.FromMilliseconds(500)
                        );

                        if (informesLoaded)
                        {
                            informesClicked = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Error en intento {informesAttempts} de clic en Informes: {ex.Message}");
                        if (informesAttempts == maxInformesAttempts) throw;
                        await dynamicWait.WaitForConditionAsync(
                            d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                                .Any(e => e.Displayed),
                            "wait_for_retry",
                            TimeSpan.FromMilliseconds(200)
                        );
                    }
                }

                if (!informesClicked)
                {
                    throw new InvalidOperationException("No se pudo acceder a la sección de Informes después de múltiples intentos");
                }

                await CheckPageStatus($"pre-búsqueda último punto intento {attempt}");

                _logger.Debug($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Intentando encontrar 'Ultimo Punto (Beta)'...");
                var ultimoPuntoBetaButton = await FindUltimoPuntoButton();

                if (ultimoPuntoBetaButton != null)
                {
                    if (ultimoPuntoBetaButton.Text.Contains("Beta"))
                    {
                        _logger.Debug($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Opción 'Ultimo Punto (Beta)' encontrada, procediendo con flujo Beta");

                        await CheckPageStatus($"pre-clic último punto beta intento {attempt}");

                        // Guardar el número de ventanas actual y la ventana original
                        int currentWindows = _driver.WindowHandles.Count;
                        string originalWindow = _driver.CurrentWindowHandle;

                        // Hacer clic y esperar a que el iframe esté disponible
                        await ClickWhenClickableAsync(By.XPath("//a[contains(text(),'Ultimo Punto (Beta)')]"), cachedElement: ultimoPuntoBetaButton);

                        // Esperar a que se abra una nueva pestaña
                        await dynamicWait.WaitForConditionAsync(
                            d => d.WindowHandles.Count > currentWindows,
                            "new_window_opened",
                            TimeSpan.FromSeconds(3)
                        );

                        // Verificar si se abrió una nueva pestaña
                        var newWindows = _driver.WindowHandles;
                        if (newWindows.Count > currentWindows)
                        {
                            _driver.SwitchTo().Window(newWindows.Last());
                        }
                        else
                        {
                            // Si no se abrió nueva pestaña, verificar si seguimos en la ventana original
                            try
                            {
                                var currentUrl = _driver.Url;
                                if (currentUrl == originalWindow)
                                {
                                    // Reintentar el clic
                                    await ClickWhenClickableAsync(By.XPath("//a[contains(text(), 'Ultimo Punto (Beta)')]"), cachedElement: ultimoPuntoBetaButton);

                                    // Esperar a que se abra una nueva pestaña
                                    await dynamicWait.WaitForConditionAsync(
                                        d => d.WindowHandles.Count > currentWindows,
                                        "new_window_opened_retry",
                                        TimeSpan.FromSeconds(3)
                                    );

                                    newWindows = _driver.WindowHandles;
                                    if (newWindows.Count > currentWindows)
                                    {
                                        _driver.SwitchTo().Window(newWindows.Last());
                                    }
                                    else
                                    {
                                        throw new InvalidOperationException("No se pudo abrir nueva pestaña después del reintento");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Error verificando ventana", ex);
                                throw;
                            }
                        }

                        _isBetaFlow = true;

                        // Esperamos a que el iframe esté disponible y cambiar a él
                        var iframeAvailable = await dynamicWait.WaitForConditionAsync(
                            d => {
                                try
                                {
                                    var iframe = d.FindElement(By.CssSelector("iframe[id^='ttab']"));
                                    if (iframe != null && iframe.Displayed)
                                    {
                                        d.SwitchTo().Frame(iframe);
                                        return true;
                                    }
                                    return false;
                                }
                                catch { return false; }
                            },
                            "iframe_available",
                            TimeSpan.FromSeconds(10)
                        );

                        if (!iframeAvailable)
                        {
                            throw new InvalidOperationException("No se pudo acceder al iframe después del clic en Ultimo Punto (Beta)");
                        }

                        // Esperamos a que Angular inicialice
                        var angularReady = await dynamicWait.WaitForConditionAsync(
                            d => {
                                try
                                {
                                    return ((IJavaScriptExecutor)d).ExecuteScript(
                                        "return !!document.querySelector('app-root')"
                                    ) as bool? ?? false;
                                }
                                catch { return false; }
                            },
                            "angular_ready",
                            TimeSpan.FromSeconds(10)
                        );

                        if (!angularReady)
                        {
                            throw new InvalidOperationException("Angular no se inicializó correctamente");
                        }

                        // Ahora sí iniciamos la búsqueda del vehículo
                        var vehicleInfo = await FindVehicleInTableWithPolling(_currentPatent, dynamicWait);
                        return (true, vehicleInfo);
                    }
                    else
                    {
                        _logger.Debug($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Opción 'Ultimo Punto' encontrada, procediendo con flujo normal");
                        await ClickWhenClickableAsync(By.XPath("//a[contains(text(),'Ultimo Punto')]"), cachedElement: ultimoPuntoBetaButton );

                        // Esperar a que la acción se complete
                        await dynamicWait.WaitForConditionAsync(
                            d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                                .Any(e => e.Displayed),
                            "wait_after_click",
                            TimeSpan.FromMilliseconds(1000)
                        );

                        // Cambiar al iframe y buscar el vehículo en la tabla
                        var tableResult = await SwitchToTableIframeAndClickVehicle(dynamicWait);
                        if (!tableResult)
                        {
                            throw new InvalidOperationException("No se pudo interactuar con la tabla después de múltiples intentos");
                        }

                        return (true, null);
                    }
                }

                return (true, null);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("SERVIDOR_CAIDO:"))
            {
                _logger.Error($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Servidor caído detectado en intento {attempt}", ex);
                throw;
            }
            catch (WebDriverTimeoutException ex)
            {
                _logger.Warning($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Timeout en intento {attempt}: {ex.Message}");
                if (attempt == maxAttempts)
                    throw new InvalidOperationException("Timeout al intentar navegar al tracking", ex);

                await dynamicWait.WaitForConditionAsync(
                    d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                        .Any(e => e.Displayed),
                    "wait_after_timeout",
                    TimeSpan.FromMilliseconds(1000)
                );
            }
            catch (Exception ex)
            {
                _logger.Error($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Error en intento {attempt}", ex);
                if (attempt == maxAttempts)
                    throw new InvalidOperationException("Error durante la navegación", ex);

                await dynamicWait.WaitForConditionAsync(
                    d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                        .Any(e => e.Displayed),
                    "wait_after_error",
                    TimeSpan.FromMilliseconds(1000)
                );
            }
        }

        return (false, null);
    }

    private async Task<bool> SwitchToTableIframeAndClickVehicle(DynamicWaitHelper dynamicWait)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            _logger.Debug($"[Tiempo: {stopwatch.ElapsedMilliseconds}ms] Iniciando búsqueda de iframe y tabla");

            // Esperar y buscar el iframe de forma dinámica
            var iframeFound = await dynamicWait.WaitForConditionAsync(
                condition: d =>
                {
                    try
                    {
                        var frames = d.FindElements(By.CssSelector("iframe[id^='ttab']"));
                        var frame = frames.FirstOrDefault(f => f.Displayed);
                        if (frame != null)
                        {
                            d.SwitchTo().Frame(frame);
                            return true;
                        }
                        return false;
                    }
                    catch
                    {
                        return false;
                    }
                },
                conditionId: "table_iframe",
                timeout: TimeSpan.FromSeconds(10)
            );

            if (!iframeFound)
            {
                throw new InvalidOperationException("No se pudo encontrar o acceder al iframe");
            }

            _logger.Debug($"[Tiempo: {stopwatch.ElapsedMilliseconds}ms] Iframe encontrado, buscando tabla");

            // Esperar a que la tabla esté disponible y contenga datos
            var tableFound = await dynamicWait.WaitForConditionAsync(
                condition: d =>
                {
                    try
                    {
                        var tables = d.FindElements(By.TagName("table"));
                        var table = tables.FirstOrDefault(t =>
                            t.GetAttribute("class")?.Contains("rounded-corner") == true &&
                            t.Displayed &&
                            t.FindElements(By.TagName("tr")).Count > 1); // Asegurar que hay filas

                        return table != null;
                    }
                    catch
                    {
                        return false;
                    }
                },
                conditionId: "data_table",
                timeout: TimeSpan.FromSeconds(10)
            );

            if (!tableFound)
            {
                throw new InvalidOperationException("No se encontró la tabla con datos");
            }

            _logger.Debug($"[Tiempo: {stopwatch.ElapsedMilliseconds}ms] Tabla encontrada, buscando placa {_currentPatent}");

            // Buscar la celda con la placa de manera dinámica
            var maxSearchAttempts = 3;

            for (int searchAttempt = 0; searchAttempt < maxSearchAttempts; searchAttempt++)
            {
                try
                {
                    var plateFound = await dynamicWait.WaitForConditionAsync(
                        condition: d =>
                        {
                            try
                            {
                                var cells = d.FindElements(By.TagName("td"));
                                var targetCell = cells.FirstOrDefault(td =>
                                    td.Displayed &&
                                    td.Text.Trim().Equals(_currentPatent, StringComparison.OrdinalIgnoreCase));

                                if (targetCell == null)
                                    return false;

                                // Verificar que la celda es clickeable
                                var isClickable = (bool)((IJavaScriptExecutor)d).ExecuteScript(@"
                                var elem = arguments[0];
                                var rect = elem.getBoundingClientRect();
                                return (
                                    rect.width > 0 &&
                                    rect.height > 0 &&
                                    elem.offsetParent !== null &&
                                    !!(elem.offsetWidth || elem.offsetHeight || elem.getClientRects().length)
                                );
                            ", targetCell);

                                if (!isClickable)
                                    return false;

                                // Intentar hacer scroll a la celda
                                ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].scrollIntoView({block: 'center', inline: 'center'});", targetCell);

                                // Verificar que no hay overlay
                                bool hasOverlay = d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                                    .Any(e => e.Displayed);

                                if (hasOverlay)
                                    return false;

                                // Intentar el clic
                                targetCell.Click();
                                return true;
                            }
                            catch
                            {
                                return false;
                            }
                        },
                        conditionId: "find_plate",
                        timeout: TimeSpan.FromSeconds(5)
                    );

                    if (plateFound)
                    {
                        _logger.Info($"[Tiempo: {stopwatch.ElapsedMilliseconds}ms] Placa encontrada y clic realizado", true);

                        // Volver al contexto principal
                        _driver.SwitchTo().DefaultContent();
                        return true;
                    }

                    if (searchAttempt < maxSearchAttempts - 1)
                    {
                        _logger.Warning($"[Tiempo: {stopwatch.ElapsedMilliseconds}ms] Intento {searchAttempt + 1} fallido, reintentando");
                        await dynamicWait.WaitForConditionAsync(
                            d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                                .Any(e => e.Displayed),
                            "wait_between_search",
                            TimeSpan.FromMilliseconds(300 * (searchAttempt + 1))
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"[Tiempo: {stopwatch.ElapsedMilliseconds}ms] Error en intento {searchAttempt + 1}: {ex.Message}");

                    if (searchAttempt == maxSearchAttempts - 1)
                    {
                        throw new InvalidOperationException($"CONFIGURACION_INVALIDA: No se encontró la placa {_currentPatent} en la tabla después de {maxSearchAttempts} intentos");
                    }
                }
            }

            throw new InvalidOperationException($"No se pudo encontrar o hacer clic en la placa {_currentPatent}");
        }
        catch (Exception ex)
        {
            _logger.Error($"[Tiempo: {stopwatch.ElapsedMilliseconds}ms] Error al interactuar con la tabla en el iframe", ex);
            try
            {
                _driver.SwitchTo().DefaultContent();
            }
            catch { }
            throw;
        }
    }

    private async Task<(IWebElement Button, string Location)?> FindVehicleInTableWithPolling(string patent, DynamicWaitHelper dynamicWait, int maxAttempts = 3)
    {
        var startTime = DateTime.UtcNow;
        var maxWaitTime = TimeSpan.FromSeconds(10); // Aumentado a 10 segundos
        var globalStopwatch = new Stopwatch();
        var actionStopwatch = new Stopwatch();
        globalStopwatch.Start();

        // Variable para controlar si ya intentamos refrescar
        bool hasTriedRefresh = false;

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            try
            {
                // Verificar el estado actual de la tabla usando JavaScript
                var tableState = await Task.Run(() =>
                {
                    try
                    {
                        return ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                        return {
                            hasTable: !!document.querySelector('p-table'),
                            isLoading: !!document.querySelector('.ui-table-loading'),
                            hasRows: !!document.querySelector('tr.ng-star-inserted'),
                            totalRows: document.querySelectorAll('tr.ng-star-inserted').length
                        }
                    ") as Dictionary<string, object>;
                    }
                    catch { return null; }
                });

                if (tableState != null)
                {
                    bool hasTable = (bool)tableState["hasTable"];
                    bool isLoading = (bool)tableState["isLoading"];
                    bool hasRows = (bool)tableState["hasRows"];
                    long totalRows = Convert.ToInt64(tableState["totalRows"]);

                    _logger.Debug($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Estado tabla - " +
                                        $"Existe: {hasTable}, Cargando: {isLoading}, " +
                                        $"Tiene filas: {hasRows}, Total filas: {totalRows}");

                    // Si la tabla está presente y no está cargando, intentar encontrar el vehículo
                    if (hasTable && !isLoading)
                    {
                        var vehicleInfo = await TryFindAndClickVehicle(patent);
                        if (vehicleInfo.HasValue)
                        {
                            return vehicleInfo;
                        }
                        else if (hasRows && DateTime.UtcNow - startTime > TimeSpan.FromSeconds(5) && !hasTriedRefresh)
                        {
                            // Si después de 5 segundos tenemos filas pero no encontramos el vehículo,
                            // intentar un único refresco
                            _logger.Debug("Tabla tiene filas pero vehículo no encontrado, intentando refrescar una vez...");
                            await RefreshTableIfPossible();
                            hasTriedRefresh = true;

                            // Esperar después del refresco
                            await dynamicWait.WaitForConditionAsync(
                                d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait,.ui-table-loading"))
                                    .Any(e => e.Displayed),
                                "wait_after_refresh",
                                TimeSpan.FromSeconds(2)
                            );
                        }
                    }
                }

                // Si estamos cerca del timeout y no hemos encontrado nada, intentar refrescar
                if (!hasTriedRefresh && DateTime.UtcNow - startTime > TimeSpan.FromSeconds(8))
                {
                    _logger.Debug("Acercándose al timeout, intentando refrescar tabla...");
                    await RefreshTableIfPossible();
                    hasTriedRefresh = true;

                    // Esperar después del refresco
                    await dynamicWait.WaitForConditionAsync(
                        d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait,.ui-table-loading"))
                            .Any(e => e.Displayed),
                        "wait_after_timeout_refresh",
                        TimeSpan.FromSeconds(2)
                    );
                }

                await dynamicWait.WaitForConditionAsync(
                    d => true, // Solo para esperar un breve momento
                    "poll_interval",
                    TimeSpan.FromMilliseconds(100)
                );
            }
            catch (Exception ex)
            {
                _logger.Warning($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Error durante polling: {ex.Message}");
                await dynamicWait.WaitForConditionAsync(
                    d => true,
                    "error_interval",
                    TimeSpan.FromMilliseconds(100)
                );
            }
        }

        // Si el polling no tuvo éxito, hacer una última verificación de las placas disponibles
        try
        {
            var plates = await GetAvailablePlates();
            if (plates != null)
            {
                if (!plates.Any(p => p.Contains(patent, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"CONFIGURACION_INVALIDA: El vehículo con placa {patent} no está disponible con las credenciales proporcionadas. " +
                        $"Placas disponibles: {string.Join(", ", plates)}");
                }
            }

            _logger.Error($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Timeout buscando vehículo {patent}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Error en búsqueda final", ex);
            throw;
        }
    }

    private async Task<List<string>> GetAvailablePlates()
    {
        try
        {
            var platesList = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            return Array.from(document.querySelectorAll('tr.ng-star-inserted td:nth-child(3)'))
                .map(td => td.textContent.trim())
                .filter(text => text.length > 0);
        ") as IEnumerable<object>;

            return platesList?.Select(p => p?.ToString() ?? "").Where(p => !string.IsNullOrEmpty(p)).ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error obteniendo placas disponibles: {ex.Message}");
            return new List<string>();
        }
    }

    private async Task RefreshTableIfPossible()
    {
        try
        {
            var refreshButton = _driver.FindElement(By.CssSelector("button.ui-button-danger"));
            if (refreshButton != null && refreshButton.Displayed && refreshButton.Enabled)
            {
                await ClickWhenClickableAsync(By.CssSelector("button.ui-button-danger"), cachedElement: refreshButton);
                _logger.Debug("Tabla refrescada exitosamente");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"No se pudo refrescar la tabla: {ex.Message}");
        }
    }

    private async Task<(IWebElement Button, string Location)?> TryFindAndClickVehicle(string patent, bool isFinalAttempt = false)
    {
        try
        {
            var vehicleInfo = await Task.Run(() =>
            {
                var result = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                function findVehicle(targetPlate) {
                    const rows = document.querySelectorAll('tr.ng-star-inserted');
                    for (const row of rows) {
                        if (!row.offsetParent) continue;
                        
                        const cells = row.querySelectorAll('td.text-center.ng-star-inserted div.ng-star-inserted');
                        if (cells.length >= 4) {
                            const plateText = cells[2].textContent.trim().toLowerCase();
                            if (plateText.includes(targetPlate.toLowerCase())) {
                                const button = row.querySelector('button.ui-button-danger[icon=""pi pi-compass""]');
                                if (button && button.offsetParent !== null) {
                                    const location = cells[4].textContent.trim();
                                    return { buttonId: button.id || Math.random().toString(), location: location };
                                }
                            }
                        }
                    }
                    return null;
                }
                return findVehicle(arguments[0]);
            ", patent.ToLower());

                return result as Dictionary<string, object>;
            });

            if (vehicleInfo != null)
            {
                var buttonId = vehicleInfo["buttonId"].ToString();
                var location = vehicleInfo["location"].ToString();

                // Buscar el botón usando el ID que obtuvimos
                var button = _driver.FindElement(By.CssSelector($"button.ui-button-danger[icon='pi pi-compass']"));

                if (button != null && button.Displayed && button.Enabled)
                {
                    // Asegurarnos que el botón esté visible
                    ((IJavaScriptExecutor)_driver).ExecuteScript(
                        "arguments[0].scrollIntoView({block: 'center', behavior: 'instant'});",
                        button
                    );

                    // Verificar que no hay overlay de carga
                    var noLoading = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    const loading = document.querySelector('.ui-table-loading');
                    return !loading || !loading.offsetParent;
                ") as bool? ?? false;

                    if (noLoading)
                    {
                        return (button, location);
                    }
                }
            }

            // Si estamos en el último intento y no encontramos el vehículo, verificar las placas disponibles
            if (isFinalAttempt)
            {
                var plates = await Task.Run(() =>
                {
                    var platesList = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    return Array.from(document.querySelectorAll('tr.ng-star-inserted td:nth-child(3)'))
                        .map(td => td.textContent.trim())
                        .filter(text => text.length > 0);
                ") as IEnumerable<object>;

                    return platesList?.Select(p => p.ToString()).Where(p => !string.IsNullOrEmpty(p)).ToList();
                });

                if (plates?.Any() == true && !plates.Any(p => p.Contains(patent, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"CONFIGURACION_INVALIDA: El vehículo con placa {patent} no está disponible con las credenciales proporcionadas. " +
                        $"Placas disponibles: {string.Join(", ", plates)}");
                }
            }

            return null;
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            if (isFinalAttempt) throw;
            _logger.Warning($"Error en TryFindAndClickVehicle: {ex.Message}");
            return null;
        }
    }

    private async Task MonitorAndHandlePopup()
    {
        var startTime = DateTime.UtcNow;
        var maxWaitTime = TimeSpan.FromSeconds(10);
        var dynamicWait = new DynamicWaitHelper(_driver);

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            try
            {
                var popupPresent = await Task.Run(() =>
                {
                    try
                    {
                        return ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                        return !!(
                            document.querySelector('div.encuesta') ||
                            document.querySelector('[class*=""modal""][style*=""display: block""]') ||
                            document.querySelector('[class*=""popup""][style*=""display: block""]') ||
                            document.querySelector('.modal-backdrop') ||
                            document.querySelector('.overlay:not([style*=""display: none""])')||
                            document.querySelector('div:has(h1:contains(""Cambia la contraseña""))')
                        );
                    ");
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (popupPresent is bool && (bool)popupPresent)
                {
                    var closed = await CloseUnexpectedPopups();
                    if (closed)
                    {
                        _logger.Debug("Popup detectado y cerrado exitosamente");
                        // Esperar un momento para asegurar que cualquier animación termine
                        await dynamicWait.WaitForConditionAsync(
                            d => true,
                            "popup_close_delay",
                            TimeSpan.FromMilliseconds(100)
                        );

                        // Verificar que realmente se cerró
                        if (await VerifyPopupClosed())
                        {
                            break;
                        }
                    }
                }

                await dynamicWait.WaitForConditionAsync(
                    d => true,
                    "popup_check_interval",
                    TimeSpan.FromMilliseconds(50)
                );
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error en monitoreo de popup: {ex.Message}");
                await dynamicWait.WaitForConditionAsync(
                    d => true,
                    "error_recovery",
                    TimeSpan.FromMilliseconds(50)
                );
            }
        }
    }

    private async Task DragMap(IWebElement element, int startX, int startY, int offsetX, int offsetY)
    {
        try
        {
            _logger.Debug($"Iniciando arrastre del mapa - Punto inicial: ({startX}, {startY}), Offset: ({offsetX}, {offsetY})");

            // Asegurarnos de que los offsets no sean demasiado grandes
            int maxOffset = 150; // Usar un offset más pequeño para evitar salir de los límites
            offsetX = Math.Sign(offsetX) * Math.Min(Math.Abs(offsetX), maxOffset);
            offsetY = Math.Sign(offsetY) * Math.Min(Math.Abs(offsetY), maxOffset);

            _logger.Debug($"Offset ajustado para mantener dentro de límites: ({offsetX}, {offsetY})");

            // Intentar primero con Actions
            try
            {
                var actions = new Actions(_driver);
                actions.MoveToElement(element)
                       .ClickAndHold()
                       .MoveByOffset(offsetX, offsetY)
                       .Release()
                       .Perform();
            }
            catch (Exception)
            {
                _logger.Debug("Primer intento fallido, intentando con JavaScript");

                // Si falla, intentar con JavaScript
                ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                function simulateDragMap(startX, startY, offsetX, offsetY) {
                    const element = arguments[0];
                    
                    // Crear evento mousedown
                    const mouseDown = new MouseEvent('mousedown', {
                        bubbles: true,
                        cancelable: true,
                        view: window,
                        clientX: startX,
                        clientY: startY
                    });
                    element.dispatchEvent(mouseDown);
                    
                    // Crear evento mousemove
                    const mouseMove = new MouseEvent('mousemove', {
                        bubbles: true,
                        cancelable: true,
                        view: window,
                        clientX: startX + offsetX,
                        clientY: startY + offsetY
                    });
                    element.dispatchEvent(mouseMove);
                    
                    // Crear evento mouseup
                    const mouseUp = new MouseEvent('mouseup', {
                        bubbles: true,
                        cancelable: true,
                        view: window,
                        clientX: startX + offsetX,
                        clientY: startY + offsetY
                    });
                    element.dispatchEvent(mouseUp);
                }
                simulateDragMap(arguments[1], arguments[2], arguments[3], arguments[4]);
            ", element, startX, startY, offsetX, offsetY);
            }

            // Esperar a que el mapa se actualice
            var dynamicWait = new DynamicWaitHelper(_driver);
            await dynamicWait.WaitForConditionAsync(
                d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                    .Any(e => e.Displayed),
                "map_update_after_drag",
                TimeSpan.FromSeconds(1)
            );

            _logger.Debug("Arrastre del mapa completado");
        }
        catch (Exception ex)
        {
            _logger.Error("Error durante el arrastre del mapa", ex);
            throw;
        }
    }

    private async Task<IWebElement?> FindPopupWithRetry(
    int maxAttempts = 3,
    CancellationToken ct = default)
    {
        _logger.Debug($"Iniciando búsqueda del popup con retry — intentos: {maxAttempts}");

        /* 1. intento directo (normalmente suficiente) - SIN cambiar contexto */
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(6);

            while (sw.Elapsed < timeout)
            {
                ct.ThrowIfCancellationRequested();

                // Buscar popup DENTRO del iframe actual
                var popup = _driver.FindElements(By.CssSelector("div.olFramedCloudPopupContent"))
                                   .FirstOrDefault(el => el.Displayed && !string.IsNullOrWhiteSpace(el.Text));

                if (popup != null) return popup;

                await Task.Delay(250, ct);
            }
        }
        catch (Exception)
        {
            _logger.Debug("Popup no visible tras la espera principal — aplicando rutina de arrastre");
        }

        /* 2. tu lógica original de arrastre para traer el popup al viewport */
        var iconPosition = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
        var icon = document.evaluate(""//*[name()='svg']//*[name()='image'][@width='35'][@height='35']"",
                                     document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
        if (!icon) return null;
        var r = icon.getBoundingClientRect();
        return { top:r.top,bottom:r.bottom,left:r.left,right:r.right,
                 viewportWidth:window.innerWidth, viewportHeight:window.innerHeight };")
                         as Dictionary<string, object>;

        if (iconPosition == null)
        {
            _logger.Warning("No se pudo obtener la posición del ícono", true);
            return null;
        }

        double iconTop = Convert.ToDouble(iconPosition["top"]);
        double iconBottom = Convert.ToDouble(iconPosition["bottom"]);
        double iconLeft = Convert.ToDouble(iconPosition["left"]);
        double iconRight = Convert.ToDouble(iconPosition["right"]);
        double vw = Convert.ToDouble(iconPosition["viewportWidth"]);
        double vh = Convert.ToDouble(iconPosition["viewportHeight"]);

        var mapElement = _driver.FindElement(By.CssSelector("div.olMap"));
        int cx = (int)vw / 2;
        int cy = (int)vh / 2;

        for (int i = 0; i < maxAttempts; i++)
        {
            _logger.Debug($"Intento {i + 1} de encontrar el popup mediante arrastre");

            /* comprobar rápido si ya está visible - DENTRO del iframe */
            var popupNow = _driver.FindElements(By.CssSelector("div.olFramedCloudPopupContent"))
                                  .FirstOrDefault(el => el.Displayed && !string.IsNullOrWhiteSpace(el.Text));
            if (popupNow != null) return popupNow;

            /* distancia incremental */
            int drag = 100 * (i + 1);

            if (iconTop < 100)
                await DragMap(mapElement, cx, cy, 0, drag);     // mover icono ↓   mapa ↑
            else if (iconBottom > vh - 100)
                await DragMap(mapElement, cx, cy, 0, -drag);     // mover icono ↑   mapa ↓
            else if (iconLeft < 100)
                await DragMap(mapElement, cx, cy, drag, 0);     // mover icono →   mapa ←
            else if (iconRight > vw - 100)
                await DragMap(mapElement, cx, cy, -drag, 0);     // mover icono ←   mapa →

            /* esperar a que termine la animación - MANTENER optimización */
            var dw = new DynamicWaitHelper(_driver);
            await dw.WaitForConditionAsync(
                d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                      .Any(e => e.Displayed),
                "map_update_after_drag",
                TimeSpan.FromSeconds(1));

            await Task.Delay(350, ct);  // Mantener el delay optimizado
        }

        _logger.Error("No se pudo encontrar el popup después de todos los intentos");
        return null;
    }

    private async Task<LocationDataInfo> ExtractVehicleInformation(IWebElement infoWindow, decimal angle)
    {
        var infoText = infoWindow.Text;
        if (string.IsNullOrWhiteSpace(infoText))
        {
            throw new InvalidOperationException("El popup no contiene información");
        }

        try
        {
            // Extraer fecha GPS
            var fechaGpsMatch = Regex.Match(infoText, @"Fecha Gps\s*:\s*(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})", RegexOptions.IgnoreCase);
            if (!fechaGpsMatch.Success)
            {
                _logger.Warning("No se pudo extraer la fecha GPS del texto: " + infoText);
            }

            DateTime timestamp;
            if (fechaGpsMatch.Success)
            {
                // Intentar parsear la fecha GPS
                if (DateTime.TryParse(fechaGpsMatch.Groups[1].Value.Trim(), out DateTime parsedDate))
                {
                    timestamp = parsedDate;
                    _logger.Debug($"Fecha GPS extraída correctamente: {timestamp}");
                }
                else
                {
                    _logger.Warning($"No se pudo parsear la fecha GPS: {fechaGpsMatch.Groups[1].Value}");
                    timestamp = DateTime.UtcNow;
                }
            }
            else
            {
                timestamp = DateTime.UtcNow;
            }

            // Extraer el resto de la información
            var latMatch = Regex.Match(infoText, @"Latitud\s*:\s*([-+]?\d*\.?\d+)", RegexOptions.IgnoreCase);
            var lonMatch = Regex.Match(infoText, @"Longitud\s*:\s*([-+]?\d*\.?\d+)", RegexOptions.IgnoreCase);
            var speedMatch = Regex.Match(infoText, @"Velocidad\s*:\s*(\d*\.?\d*)", RegexOptions.IgnoreCase);
            var reasonMatch = Regex.Match(infoText, @"Motivo\s*:\s*([^\r\n]*)", RegexOptions.IgnoreCase);
            var driverMatch = Regex.Match(infoText, @"Conductor\s*:\s*([^\r\n]*)", RegexOptions.IgnoreCase);
            var geoRefMatch = Regex.Match(infoText, @"Georeferencia\s*:\s*([^\r\n]*)", RegexOptions.IgnoreCase);
            var zoneMatch = Regex.Match(infoText, @"En Zona\s*:\s*([^\r\n]*)", RegexOptions.IgnoreCase);
            var stopTimeMatch = Regex.Match(infoText, @"Tiempo Detencion\s*:\s*([^\r\n]*)", RegexOptions.IgnoreCase);
            var distanceMatch = Regex.Match(infoText, @"Distancia Recorrida \(Km\)\s*:\s*(\d*\.?\d*)", RegexOptions.IgnoreCase);
            var tempMatch = Regex.Match(infoText, @"Temperatura\s*:\s*(\d*\.?\d*)", RegexOptions.IgnoreCase);

            if (!latMatch.Success || !lonMatch.Success)
            {
                throw new InvalidOperationException($"No se pudieron extraer las coordenadas. Texto del popup: {infoText}");
            }

            return new LocationDataInfo
            {
                Latitude = decimal.Parse(latMatch.Groups[1].Value.Trim(), CultureInfo.InvariantCulture),
                Longitude = decimal.Parse(lonMatch.Groups[1].Value.Trim(), CultureInfo.InvariantCulture),
                Speed = ParseDecimalOrDefault(speedMatch.Groups[1].Value),
                Timestamp = timestamp,
                Reason = reasonMatch.Success ? reasonMatch.Groups[1].Value.Trim() : string.Empty,
                Driver = driverMatch.Success ? driverMatch.Groups[1].Value.Trim() : string.Empty,
                Georeference = geoRefMatch.Success ? geoRefMatch.Groups[1].Value.Trim() : string.Empty,
                InZone = zoneMatch.Success ? zoneMatch.Groups[1].Value.Trim() : string.Empty,
                DetentionTime = stopTimeMatch.Success ? stopTimeMatch.Groups[1].Value.Trim() : "0",
                DistanceTraveled = ParseDecimalOrDefault(distanceMatch.Groups[1].Value),
                Temperature = ParseDecimalOrDefault(tempMatch.Groups[1].Value),
                Angle = angle
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error procesando información del vehículo: {ex.Message}\nTexto del popup: {infoText}");
        }
    }

    private decimal ParseDecimalOrDefault(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;

        try
        {
            return decimal.Parse(value.Trim(), CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0m;
        }
    }

    private async Task<IWebElement?> FindSkytrackButtonWithRetry(DynamicWaitHelper dynamicWait)
    {
        // Primero, asegurarse que cualquier loading o overlay haya desaparecido
        await dynamicWait.WaitForConditionAsync(driver =>
        {
            try
            {
                var loadingElements = driver.FindElements(By.CssSelector(".loading, .wait, .x-mask"));
                return !loadingElements.Any(e => e.Displayed);
            }
            catch
            {
                return false;
            }
        }, "loading_complete");

        // Lista actualizada de selectores a intentar
        var selectors = new[]
        {
            (By.Id("idBtnProductSkytrack")),
            (By.Id("idBtnProductSkytrack-btnInnerEl")),
            (By.XPath("//a[contains(@id,'idBtnProductSkytrack')]")),
            (By.XPath("//a[contains(@class,'hexa_btk_si')]")),
            (By.XPath("//span[contains(text(),'ACCEDER')]")),
            (By.XPath("//a[contains(@class,'x-btn')]//span[contains(text(),'ACCEDER')]")),
            (By.XPath("//a[contains(@class,'x-btn')]//span[contains(text(),'Skytrack')]")),
            // Selector más general como última opción
            (By.XPath("//*[contains(text(),'ACCEDER') or contains(text(),'Skytrack')]"))
        };

        for (int attempt = 0; attempt < 3; attempt++)
        {
            // Esperar a que la página esté completamente cargada
            await dynamicWait.WaitForPageLoadAsync();
            await dynamicWait.WaitForAjaxCompletionAsync();

            foreach (var selector in selectors)
            {
                try
                {
                    // Intentar encontrar el elemento con cada selector
                    var (element, _) = await dynamicWait.WaitForElementAsync(selector, ensureClickable: true);
                    if (element != null && element.Displayed && element.Enabled)
                    {
                        // Verificar si el elemento es realmente visible y clickeable
                        var isVisible = (bool)((IJavaScriptExecutor)_driver).ExecuteScript(@"
                        var elem = arguments[0];
                        var rect = elem.getBoundingClientRect();
                        return rect.width > 0 && rect.height > 0;
                    ", element);

                        if (isVisible)
                        {
                            return element;
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            if (attempt < 2)
            {
                // En lugar de un delay fijo, esperamos a que la página esté estable
                await dynamicWait.WaitForConditionAsync(driver =>
                {
                    try
                    {
                        return (bool)((IJavaScriptExecutor)driver).ExecuteScript(
                            "return document.readyState === 'complete' && !document.querySelector('.loading, .wait, .x-mask')"
                        );
                    }
                    catch
                    {
                        return false;
                    }
                }, "page_stable");

                // Intentar refrescar el DOM
                ((IJavaScriptExecutor)_driver).ExecuteScript("document.body.style.zoom='100%';");
            }
        }

        return null;
    }    

    private async Task<IWebElement?> FindVehicleIcon()
    {
        try
        {
            _logger.Debug("Iniciando búsqueda del ícono del vehículo...");
            var dynamicWait = new DynamicWaitHelper(_driver);

            // Esperar a que el mapa esté cargado
            await dynamicWait.WaitForConditionAsync(
                d => d.FindElements(By.TagName("svg")).Any(),
                "svg_map_loaded",
                TimeSpan.FromSeconds(3)
            );

            // Primer intento de búsqueda normal
            _logger.Debug("Intentando búsqueda normal del ícono...");
            var vehicleIcon = await TryFindVehicleIcon();

            // Si encontramos el ícono, retornarlo directamente
            if (vehicleIcon != null)
            {
                _logger.Debug("Ícono encontrado en primer intento, procediendo normalmente");
                return vehicleIcon;
            }

            _logger.Debug("Ícono no visible, iniciando procedimiento de rescate...");

            // Si no encontramos el ícono, intentar el procedimiento de rescate
            try
            {
                // 1. Buscar y hacer clic en el botón "Ultimo Punto" del popup
                _logger.Debug("Paso 1: Buscando botón 'Ultimo Punto' en el popup...");
                IWebElement? ultimoPuntoButton = null;

                // 2. Esperar a que el botón aparezca y esté visible
                await dynamicWait.WaitForConditionAsync(
                    d =>
                    {
                        try
                        {
                            var btn = d.FindElement(
                                By.XPath("//a[contains(@class,'button')][.//span[text()='Ultimo Punto']]"));

                            if (btn.Displayed)           // condición que debe cumplirse
                            {
                                ultimoPuntoButton = btn; // lo guardamos para usar luego
                                return true;             // señalamos que la espera terminó
                            }

                            return false;                // todavía no cumple
                        }
                        catch (NoSuchElementException)   // el elemento aún no existe
                        {
                            return false;
                        }
                    },
                    "ultimo_punto_button",
                    TimeSpan.FromSeconds(2));            // timeout opcional

                if (ultimoPuntoButton != null)
                {
                    _logger.Debug("Intentando hacer clic en botón 'Ultimo Punto'...");
                    await ClickWhenClickableAsync(By.XPath("//a[contains(@class, 'button')][.//span[text()='Ultimo Punto']]"), cachedElement: ultimoPuntoButton);

                    // Esperar a que la acción se complete
                    await dynamicWait.WaitForConditionAsync(
                        d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                            .Any(e => e.Displayed),
                        "after_ultimo_punto_click",
                        TimeSpan.FromSeconds(2)
                    );

                    _logger.Debug("Clic en 'Ultimo Punto' realizado");
                }
                else
                {
                    _logger.Error("No se pudo encontrar el botón 'Ultimo Punto'");
                    return null;
                }

                // 2. Minimizar el popup usando el toggle
                _logger.Debug("Paso 2: Buscando botón toggle para minimizar popup...");
                IWebElement? toggleButton = null;

                await dynamicWait.WaitForConditionAsync(
                    d =>
                    {
                        try
                        {
                            var btn = d.FindElement(By.Id("ext-gen17"));
                            if (btn.Displayed)           // condición que debe cumplirse
                            {
                                toggleButton = btn;      // lo guardamos para usar luego
                                return true;             // señalamos que la espera terminó
                            }
                            return false;                // todavía no cumple
                        }
                        catch (NoSuchElementException)   // el elemento aún no existe
                        {
                            return false;
                        }
                    },
                    "toggle_button",
                    TimeSpan.FromSeconds(2));            // timeout opcional
                

                if (toggleButton != null)
                {
                    _logger.Debug("Intentando minimizar popup con botón toggle...");
                    await ClickWhenClickableAsync(By.Id("ext-gen17"), cachedElement: toggleButton);

                    // Esperar a que la acción se complete
                    await dynamicWait.WaitForConditionAsync(
                        d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                            .Any(e => e.Displayed),
                        "after_toggle_click",
                        TimeSpan.FromMilliseconds(1000)
                    );

                    _logger.Debug("Popup minimizado correctamente");
                }
                else
                {
                    _logger.Error("No se pudo encontrar el botón toggle");
                    return null;
                }

                // 3. Hacer zoom out en el mapa
                _logger.Debug("Paso 3: Intentando hacer zoom out en el mapa...");
                try
                {
                    ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    try {
                        var mapElement = document.getElementById('map');
                        if (mapElement && mapElement.map) {
                            var map = mapElement.map;
                            var currentZoom = map.getZoom();
                            map.zoomTo(currentZoom - 2);
                            return true;
                        }
                        return false;
                    } catch(e) {
                        console.error('Error durante zoom:', e);
                        return false;
                    }
                ");
                    // Esperar a que el mapa se actualice
                    await dynamicWait.WaitForConditionAsync(
                        d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                            .Any(e => e.Displayed),
                        "after_zoom_out",
                        TimeSpan.FromSeconds(2)
                    );

                    _logger.Debug("Zoom out realizado correctamente");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error al intentar hacer zoom out: {ex.Message}");
                }

                // 4. Intentar encontrar el ícono nuevamente
                _logger.Debug("Paso 4: Reintentando búsqueda del ícono después del procedimiento de rescate...");
                var finalIcon = await TryFindVehicleIcon();
                if (finalIcon != null)
                {
                    _logger.Debug("Ícono encontrado después del procedimiento de rescate");
                    return finalIcon;
                }
                else
                {
                    _logger.Error("No se pudo encontrar el ícono después del procedimiento de rescate");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error en procedimiento de rescate: {ex.Message}");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error general buscando el icono del vehículo", ex);
            return null;
        }
    }

    private async Task<IWebElement?> TryFindVehicleIcon()
    {
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(5));

        // 1. Primer intento de búsqueda normal
        var vehicleIcon = await TryFindIconWithCurrentView();
        if (vehicleIcon != null)
        {
            _logger.Debug("Ícono encontrado en primer intento con la vista actual");
            return vehicleIcon;
        }

        // 2. Si no se encuentra, intentar rescate automático
        _logger.Debug("Ícono no visible, iniciando procedimiento de rescate automático");

        try
        {
            // 2.1 Buscar y clic en botón "Ultimo Punto" en el popup
            var ultimoPuntoButton = _driver.FindElement(
                By.XPath("//a[contains(@class, 'button')]//span[text()='Ultimo Punto']/parent::a"));

            if (ultimoPuntoButton != null)
            {
                _logger.Debug("Intentando clic en botón 'Ultimo Punto'");
                await ClickWhenClickableAsync(By.XPath("//a[contains(@class, 'button')]//span[text()='Ultimo Punto']/parent::a"), cachedElement: ultimoPuntoButton);

                // Esperar a que la acción se complete
                var dynamicWait = new DynamicWaitHelper(_driver);
                await dynamicWait.WaitForConditionAsync(
                    d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                        .Any(e => e.Displayed),
                    "after_ultimo_punto_click",
                    TimeSpan.FromMilliseconds(1000)
                );
            }

            // 2.2 Minimizar el popup
            var toggleButton = _driver.FindElement(By.Id("ext-gen17"));
            if (toggleButton != null)
            {
                _logger.Debug("Minimizando popup");
                await ClickWhenClickableAsync(By.Id("ext-gen17"));

                // Esperar a que la acción se complete
                var dynamicWait = new DynamicWaitHelper(_driver);
                await dynamicWait.WaitForConditionAsync(
                    d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                        .Any(e => e.Displayed),
                    "after_toggle_click",
                    TimeSpan.FromMilliseconds(1000)
                );
            }

            // 2.3 Hacer zoom out mediante JavaScript
            _logger.Debug("Ejecutando zoom out del mapa");
            ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            try {
                // Encontrar el mapa OpenLayers
                var map = null;
                for (var key in window) {
                    if (window[key] && window[key].CLASS_NAME === 'OpenLayers.Map') {
                        map = window[key];
                        break;
                    }
                }
                
                if (map) {
                    // Obtener zoom actual y hacer zoom out
var currentZoom = map.getZoom();
                    map.zoomTo(currentZoom - 2);
                    
                    // Centrar el mapa
                    var center = new OpenLayers.LonLat(-75.0, 4.5);
                    center.transform(
                        new OpenLayers.Projection('EPSG:4326'), 
                        map.getProjectionObject()
                    );
                    map.setCenter(center);
                }
            } catch(e) {
                console.error('Error en zoom out:', e);
            }
        ");

            // Esperar a que el mapa se actualice
            var mapUpdateWait = new DynamicWaitHelper(_driver);
            await mapUpdateWait.WaitForConditionAsync(
                d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                    .Any(e => e.Displayed),
                "after_zoom_out",
                TimeSpan.FromSeconds(2)
            );

            // 2.4 Reintentar búsqueda del ícono con la nueva vista
            return await TryFindIconWithCurrentView();
        }
        catch (Exception ex)
        {
            _logger.Error($"Error en procedimiento de rescate: {ex.Message}");
            return null;
        }
    }

    private async Task<IWebElement?> TryFindIconWithCurrentView()
    {
        try
        {
            var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(5));
            return wait.Until(d =>
            {
                // Primera búsqueda usando el selector original
                var elements = d.FindElements(By.XPath("//*[name()='svg']//*[name()='image']"));
                _logger.Debug($"Usando selector inicial - Elementos encontrados: {elements.Count}");

                return elements.FirstOrDefault(e =>
                {
                    try
                    {
                        string? width = e.GetAttribute("width");
                        string? height = e.GetAttribute("height");
                        string? href = e.GetAttribute("href") ?? e.GetAttribute("xlink:href");

                        // Validar dimensiones más flexibles para soportar diferentes tamaños de íconos
                        bool validDimensions = (width == "35" && height == "35") || // Para carros
                                             (width == "16" && height == "14") ||   // Para camiones pequeños
                                             (width == "20" && height == "20");     // Para camiones grandes

                        // Validar href para todos los tipos de vehículos
                        bool validHref = false;
                        if (!string.IsNullOrEmpty(href))
                        {
                            href = href.ToLower();
                            validHref = href.Contains("carro") ||
                                      href.Contains("camion");
                        }

                        _logger.Debug($"Analizando ícono - Width: {width}, Height: {height}, Href: {href}");

                        var isValid = e.Displayed && validDimensions && validHref;
                        if (isValid)
                        {
                            _logger.Debug($"Ícono válido encontrado - Width: {width}, Height: {height}, Href: {href}");
                        }

                        return isValid;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error analizando elemento: {ex.Message}");
                        return false;
                    }
                });
            });
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error en búsqueda de ícono: {ex.Message}");
            return null;
        }
    }

    private async Task<IWebElement> WaitForElementWithRetry(By selector, int timeout = 10)
    {
        try
        {
            var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeout));
            return wait.Until(driver =>
            {
                try
                {
                    var element = driver.FindElement(selector);
                    return element.Displayed ? element : null;
                }
                catch
                {
                    return null;
                }
            });
        }
        catch
        {
            return null;
        }
    }

    private async Task<IWebElement> FindInformesButton()
    {
        try
        {
            return await Task.Run(() => _wait.Until(d =>
            {
                try
                {
                    var elements = _driver.FindElements(By.XPath("//a[contains(text(), 'Informes')]"));
                    var element = elements.FirstOrDefault(e => e.Displayed && e.Text.Trim() == "Informes");
                    return element != null && element.Displayed ? element : null;
                }
                catch
                {
                    return null;
                }
            }));
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error buscando botón Informes: {ex.Message}");
            return null;
        }
    }

    private async Task<IWebElement?> FindUltimoPuntoButton()
    {
        try
        {
            // Reducimos el tiempo de espera para este caso específico
            var shortWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(5));

            // Primero intentamos encontrar el botón Beta
            var betaButton = await Task.Run(() =>
            {
                try
                {
                    var elements = _driver.FindElements(By.XPath("//a[contains(text(), 'Ultimo Punto (Beta)')]"));
                    var element = elements.FirstOrDefault(e => e.Displayed && e.Text.Contains("Ultimo Punto (Beta)"));
                    return element != null && element.Displayed ? element : null;
                }
                catch
                {
                    return null;
                }
            });

            if (betaButton != null)
            {
                _logger.Debug("Botón 'Ultimo Punto (Beta)' encontrado");
                return betaButton;
            }

            // Si no encontramos el botón Beta, buscamos el botón normal
            _logger.Debug("Botón Beta no encontrado, buscando 'Ultimo Punto' normal...");

            return await Task.Run(() =>
            {
                try
                {
                    // Intentar primero con XPath más específico
                    var elements = _driver.FindElements(By.XPath("//a[text()='Ultimo Punto']"));
                    var element = elements.FirstOrDefault(e => e.Displayed);

                    if (element != null && element.Displayed)
                    {
                        _logger.Debug("Botón 'Ultimo Punto' encontrado con XPath exacto");
                        return element;
                    }

                    // Si no lo encuentra, intentar con una búsqueda más flexible
                    elements = _driver.FindElements(By.XPath("//a[contains(text(), 'Ultimo Punto') and not(contains(text(), 'Beta'))]"));
                    element = elements.FirstOrDefault(e => e.Displayed && e.Text.Trim() == "Ultimo Punto");

                    if (element != null && element.Displayed)
                    {
                        _logger.Debug("Botón 'Ultimo Punto' encontrado con búsqueda flexible");
                        return element;
                    }

                    _logger.Warning("No se encontró ningún botón de Ultimo Punto", true);
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error buscando botón 'Ultimo Punto': {ex.Message}");
                    return null;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Error en FindUltimoPuntoButton: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> CloseUnexpectedPopups(int maxAttempts = 3)
    {
        try
        {
            _driver.SwitchTo().DefaultContent();

            // Intentar cerrar cualquier popup usando JavaScript primero
            var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            function closeAllPopups() {
                // 1. Cerrar popup de encuesta
                var encuestaModal = document.querySelector('div.encuesta');
                if (encuestaModal) {
                    encuestaModal.style.display = 'none';
                    encuestaModal.parentElement.removeChild(encuestaModal);
                }

                // 2. Remover cualquier backdrop
                var backdrops = document.querySelectorAll('.modal-backdrop');
                backdrops.forEach(b => b.parentElement.removeChild(b));

                // 3. Limpiar clases y estilos del body
                document.body.classList.remove('modal-open');
                document.body.style.overflow = 'auto';
                document.body.style.paddingRight = '';

                // 4. Buscar y cerrar otros posibles modales
                var modals = document.querySelectorAll('[class*=""modal""], [class*=""popup""], [class*=""dialog""]');
                modals.forEach(modal => {
                    if (modal.style.display !== 'none') {
                        modal.style.display = 'none';
                        if (modal.parentElement) {
                            modal.parentElement.removeChild(modal);
                        }
                    }
                });

                // 5. Remover cualquier overlay
                var overlays = document.querySelectorAll('.overlay, .modal-overlay, [class*=""overlay""]');
                overlays.forEach(overlay => overlay.parentElement.removeChild(overlay));

                return true;
            }
            return closeAllPopups();
        ");

            if (jsResult != null && (bool)jsResult)
            {
                _logger.Debug("Popup cerrado exitosamente mediante JavaScript");
                return true;
            }

            // Si el JavaScript no funcionó, intentar con botones específicos
            var closingSelectors = new[]
            {
                "//button[@id='btnClearEsat']",                          // Botón específico de encuesta
                "//button[contains(@class, 'close')]",                   // Botones genéricos de cierre
                "//div[contains(@class, 'modal')]//button",              // Botones en modales
                "//button[contains(@class, 'btn-close')]",              // Bootstrap close buttons
                "//*[contains(@class, 'modal')]//button[contains(@class, 'close')]" // Cualquier botón de cierre en modal
            };

            foreach (var selector in closingSelectors)
            {
                try
                {
                    var closeButtons = _driver.FindElements(By.XPath(selector));
                    foreach (var button in closeButtons.Where(b => b.Displayed))
                    {
                        try
                        {
                            await ClickWhenClickableAsync(By.XPath(selector), cachedElement: button);
                            // Verificar si el popup realmente se cerró
                            var popupGone = await VerifyPopupClosed();
                            if (popupGone)
                            {
                                _logger.Debug($"Popup cerrado exitosamente usando selector: {selector}");
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning($"Error al intentar clic en botón {selector}: {ex.Message}");
                        }
                    }
                }
                catch (Exception) { continue; }
            }

            // Verificación final
            var finalCheck = await VerifyPopupClosed();
            if (finalCheck)
            {
                _logger.Debug("Popup cerrado verificado en chequeo final");
                return true;
            }

            _logger.Warning("No se pudo cerrar el popup completamente");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error cerrando popup: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> VerifyPopupClosed()
    {
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Verificar múltiples indicadores de popups
                    var checks = new[]
                    {
                        "return !document.querySelector('div.encuesta')",
                        "return !document.querySelector('.modal-backdrop')",
                        "return !document.querySelector('[class*=\"modal\"][style*=\"display: block\"]')",
                        "return !document.querySelector('[class*=\"popup\"][style*=\"display: block\"]')",
                        "return !document.querySelector('.overlay:not([style*=\"display: none\"])')",
                        "return document.body.style.overflow !== 'hidden'",
                    };

                    foreach (var check in checks)
                    {
                        var result = ((IJavaScriptExecutor)_driver).ExecuteScript(check);
                        if (result is bool checkResult && !checkResult)
                        {
                            return false;
                        }
                    }

                    // Verificar elementos visibles
                    var visibleModals = _driver.FindElements(By.CssSelector(".encuesta, [class*='modal'], [class*='popup']"))
                        .Any(e => e.Displayed);

                    return !visibleModals;
                }
                catch
                {
                    return false;
                }
            });
        }
        catch
        {
            return false;
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

    private async Task<decimal> ExtractVehicleAngle(IWebElement vehicleIcon)
    {
        try
        {
            // Primero intentar obtener el ángulo de la transformación CSS
            var transform = vehicleIcon.GetAttribute("transform");
            if (!string.IsNullOrEmpty(transform))
            {
                var match = Regex.Match(transform, @"rotate\(([-\d.]+)");
                if (match.Success)
                {
                    return decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                }
            }

            // Si no hay transformación, intentar obtener el ángulo del href de la imagen
            var href = vehicleIcon.GetAttribute("href") ?? vehicleIcon.GetAttribute("xlink:href");
            if (!string.IsNullOrEmpty(href))
            {
                var directions = new Dictionary<string, decimal>
                {
                    {"_n", 0}, {"_ne", 45}, {"_e", 90}, {"_se", 135},
                    {"_s", 180}, {"_sw", 225}, {"_w", 270}, {"_nw", 315}
                };

                foreach (var dir in directions)
                {
                    if (href.Contains(dir.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        return dir.Value;
                    }
                }
            }

            // Si no se puede determinar el ángulo, retornar 0
            return 0;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error extrayendo el ángulo del vehículo", ex);
            return 0;
        }
    }

    private async Task HandleChangePasswordPopupIfPresent()
    {
        try
        {
            _logger.Debug("Verificando si existe un popup de cambio de contraseña...");

            // Buscar el popup por su contenido o título
            var passwordPopup = _driver.FindElements(By.XPath(
                "//div[contains(text(), 'Cambia la contraseña') or .//h1[contains(text(), 'Cambia la contraseña')]]"
            )).FirstOrDefault(e => e.Displayed);

            if (passwordPopup != null)
            {
                _logger.Info("Popup de cambio de contraseña detectado, intentando cerrar", true);

                // Buscar el botón Aceptar dentro del popup
                var acceptButton = _driver.FindElements(By.XPath(
                    "//button[text()='Aceptar' or contains(@class, 'accept') or contains(@class, 'primary')]"
                )).FirstOrDefault(b => b.Displayed && b.Enabled);

                if (acceptButton != null)
                {
                    await ClickWhenClickableAsync(By.XPath("//button[text()='Aceptar' or contains(@class, 'accept') or contains(@class, 'primary')]"), cachedElement: acceptButton);
                    _logger.Info("Popup de cambio de contraseña cerrado exitosamente", true);

                    // Esperar a que el popup se cierre
                    var dynamicWait = new DynamicWaitHelper(_driver);
                    await dynamicWait.WaitForConditionAsync(
                        d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                            .Any(e => e.Displayed),
                        "after_password_popup_close",
                        TimeSpan.FromMilliseconds(500)
                    );
                }
                else
                {
                    // Intentar encontrar el botón con JavaScript si los selectores anteriores fallan
                    var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    var popups = document.querySelectorAll('div.modal, div[role=""dialog""]');
                    for (var i = 0; i < popups.length; i++) {
                        var popup = popups[i];
                        if (popup.textContent.includes('Cambia la contraseña') && 
                            popup.style.display !== 'none') {
                            var buttons = popup.querySelectorAll('button');
                            for (var j = 0; j < buttons.length; j++) {
                                var button = buttons[j];
                                if (button.textContent.includes('Aceptar')) {
                                    button.click();
                                    return true;
                                }
                            }
                        }
                    }
                    return false;
                ");

                    if (jsResult is bool && (bool)jsResult)
                    {
                        _logger.Info("Popup de cambio de contraseña cerrado mediante JavaScript", true);
                    }
                    else
                    {
                        _logger.Warning("No se pudo encontrar el botón para cerrar el popup de cambio de contraseña");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error al intentar manejar el popup de cambio de contraseña: {ex.Message}");
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
        ") as bool? ?? false;

            if (popupExists)
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
                else
                {
                    // Intentar con el método convencional si el JavaScript avanzado falla
                    try
                    {
                        // Intentar encontrar el botón por diferentes selectores
                        var acceptButton = _driver.FindElements(By.XPath("//button[contains(text(), 'Aceptar') or contains(text(), 'Accept')]"))
                            .FirstOrDefault(b => b.Displayed && b.Enabled);

                        if (acceptButton != null)
                        {
                            await ClickWhenClickableAsync(By.XPath("//button[contains(text(), 'Aceptar') or contains(text(), 'Accept')]"), cachedElement: acceptButton);
                            _logger.Info("Se cerró el popup usando el método convencional", true);

                            // Esperar a que el popup se cierre
                            var dynamicWait = new DynamicWaitHelper(_driver);
                            await dynamicWait.WaitForConditionAsync(
                                d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait"))
                                    .Any(e => e.Displayed),
                                "after_conventional_close",
                                TimeSpan.FromMilliseconds(500)
                            );
                        }
                        else
                        {
                            _logger.Warning("No se pudo encontrar el botón para cerrar el popup de cambio de contraseña");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error al buscar el botón con método convencional: {ex.Message}");
                    }
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
                // ▸ 1. Obtener (o reutilizar) el elemento
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
                    // IgnoreExceptionTypes devuelve void ⇒ llamada independiente
                    wait.IgnoreExceptionTypes(
                        typeof(NoSuchElementException),
                        typeof(StaleElementReferenceException));

                    element = wait.Until(drv =>
                    {
                        var el = drv.FindElement(locator);
                        return (el.Displayed && el.Enabled) ? el : null;
                    });
                }

                // ▸ 2. Scroll al centro
                ((IJavaScriptExecutor)_driver)
                    .ExecuteScript(
                        "arguments[0].scrollIntoView({block:'center',inline:'center'});",
                        element);

                // ▸ 3. Clic normal
                try { element.Click(); return true; }
                catch (Exception ex) { _logger.Debug($"Clic nativo falló: {ex.Message}"); }

                // ▸ 4. Clic JavaScript
                try
                {
                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", element);
                    return true;
                }
                catch (Exception ex) { _logger.Debug($"Clic JS falló: {ex.Message}"); }

                // ▸ 5. Clic Actions
                try
                {
                    new Actions(_driver).MoveToElement(element).Click().Perform();
                    return true;
                }
                catch (Exception ex) { _logger.Debug($"Clic Actions falló: {ex.Message}"); }

                // ▸ 6. dispatchEvent como último recurso
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

            // ▸ 7. Breve pausa antes de reintentar (120–300 ms)
            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Min(300, timeout.Value.TotalMilliseconds / 25)),
                ct);
        }

        return false;   // Agotados los intentos
    }

    private async Task WaitForNoOverlayAsync(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(3);
        var dynamicWait = new DynamicWaitHelper(_driver);

        await dynamicWait.WaitForConditionAsync(
            d => !d.FindElements(By.CssSelector(".loading,.spinner,.wait,.ui-table-loading"))
                  .Any(e => e.Displayed),
            "no_overlay",
            timeout);
    }

    private async Task<IWebElement?> WaitForVehiclePopupAsync(
     TimeSpan? timeout = null,
     CancellationToken ct = default)
    {
        timeout ??= TimeSpan.FromSeconds(12);

        // NO CAMBIAR CONTEXTO - El popup está DENTRO del iframe
        // _driver.SwitchTo().DefaultContent();  ← COMENTAR ESTA LÍNEA

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();

            var popup = _driver.FindElements(By.CssSelector("div.olFramedCloudPopupContent"))
                               .FirstOrDefault(el => el.Displayed &&
                                                     !string.IsNullOrWhiteSpace(el.Text));

            if (popup != null) return popup;

            await Task.Delay(250, ct);
        }

        return null;
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