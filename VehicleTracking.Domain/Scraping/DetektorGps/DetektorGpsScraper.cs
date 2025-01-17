using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using VehicleTracking.Domain.Contracts;
using VehicleTracking.Domain.Contracts.IDetektorGps;
using VehicleTracking.Domain.Services;
using VehicleTracking.Shared.InDTO.DetektorGps;
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
            options.AddUserProfilePreference("credentials_enable_service", false);
            options.AddUserProfilePreference("profile.password_manager_enabled", false);
            if (_seleniumConfig.Headless)
            {
                options.AddArgument("--headless");
            }

            var chromeDriverService = string.IsNullOrEmpty(_seleniumConfig.ChromeDriverPath)
                ? ChromeDriverService.CreateDefaultService()
                : ChromeDriverService.CreateDefaultService(_seleniumConfig.ChromeDriverPath);

            chromeDriverService.HideCommandPromptWindow = true;

            _driver = new ChromeDriver(chromeDriverService, options);
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
                _logger.Warning("Credenciales inválidas: usuario o contraseña están vacíos", true);
                return false;
            }

            _logger.Debug("Iniciando proceso de login...");
            var dynamicWait = new DynamicWaitHelper(_driver);

            _logger.Debug("Navegando a la URL base...");
            _driver.Navigate().GoToUrl(_config.BaseUrl);

            // Verificar estado de la página después de navegar
            await CheckPageStatus("navegación inicial");

            _logger.Debug("Esperando que la página cargue completamente...");
            await dynamicWait.WaitForPageLoadAsync();

            _logger.Debug("Buscando campo de usuario...");
            var userInput = await dynamicWait.WaitForElementAsync(
                By.CssSelector("input[name='username'].form-control"),
                "login_username",
                ensureClickable: true);

            if (userInput == null)
            {
                _logger.Warning("No se pudo encontrar el campo de usuario", true);
                return false;
            }

            _logger.Debug("Buscando campo de contraseña...");
            var passInput = await dynamicWait.WaitForElementAsync(
                By.CssSelector("input[name='password'].form-control"),
                "login_password",
                ensureClickable: true);

            if (passInput == null)
            {
                _logger.Warning("No se pudo encontrar el campo de contraseña", true);
                return false;
            }

            _logger.Debug("Buscando botón de login...");
            var loginButton = await dynamicWait.WaitForElementAsync(
                By.Id("initSession"),
                "login_button",
                ensureClickable: true);

            if (loginButton == null)
            {
                _logger.Warning("No se pudo encontrar el botón de inicio de sesión", true);
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
            try
            {
                loginButton.Click();
            }
            catch (Exception ex)
            {
                _logger.Warning("Click normal falló, intentando con JavaScript...");
                IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;
                js.ExecuteScript("arguments[0].click();", loginButton);
            }

            // Verificar si hay errores de login visibles
            await Task.Delay(1000);
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
            await ClickElementWithRetry(skytrackButton);

            _logger.Debug("Esperando apertura de nueva pestaña...");
            await Task.Delay(500);  // Breve espera para estabilización
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
                    var menuElement = await dynamicWait.WaitForElementAsync(
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
                            return true;
                        }
                    }

                    // Verificar estado después de cada intento
                    await CheckPageStatus($"verificación post-login intento {attempt + 1}");

                    if (attempt < maxAttempts - 1)
                    {
                        _logger.Warning($"Intento {attempt + 1} fallido, reintentando verificación...");
                        await Task.Delay(1000);  // Espera entre intentos
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
            _logger.Error($"Error durante el proceso de login para usuario: {username}", ex);
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
                        var noLoading = await dynamicWait.WaitForConditionAsync(
                            d => {
                                try
                                {
                                    return !(bool)((IJavaScriptExecutor)d).ExecuteScript(
                                        "return !!document.querySelector('.ui-table-loading')?.offsetParent"
                                    );
                                }
                                catch { return false; }
                            },
                            "loading_check",
                            TimeSpan.FromSeconds(2)
                        );

                        if (!noLoading)
                        {
                            _logger.Warning($"Intento {clickAttempt + 1}: Overlay de carga aún presente");
                            continue;
                        }

                        // Refrescar el elemento antes de cada intento
                        var buttons = _driver.FindElements(By.CssSelector("button.ui-button-danger[icon='pi pi-compass']"))
                                            .Where(b => b.Displayed && b.Enabled)
                                            .ToList();

                        var button = buttons.FirstOrDefault();
                        if (button == null)
                        {
                            _logger.Warning($"Intento {clickAttempt + 1}: Botón no encontrado, reintentando...");
                            await Task.Delay(500);
                            continue;
                        }

                        // Asegurar que el botón está visible
                        ((IJavaScriptExecutor)_driver).ExecuteScript(
                            "arguments[0].scrollIntoView({block: 'center', behavior: 'instant'});",
                            button
                        );

                        await Task.Delay(200); // Breve espera para que el scroll termine

                        // Intentar el clic
                        await ClickElementWithRetry(button);
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
                        await Task.Delay(500);
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
            await ClickElementWithRetry(vehicleIcon);

            // Verificar estado antes de buscar el popup
            await CheckPageStatus("pre-búsqueda de popup");

            _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Buscando popup de información");

            // Intentar encontrar el popup de información
            var infoWindow = await FindPopupWithRetry();
            if (infoWindow == null)
            {
                throw new InvalidOperationException("No se pudo obtener la información del vehículo después de múltiples intentos");
            }

            _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds}ms] Popup encontrado, extrayendo información");

            // Verificar estado antes de extraer la información
            await CheckPageStatus("pre-extracción de información");

            // Extraer y retornar la información del vehículo
            var result = await ExtractVehicleInformation(infoWindow);

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
                            continue;
                        }

                        await ClickElementWithRetry(menuPrincipal);

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
                                await ClickElementWithRetry(menuPrincipal);
                            }
                            catch { }

                            await Task.Delay(100);
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
                        await Task.Delay(200);
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
                                await ClickElementWithRetry(menuPrincipal);
                                await Task.Delay(200);
                                continue;
                            }

                            throw new InvalidOperationException("No se pudo encontrar el botón de Informes");
                        }

                        await CheckPageStatus($"pre-clic informes intento {informesAttempts}");
                        await ClickElementWithRetry(informesButton);

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
                        await Task.Delay(200);
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
                        await ClickElementWithRetry(ultimoPuntoBetaButton);
                        await Task.Delay(1000);

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
                                    await ClickElementWithRetry(ultimoPuntoBetaButton);
                                    await Task.Delay(1000);

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
                        await ClickElementWithRetry(ultimoPuntoBetaButton);
                        await Task.Delay(1000);

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
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                _logger.Error($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Error en intento {attempt}", ex);
                if (attempt == maxAttempts)
                    throw new InvalidOperationException("Error durante la navegación", ex);
                await Task.Delay(1000);
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
            var searchDelay = 500; // Retraso inicial entre intentos

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
                        _logger.Warning($"[Tiempo: {stopwatch.ElapsedMilliseconds}ms] Intento {searchAttempt + 1} fallido, reintentando después de {searchDelay}ms");
                        await Task.Delay(searchDelay);
                        searchDelay = Math.Min(searchDelay * 2, 2000); // Incremento exponencial con límite
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
        var pollingInterval = 100; // Aumentado a 100ms para ser menos agresivo
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
                            await Task.Delay(2000); // Espera después del refresco
                        }
                    }
                }

                // Si estamos cerca del timeout y no hemos encontrado nada, intentar refrescar
                if (!hasTriedRefresh && DateTime.UtcNow - startTime > TimeSpan.FromSeconds(8))
                {
                    _logger.Debug("Acercándose al timeout, intentando refrescar tabla...");
                    await RefreshTableIfPossible();
                    hasTriedRefresh = true;
                    await Task.Delay(2000);
                }

                await Task.Delay(pollingInterval);
            }
            catch (Exception ex)
            {
                _logger.Warning($"[Tiempo: {globalStopwatch.ElapsedMilliseconds}ms] Error durante polling: {ex.Message}");
                await Task.Delay(pollingInterval);
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
                await ClickElementWithRetry(refreshButton);
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
        var checkInterval = TimeSpan.FromMilliseconds(50); // Polling frecuente

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
                            document.querySelector('.overlay:not([style*=""display: none""])')
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
                        await Task.Delay(100);

                        // Verificar que realmente se cerró
                        if (await VerifyPopupClosed())
                        {
                            break;
                        }
                    }
                }

                await Task.Delay(checkInterval);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error en monitoreo de popup: {ex.Message}");
                await Task.Delay(checkInterval);
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

            await Task.Delay(1000); // Esperar a que el mapa se actualice
            _logger.Debug("Arrastre del mapa completado");
        }
        catch (Exception ex)
        {
            _logger.Error("Error durante el arrastre del mapa", ex);
            throw;
        }
    }

    private async Task<IWebElement?> FindPopupWithRetry(int maxAttempts = 3)
    {
        try
        {
            _logger.Debug($"Iniciando búsqueda del popup con retry - intentos máximos: {maxAttempts}");

            // Obtener la posición del icono del vehículo
            var iconPosition = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            try {
                var icons = document.evaluate(""//*[name()='svg']//*[name()='image'][@width='35'][@height='35']"", 
                                           document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                var rect = icons.getBoundingClientRect();
                return {
                    top: rect.top,
                    bottom: rect.bottom,
                    left: rect.left,
                    right: rect.right,
                    viewportWidth: window.innerWidth,
                    viewportHeight: window.innerHeight
                };
            } catch(e) {
                console.error('Error:', e);
                return null;
            }
        ") as Dictionary<string, object>;

            if (iconPosition == null)
            {
                _logger.Warning("No se pudo obtener la posición del ícono", true);
                return null;
            }

            double iconTop = Convert.ToDouble(iconPosition["top"]);
            double iconBottom = Convert.ToDouble(iconPosition["bottom"]);
            double iconLeft = Convert.ToDouble(iconPosition["left"]);
            double iconRight = Convert.ToDouble(iconPosition["right"]);
            double viewportWidth = Convert.ToDouble(iconPosition["viewportWidth"]);
            double viewportHeight = Convert.ToDouble(iconPosition["viewportHeight"]);

            _logger.Debug($"Posición del ícono:\n" +
                $"Top: {iconTop}px\n" +
                $"Bottom: {iconBottom}px\n" +
                $"Left: {iconLeft}px\n" +
                $"Right: {iconRight}px\n" +
                $"Viewport: {viewportWidth}x{viewportHeight}");

            var mapElement = _driver.FindElement(By.CssSelector("div.olMap"));
            int centerX = (int)viewportWidth / 2;
            int centerY = (int)viewportHeight / 2;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    _logger.Debug($"Intento {attempt + 1} de encontrar el popup");

                    // Intentar encontrar el popup (aunque no sea visible)
                    var popup = _driver.FindElement(By.CssSelector("div.olFramedCloudPopupContent"));

                    if (!popup.Displayed)
                    {
                        _logger.Debug("Popup encontrado pero no visible, iniciando arrastre del mapa");

                        // La distancia base de arrastre es menor y aumenta gradualmente
                        int dragDistance = 100 * (attempt + 1);

                        // IMPORTANTE: El arrastre del mapa debe ser en dirección OPUESTA 
                        // a donde queremos que se mueva el ícono
                        if (iconTop < 100)
                        {
                            _logger.Debug($"Ícono muy cerca del borde superior ({iconTop}px), " +
                                                $"arrastrando mapa hacia ARRIBA para que el ícono baje");
                            await DragMap(mapElement, centerX, centerY, 0, dragDistance);
                        }
                        else if (iconBottom > viewportHeight - 100)
                        {
                            _logger.Debug($"Ícono muy cerca del borde inferior ({viewportHeight - iconBottom}px), " +
                                                $"arrastrando mapa hacia ABAJO para que el ícono suba");
                            await DragMap(mapElement, centerX, centerY, 0, -dragDistance);
                        }
                        else if (iconLeft < 100)
                        {
                            _logger.Debug($"Ícono muy cerca del borde izquierdo ({iconLeft}px), " +
                                                $"arrastrando mapa hacia la IZQUIERDA para que el ícono se mueva a la derecha");
                            await DragMap(mapElement, centerX, centerY, dragDistance, 0);
                        }
                        else if (iconRight > viewportWidth - 100)
                        {
                            _logger.Debug($"Ícono muy cerca del borde derecho ({viewportWidth - iconRight}px), " +
                                                $"arrastrando mapa hacia la DERECHA para que el ícono se mueva a la izquierda");
                            await DragMap(mapElement, centerX, centerY, -dragDistance, 0);
                        }

                        await Task.Delay(1000);

                        // Verificar si el popup ahora es visible
                        popup = _driver.FindElement(By.CssSelector("div.olFramedCloudPopupContent"));
                        if (popup.Displayed && !string.IsNullOrWhiteSpace(popup.Text))
                        {
                            _logger.Info("Popup ahora visible después del arrastre", true); ;
                            return popup;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(popup.Text))
                    {
                        _logger.Info("Popup encontrado y visible con contenido", true);
                        return popup;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error en intento {attempt + 1}", ex);
                    if (attempt == maxAttempts - 1) throw;
                }

                await Task.Delay(1000);
            }

            _logger.Error("No se pudo encontrar el popup después de todos los intentos");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error("Error general en FindPopupWithRetry", ex);
            throw;
        }
    }

    private async Task<LocationDataInfo> ExtractVehicleInformation(IWebElement infoWindow)
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
                Temperature = ParseDecimalOrDefault(tempMatch.Groups[1].Value)
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
                    var element = await dynamicWait.WaitForElementAsync(selector, ensureClickable: true);
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

    private async Task<bool> ClickElementWithRetry(IWebElement element, int maxAttempts = 3)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                await Task.Delay(500);
                if (!element.Displayed)
                {
                    _logger.Warning($"Elemento no visible en intento {i + 1}");
                    continue;
                }

                // Intentar hacer scroll al elemento
                try
                {
                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView(true);", element);
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error al hacer scroll al elemento: {ex.Message}");
                }

                // Primero intentar un clic normal
                try
                {
                    _logger.Debug("Intentando clic normal...");
                    element.Click();
                    return true;
                }
                catch (Exception clickEx)
                {
                    _logger.Warning($"Clic normal falló: {clickEx.Message}, intentando alternativas...");

                    // Intentar clic mediante JavaScript con diferentes enfoques
                    try
                    {
                        _logger.Debug("Intentando clic con JavaScript (dispatchEvent)...");
                        ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                        var evt = new MouseEvent('click', {
                            bubbles: true,
                            cancelable: true,
                            view: window
                        });
                        arguments[0].dispatchEvent(evt);
                    ", element);
                        await Task.Delay(500);
                        return true;
                    }
                    catch (Exception jsEx1)
                    {
                        _logger.Warning($"Primer método JavaScript falló: {jsEx1.Message}, intentando siguiente método...");

                        // Intento alternativo específico para elementos SVG
                        try
                        {
                            _logger.Debug("Intentando clic con JavaScript (getBoundingClientRect)...");
                            ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                            function clickElement(element) {
                                var rect = element.getBoundingClientRect();
                                var centerX = rect.left + (rect.width / 2);
                                var centerY = rect.top + (rect.height / 2);
                                
                                var clickEvent = new MouseEvent('click', {
                                    'view': window,
                                    'bubbles': true,
                                    'cancelable': true,
                                    'screenX': centerX,
                                    'screenY': centerY,
                                    'clientX': centerX,
                                    'clientY': centerY
                                });
                                
                                element.dispatchEvent(clickEvent);
                            }
                            clickElement(arguments[0]);
                        ", element);
                            await Task.Delay(500);
                            return true;
                        }
                        catch (Exception jsEx2)
                        {
                            _logger.Warning($"Segundo método JavaScript falló: {jsEx2.Message}");

                            // Si es el último intento, intentar con Actions
                            if (i == maxAttempts - 1)
                            {
                                try
                                {
                                    _logger.Debug("Intentando clic con Actions...");
                                    var actions = new Actions(_driver);
                                    actions.MoveToElement(element).Click().Perform();
                                    return true;
                                }
                                catch (Exception actionEx)
                                {
                                    _logger.Error($"Clic con Actions falló", actionEx);
                                    throw;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error en intento {i + 1} de click: {ex.Message}");
                if (i == maxAttempts - 1) throw;
            }

            await Task.Delay(1000); // Espera entre intentos
        }

        return false;
    }

    private async Task<IWebElement?> FindVehicleIcon()
    {
        try
        {
            _logger.Debug("Iniciando búsqueda del ícono del vehículo...");
            await Task.Delay(3000);

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
                var ultimoPuntoButton = _wait.Until(d =>
                {
                    try
                    {
                        var button = d.FindElement(By.XPath("//a[contains(@class, 'button')][.//span[text()='Ultimo Punto']]"));
                        _logger.Debug("Botón 'Ultimo Punto' encontrado");
                        return button;
                    }
                    catch
                    {
                        _logger.Warning("No se encontró el botón 'Ultimo Punto' en este intento");
                        return null;
                    }
                });

                if (ultimoPuntoButton != null)
                {
                    _logger.Debug("Intentando hacer clic en botón 'Ultimo Punto'...");
                    await ClickElementWithRetry(ultimoPuntoButton);
                    await Task.Delay(2000);
                    _logger.Debug("Clic en 'Ultimo Punto' realizado");                    
                }
                else
                {
                    _logger.Error("No se pudo encontrar el botón 'Ultimo Punto'");
                    return null;
                }

                // 2. Minimizar el popup usando el toggle
                _logger.Debug("Paso 2: Buscando botón toggle para minimizar popup...");
                var toggleButton = _wait.Until(d =>
                {
                    try
                    {
                        var button = d.FindElement(By.Id("ext-gen17"));
                        _logger.Debug("Botón toggle encontrado");
                        return button;
                    }
                    catch
                    {
                        _logger.Warning("No se encontró el botón toggle en este intento");
                        return null;
                    }
                });

                if (toggleButton != null)
                {
                    _logger.Debug("Intentando minimizar popup con botón toggle...");
                    await ClickElementWithRetry(toggleButton);
                    await Task.Delay(1000);
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
                            _logger.LogInformation('Zoom actual: ' + currentZoom);
                            map.zoomTo(currentZoom - 2);
                            return true;
                        }
                        return false;
                    } catch(e) {
                        console.error('Error durante zoom:', e);
                        return false;
                    }
                ");
                    await Task.Delay(2000);
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
                await ClickElementWithRetry(ultimoPuntoButton);
                await Task.Delay(1000);
            }

            // 2.2 Minimizar el popup
            var toggleButton = _driver.FindElement(By.Id("ext-gen17"));
            if (toggleButton != null)
            {
                _logger.Debug("Minimizando popup");
                await ClickElementWithRetry(toggleButton);
                await Task.Delay(1000);
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
            await Task.Delay(2000);

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
                            await ClickElementWithRetry(button);
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