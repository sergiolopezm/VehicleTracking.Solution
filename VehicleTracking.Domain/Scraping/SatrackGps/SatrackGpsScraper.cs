﻿using System.Diagnostics;
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

                // IMPORTANTE: Manejar página "no encontrada" que puede aparecer después del login
                var notFoundHandled = await HandlePageNotFoundError();
                if (!notFoundHandled)
                {
                    _logger.Warning("Se detectó página 'no encontrada' pero no se pudo manejar correctamente", true);
                    // Continuamos de todos modos, ya que podría ser un falso positivo
                }

                _logger.Debug("Login ejecutado, esperando redirección a página principal...");

                // Espera más robusta para la página post-login
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

                // 1️⃣ Validar salud de la página
                await CheckPageStatus("inicio de búsqueda de vehículo");

                // Verificar si estamos en la página "no encontrada" y manejarla
                var notFoundHandled = await HandlePageNotFoundError();
                if (!notFoundHandled)
                {
                    _logger.Warning("Se detectó página 'no encontrada' durante la búsqueda del vehículo", true);
                    try
                    {
                        _driver.Navigate().Refresh();
                        await Task.Delay(3000);
                    }
                    catch (Exception refreshEx)
                    {
                        _logger.Warning($"Error al intentar refrescar la página: {refreshEx.Message}", true);
                    }
                }

                var dynamicWait = new DynamicWaitHelper(_driver);

                // 2️⃣ NUEVA VERIFICACIÓN RÁPIDA: ¿La placa específica ya está visible?
                var plateAlreadyReady = await IsSpecificPlateReady(patent, 3);
                if (plateAlreadyReady)
                {
                    _logger.Info($"🚀 Placa {patent} detectada inmediatamente, omitiendo esperas largas", true);
                }
                else
                {
                    // 3️⃣ Solo si la placa no está lista, hacer verificaciones completas
                    await EnsureMainPageIsFullyLoaded(dynamicWait);
                    await NavigateToVehiclesSection(dynamicWait);

                    // 4️⃣ Esperar de forma **optimizada** a que la lista de vehículos esté lista
                    var listReady = await WaitForVehicleListReady(dynamicWait, TimeSpan.FromSeconds(8)); // Reducido de 60s
                    if (!listReady)
                    {
                        _logger.Warning("La lista de vehículos no estuvo disponible, pero continuando...", true);
                    }
                }

                _logger.Debug($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds} ms] " +
                              $"Iniciando búsqueda de vehículo {patent}");

                // 5️⃣ Buscar el vehículo (este proceso ya es eficiente)
                var vehicleElement = await FindVehicleInVehicleList(patent, dynamicWait);
                if (vehicleElement == null)
                    throw new InvalidOperationException(
                        $"CONFIGURACION_INVALIDA: No se encontró el vehículo con placa {patent}");

                // 6️⃣ Clic en la placa para centrar el mapa
                IWebElement? elementToClick = vehicleElement;

                var plateElement = await FindPlateElementInContainer(vehicleElement, patent);
                if (plateElement != null)
                {
                    _logger.Info($"Encontrado elemento específico de placa para {patent}", true);
                    elementToClick = plateElement;
                }

                var clickResult = await ClickWhenClickableAsync(
                    By.Id(elementToClick.GetAttribute("id") ?? string.Empty),
                    cachedElement: elementToClick);

                if (!clickResult)
                {
                    _logger.Warning($"No se pudo hacer clic en el elemento de placa {patent}. Intentando alternativas...", true);

                    try
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    var evt = document.createEvent('MouseEvents');
                    evt.initEvent('mousedown', true, true);
                    arguments[0].dispatchEvent(evt);
                    
                    var selectEvent = new CustomEvent('vehicleSelected', { detail: { plate: arguments[1] } });
                    document.dispatchEvent(selectEvent);
                ", elementToClick, patent);

                        _logger.Debug("Simulación de selección ejecutada con JavaScript");
                    }
                    catch (Exception jsEx)
                    {
                        _logger.Warning($"Error en simulación JavaScript: {jsEx.Message}", true);
                    }
                }

                _logger.Info($"[Tiempo transcurrido: {stopwatch.ElapsedMilliseconds} ms] " +
                             $"Vehículo {patent} seleccionado", true);

                // 7️⃣ Esperar mínimamente a que el mapa termine de actualizar
                await dynamicWait.WaitForConditionAsync(
                    d => !d.FindElements(By.CssSelector(".loading, .spinner, .wait"))
                           .Any(e => e.Displayed),
                    "map_update_complete",
                    TimeSpan.FromSeconds(3) // Reducido de 10s
                );

                // 8️⃣ Extraer información
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
                _logger.Debug($"Buscando vehículo con placa {patent} en la lista…");

                /* ───────────────────────────────────────── 0️⃣  intento directo */
                var plateSpan = await FindVehiclePlateSpan(patent);
                if (plateSpan is not null)
                {
                    _logger.Info($"Placa {patent} encontrada directamente en pantalla", true);
                    return plateSpan;
                }

                /* ───────────────────────────────────────── 1️⃣  contenedor lista */
                var vehicleList = await WaitForVehicleListContainer(dynamicWait);
                if (vehicleList is null)
                {
                    _logger.Warning("No se encontró el contenedor de la lista de vehículos", true);
                    return null;
                }

                /* ───────────────────────────────────────── 2️⃣  filtro rápido   */
                var (searchInput, _) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector(
                        "input[placeholder*='Buscar'],input[aria-label*='buscar']," +
                        "input[placeholder*='Filtrar'],input.search-input"),
                    "search_input",
                    ensureClickable: false /* solo necesitamos escribir */);

                if (searchInput is not null)
                {
                    try
                    {
                        searchInput.Clear();
                        searchInput.SendKeys(patent + Keys.Enter);

                        await dynamicWait.WaitForAjaxCompletionAsync();
                        await Task.Delay(250); // pequeño respiro para el re-render
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error al interactuar con el cuadro de búsqueda: {ex.Message}", true);
                    }

                    plateSpan = await FindVehiclePlateSpan(patent);
                    if (plateSpan is not null)
                    {
                        _logger.Info($"Placa {patent} encontrada después de filtrar", true);
                        return plateSpan;
                    }
                }

                /* ───────────────────────────────────────── 3️⃣  búsqueda puntual */
                var vehicleItem = await FindVehicleItemByPatent(patent, dynamicWait);
                if (vehicleItem is not null)
                {
                    _logger.Info($"Vehículo {patent} encontrado por selector puntual", true);
                    return vehicleItem;
                }

                /* ───────────────────────────────────────── 4️⃣  scroll progresivo */
                var scrolledElement = await ScrollAndFindVehicle(patent, vehicleList, dynamicWait);
                if (scrolledElement is not null)
                    return scrolledElement;

                /* ───────────────────────────────────────── 5️⃣  búsqueda JS global */
                var jsElement = (IWebElement?)((IJavaScriptExecutor)_driver).ExecuteScript(@"
            function findPlate(plate){
                // span clásico
                const span = Array.from(document.querySelectorAll('span.notranslate'))
                                   .find(s=>s.textContent.trim()===plate);
                if (span) return span;

                // div con id que contenga la placa
                const div = document.querySelector(`div[id*='${plate}']`);
                if (div) return div;

                // cualquier nodo de texto exacto visible
                const walker = document.createTreeWalker(document.body,
                    NodeFilter.SHOW_TEXT,null,false);
                while(walker.nextNode()){
                    const n = walker.currentNode;
                    if(n.nodeValue.trim()===plate &&
                       n.parentElement.offsetWidth>0 &&
                       n.parentElement.offsetHeight>0){
                        return n.parentElement;
                    }
                }
                return null;
            }
            return findPlate(arguments[0]);", patent);

                if (jsElement is not null)
                {
                    ((IJavaScriptExecutor)_driver)
                        .ExecuteScript("arguments[0].scrollIntoView({block:'center'});", jsElement);

                    _logger.Info($"Placa {patent} localizada mediante búsqueda JavaScript exhaustiva", true);
                    return jsElement;
                }

                _logger.Warning($"No se pudo localizar la placa {patent} tras agotar todos los métodos", true);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error en FindVehicleInVehicleList: {ex.Message}", ex);
                return null;
            }
        }

        private async Task<IWebElement?> FindVehicleItemByPatent(string patent, DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug($"Buscando vehículo con placa {patent} en la lista de vehículos");

                // 1. Selectores específicos para las placas basados en el HTML observado
                var exactPlateSelectors = new[]
                {
                    // id exacto (sin guion bajo)
                    $"//*[@id='{patent}alias']",
                    // id que contiene la placa (por si cambia el sufijo)
                    $"//*[@id[contains(.,'{patent}')]]",
                    // div con clase item-veh-plate cuyo id contiene la placa
                    $"//div[contains(@class,'item-veh-plate') and contains(@id,'{patent}')]",
                    // span notranslate con el texto exacto
                    $"//span[@class='notranslate' and normalize-space(text())='{patent}']"
                };

                // 2. Intentar primero búsqueda directa sin scroll
                foreach (var selector in exactPlateSelectors)
                {
                    try
                    {
                        var elements = _driver.FindElements(By.XPath(selector));
                        var visibleElement = elements.FirstOrDefault(e => e.Displayed);

                        if (visibleElement != null)
                        {
                            _logger.Info($"Placa {patent} encontrada directamente con selector: {selector}", true);
                            return visibleElement;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error en búsqueda directa con selector '{selector}': {ex.Message}");
                    }
                }

                // 3. Buscar contenedor de lista de vehículos para hacer scroll
                var vehicleListContainers = new[]
                {
            "div.vehicle-list",
            ".leaflet-sidebar-content",
            "[class*='vehicle-list']",
            "[class*='sidebar'] [class*='content']",
            "div:has(div.item-veh-plate)"
        };

                IWebElement? listContainer = null;
                foreach (var containerSelector in vehicleListContainers)
                {
                    try
                    {
                        var containers = _driver.FindElements(By.CssSelector(containerSelector));
                        listContainer = containers.FirstOrDefault(c => c.Displayed && c.Size.Height > 100);

                        if (listContainer != null)
                        {
                            _logger.Debug($"Contenedor de lista de vehículos encontrado con selector: {containerSelector}");
                            break;
                        }
                    }
                    catch { /* Continuar con el siguiente selector */ }
                }

                if (listContainer == null)
                {
                    _logger.Warning("No se pudo encontrar un contenedor de lista de vehículos para hacer scroll", true);

                    // Intentar usar JavaScript para encontrar el contenedor
                    try
                    {
                        var jsContainer = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    // Buscar contenedor scrollable que contenga placas de vehículos
                    function findScrollableContainer() {
                        // Primero buscar por elementos que contengan la palabra 'vehicle' o 'veh' en sus clases
                        const vehicleContainers = Array.from(document.querySelectorAll('[class*=""vehicle""], [class*=""veh""]'))
                            .filter(el => el.scrollHeight > el.clientHeight);
                        
                        if (vehicleContainers.length > 0) {
                            return vehicleContainers[0];
                        }
                        
                        // Si no, buscar cualquier contenedor scrollable visible
                        return Array.from(document.querySelectorAll('div'))
                            .filter(el => 
                                el.scrollHeight > el.clientHeight && 
                                el.offsetWidth > 0 && 
                                el.offsetHeight > 100 &&
                                window.getComputedStyle(el).overflow.includes('scroll') || 
                                window.getComputedStyle(el).overflowY.includes('scroll') ||
                                window.getComputedStyle(el).overflow === 'auto' || 
                                window.getComputedStyle(el).overflowY === 'auto'
                            )[0];
                    }
                    
                    return findScrollableContainer();
                ");

                        if (jsContainer != null)
                        {
                            listContainer = (IWebElement)jsContainer;
                            _logger.Debug("Contenedor de lista de vehículos encontrado mediante JavaScript");
                        }
                    }
                    catch { /* Ignorar errores en JavaScript */ }
                }

                // 4. Si tenemos un contenedor, hacer scroll y buscar la placa
                if (listContainer != null)
                {
                    _logger.Debug($"Iniciando scroll para buscar placa {patent}");

                    // Primero, scrollear al inicio
                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollTop = 0;", listContainer);
                    await Task.Delay(500);

                    // Intentar buscar la placa de nuevo
                    foreach (var selector in exactPlateSelectors)
                    {
                        try
                        {
                            var elements = _driver.FindElements(By.XPath(selector));
                            var visibleElement = elements.FirstOrDefault(e => e.Displayed);

                            if (visibleElement != null)
                            {
                                _logger.Info($"Placa {patent} encontrada después de scroll al inicio", true);
                                return visibleElement;
                            }
                        }
                        catch { /* Continuar con el siguiente selector */ }
                    }

                    // Calcular parámetros para scroll progresivo
                    int scrollHeight = 0;
                    try
                    {
                        scrollHeight = Convert.ToInt32(((IJavaScriptExecutor)_driver).ExecuteScript("return arguments[0].scrollHeight", listContainer));
                    }
                    catch
                    {
                        scrollHeight = 1000; // Valor predeterminado si no podemos obtener el scrollHeight
                    }

                    int clientHeight = 0;
                    try
                    {
                        clientHeight = Convert.ToInt32(((IJavaScriptExecutor)_driver).ExecuteScript("return arguments[0].clientHeight", listContainer));
                    }
                    catch
                    {
                        clientHeight = 200; // Valor predeterminado si no podemos obtener el clientHeight
                    }

                    // Usar un paso de scroll más pequeño para no saltarse elementos
                    int scrollStep = Math.Max(clientHeight / 2, 50);
                    int maxScrolls = (scrollHeight / scrollStep) + 5; // +5 para asegurar que llegamos al final

                    _logger.Debug($"Iniciando scroll progresivo: altura={scrollHeight}, paso={scrollStep}, máx. pasos={maxScrolls}");

                    // Scroll progresivo a través de la lista
                    for (int i = 0; i < maxScrolls; i++)
                    {
                        // Scroll un paso hacia abajo
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollTop += arguments[1];", listContainer, scrollStep);
                        await Task.Delay(300); // Pausa para permitir que se carguen elementos

                        // Verificar si la placa es visible después del scroll
                        foreach (var selector in exactPlateSelectors)
                        {
                            try
                            {
                                var elements = _driver.FindElements(By.XPath(selector));
                                var visibleElement = elements.FirstOrDefault(e => e.Displayed);

                                if (visibleElement != null)
                                {
                                    _logger.Info($"Placa {patent} encontrada después de {i + 1} pasos de scroll", true);

                                    // Scroll adicional para centrar el elemento
                                    try
                                    {
                                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", visibleElement);
                                        await Task.Delay(300);
                                    }
                                    catch { /* Ignorar errores de centrado */ }

                                    return visibleElement;
                                }
                            }
                            catch { /* Continuar con el siguiente selector */ }
                        }

                        // Verificar si hemos llegado al final del scroll
                        var currentScrollTop = Convert.ToInt32(((IJavaScriptExecutor)_driver).ExecuteScript("return arguments[0].scrollTop;", listContainer));
                        var isAtBottom = (currentScrollTop + clientHeight >= scrollHeight - 10);

                        if (isAtBottom)
                        {
                            _logger.Debug("Llegamos al final de la lista sin encontrar la placa");
                            break;
                        }
                    }

                    // Búsqueda final utilizando JavaScript para examinar todo el DOM
                    try
                    {
                        var jsElement = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    function findPlateElement(patent) {
                        // Buscar por ID exacto primero
                        const idSelectors = [
                            `div[id='${patent}_alias']`,
                            `div[id='${patent}*alias']`,
                            `[id*='${patent}']`
                        ];
                        
                        for (const selector of idSelectors) {
                            const element = document.querySelector(selector);
                            if (element && element.offsetWidth > 0 && element.offsetHeight > 0) {
                                return element;
                            }
                        }
                        
                        // Buscar span con el texto exacto
                        const spans = Array.from(document.querySelectorAll('span'));
                        for (const span of spans) {
                            if (span.textContent.trim() === patent && 
                                span.offsetWidth > 0 && 
                                span.offsetHeight > 0) {
                                return span;
                            }
                        }
                        
                        // Buscar cualquier elemento con el texto exacto
                        const walker = document.createTreeWalker(
                            document.body, 
                            NodeFilter.SHOW_TEXT, 
                            { acceptNode: function(node) { 
                                return node.textContent.trim() === patent ? 
                                    NodeFilter.FILTER_ACCEPT : 
                                    NodeFilter.FILTER_REJECT; 
                              }
                            }, 
                            false
                        );
                        
                        while (walker.nextNode()) {
                            const node = walker.currentNode;
                            if (node.parentElement && 
                                node.parentElement.offsetWidth > 0 && 
                                node.parentElement.offsetHeight > 0) {
                                return node.parentElement;
                            }
                        }
                        
                        return null;
                    }
                    
                    return findPlateElement(arguments[0]);
                ", patent);

                        if (jsElement != null)
                        {
                            _logger.Info($"Placa {patent} encontrada mediante búsqueda JavaScript completa", true);

                            // Asegurarse de que el elemento está visible
                            ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", jsElement);
                            await Task.Delay(300);

                            return (IWebElement)jsElement;
                        }
                    }
                    catch (Exception jsEx)
                    {
                        _logger.Debug($"Error en búsqueda JavaScript: {jsEx.Message}");
                    }
                }

                // 5. Último intento: Buscar en toda la página
                _logger.Warning($"No se pudo encontrar la placa {patent} en la lista de vehículos usando scroll. Intentando búsqueda global...", true);

                try
                {
                    // Intentar búsqueda global en todo el DOM
                    var globalSelectors = new[]
                    {
                $"//div[contains(text(), '{patent}')]",
                $"//span[contains(text(), '{patent}')]",
                $"//a[contains(text(), '{patent}')]",
                $"//div[contains(@id, '{patent}')]",
                $"//*[text()='{patent}']"
            };

                    foreach (var selector in globalSelectors)
                    {
                        var elements = _driver.FindElements(By.XPath(selector));
                        var visibleElement = elements.FirstOrDefault(e => e.Displayed);

                        if (visibleElement != null)
                        {
                            _logger.Info($"Placa {patent} encontrada mediante búsqueda global con selector: {selector}", true);
                            return visibleElement;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error en búsqueda global: {ex.Message}", true);
                }

                _logger.Warning($"No se pudo encontrar la placa {patent} usando ningún método", true);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error en FindVehicleItemByPatent: {ex.Message}", true);
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

                // Esperar a que el popup de información aparezca
                var (popup, _) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector(".leaflet-popup-content, .gm-style-iw-d"),
                    "vehicle_popup",
                    ensureClickable: false
                );

                if (popup == null)
                {
                    _logger.Warning("No se pudo encontrar el popup de información del vehículo", true);
                    throw new InvalidOperationException("No se pudo encontrar el popup de información del vehículo");
                }

                // Inicializar el objeto LocationDataInfo
                var locationInfo = new LocationDataInfo
                {
                    // Valores predeterminados
                    Speed = 0,
                    Timestamp = DateTime.Now,
                    Driver = string.Empty,
                    Georeference = string.Empty,
                    InZone = "N/A",
                    DetentionTime = "0",
                    DistanceTraveled = 0,
                    Temperature = 0,
                    Angle = 0,
                    Reason = string.Empty,
                    Latitude = 0,
                    Longitude = 0
                };

                // Extraer coordenadas (Lat/Long)
                try
                {
                    var positionElement = popup.FindElement(By.Id("iwv-position-value"));
                    if (positionElement != null)
                    {
                        var positionText = positionElement.Text.Trim();
                        _logger.Debug($"Texto de posición: {positionText}");

                        var coordinates = positionText.Split(',');
                        if (coordinates.Length == 2)
                        {
                            if (decimal.TryParse(coordinates[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal latitude) &&
                                decimal.TryParse(coordinates[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal longitude))
                            {
                                locationInfo.Latitude = latitude;
                                locationInfo.Longitude = longitude;
                                _logger.Debug($"Coordenadas extraídas: {latitude}, {longitude}");
                            }
                            else
                            {
                                _logger.Warning($"No se pudieron parsear las coordenadas: {positionText}", true);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error al extraer coordenadas: {ex.Message}", true);
                }

                // Extraer dirección (Georeference)
                try
                {
                    var addressElement = popup.FindElement(By.Id("iw-vehicle-address"));
                    if (addressElement != null)
                    {
                        var addressText = addressElement.Text.Trim();
                        _logger.Debug($"Texto de dirección completo: {addressText}");

                        // Intentar extraer solo el texto de la dirección sin etiquetas
                        locationInfo.Georeference = addressText;
                        _logger.Debug($"Dirección extraída: {locationInfo.Georeference}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error al extraer dirección: {ex.Message}", true);
                }

                // Extraer estado del vehículo (Reason)
                try
                {
                    var stateElement = popup.FindElement(By.Id("iwv-state-value-txt"));
                    if (stateElement != null)
                    {
                        locationInfo.Reason = stateElement.Text.Trim();
                        _logger.Debug($"Estado extraído: {locationInfo.Reason}");

                        // Determinar Driver basado en el estado
                        switch (locationInfo.Reason.ToLower())
                        {
                            case "en movimiento":
                                locationInfo.Driver = "Conductor manejando";
                                break;
                            case "detenido":
                            case "detenido (ralentí)":
                                locationInfo.Driver = "Conductor ausente";
                                break;
                            case "apagado":
                                locationInfo.Driver = "Sin conductor";
                                break;
                            default:
                                locationInfo.Driver = "Estado desconocido";
                                break;
                        }
                    }
                    else
                    {
                        _logger.Debug("Elemento iwv-state-value-txt no encontrado, buscando alternativas...");

                        // Buscar alternativas para el estado
                        var stateContainer = popup.FindElement(By.Id("iw-vehicle-state"));
                        if (stateContainer != null)
                        {
                            locationInfo.Reason = stateContainer.Text.Replace("Estado:", "").Trim();
                            _logger.Debug($"Estado extraído de contenedor: {locationInfo.Reason}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error al extraer estado: {ex.Message}", true);
                }

                // Extraer velocidad
                try
                {
                    var speedElement = popup.FindElement(By.Id("iwv-speed-value"));
                    if (speedElement != null)
                    {
                        var speedText = speedElement.Text.Trim().Replace("km/h", "").Trim();
                        _logger.Debug($"Texto de velocidad: {speedText}");

                        if (decimal.TryParse(speedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal speed))
                        {
                            locationInfo.Speed = speed;
                            _logger.Debug($"Velocidad extraída: {speed} km/h");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error al extraer velocidad: {ex.Message}", true);
                }

                // Extraer sentido y calcular ángulo
                try
                {
                    var headingElement = popup.FindElement(By.Id("iwv-heading-value"));
                    if (headingElement != null)
                    {
                        var heading = headingElement.Text.Trim();
                        _logger.Debug($"Sentido extraído: {heading}");

                        // Convertir sentido textual a ángulo aproximado
                        locationInfo.Angle = ConvertHeadingToAngle(heading);
                        _logger.Debug($"Ángulo calculado: {locationInfo.Angle}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error al extraer sentido: {ex.Message}", true);
                }

                // Extraer tiempo sin moverse (solo visible en estados Detenido o Apagado)
                try
                {
                    var detentionElement = popup.FindElement(By.Id("iwv-timeWithoutMoving-value"));
                    if (detentionElement != null)
                    {
                        locationInfo.DetentionTime = detentionElement.Text.Trim();
                        _logger.Debug($"Tiempo sin moverse extraído: {locationInfo.DetentionTime}");
                    }
                }
                catch (Exception ex)
                {
                    // El elemento puede no estar presente si el vehículo está en movimiento
                    _logger.Debug($"No se encontró tiempo de detención (posiblemente en movimiento): {ex.Message}");
                    locationInfo.DetentionTime = "0";
                }

                // MÉTODOS MEJORADOS PARA EXTRAER TIMESTAMP

                // 1. Intentar obtener directamente del elemento de último reporte
                try
                {
                    var lastReportElement = popup.FindElement(By.Id("iw-vehicle-lastReport"));
                    if (lastReportElement != null)
                    {
                        var lastReportText = lastReportElement.Text.Trim();
                        _logger.Debug($"Texto completo de último reporte: {lastReportText}");

                        // Intentar extraer hora del formato "hace X min/horas"
                        var timePattern = @"hace\s+(\d+)\s+(min|hora|horas|día|días)";
                        var match = Regex.Match(lastReportText, timePattern);

                        if (match.Success)
                        {
                            var amount = int.Parse(match.Groups[1].Value);
                            var unit = match.Groups[2].Value;

                            DateTime reportTime = DateTime.Now;
                            switch (unit)
                            {
                                case "min":
                                    reportTime = reportTime.AddMinutes(-amount);
                                    break;
                                case "hora":
                                case "horas":
                                    reportTime = reportTime.AddHours(-amount);
                                    break;
                                case "día":
                                case "días":
                                    reportTime = reportTime.AddDays(-amount);
                                    break;
                            }

                            locationInfo.Timestamp = reportTime;
                            _logger.Debug($"Timestamp calculado del texto 'hace X': {reportTime}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error al extraer timestamp de último reporte: {ex.Message}");
                }

                // 2. Intentar extraer del tooltip mediante simulación de hover
                try
                {
                    _logger.Debug("Intentando extraer timestamp mediante simulación de hover en el reloj...");

                    var lastReportContainer = popup.FindElement(By.Id("iw-vehicle-lastReport"));
                    var clockIcon = lastReportContainer.FindElement(By.Id("iconLastReport"));

                    if (clockIcon != null)
                    {
                        _logger.Debug("Icono de reloj encontrado, intentando simulación de hover...");

                        // Intentar mostrar el tooltip con hover mediante JavaScript
                        ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    var event = new MouseEvent('mouseover', {
                        'view': window,
                        'bubbles': true,
                        'cancelable': true
                    });
                    arguments[0].dispatchEvent(event);
                ", clockIcon);

                        // Esperar brevemente para que aparezca el tooltip
                        await Task.Delay(500);

                        // Intentar leer el tooltip después del hover
                        var tooltipElement = popup.FindElement(By.Id("iw-last-report-value"));
                        if (tooltipElement != null)
                        {
                            var tooltipText = tooltipElement.GetAttribute("textContent") ?? tooltipElement.Text;
                            _logger.Debug($"Texto obtenido del tooltip después de hover: {tooltipText}");

                            if (!string.IsNullOrEmpty(tooltipText))
                            {
                                // Intentar extraer fecha y hora del texto
                                var dateTimePattern = @"(Hoy|Ayer|\d{2}/\d{2}/\d{4})\s*,\s*(\d{2}:\d{2}:\d{2}\s*(?:AM|PM|a\.m\.|p\.m\.)?)";
                                var match = Regex.Match(tooltipText, dateTimePattern);

                                if (match.Success)
                                {
                                    var dateText = match.Groups[1].Value;
                                    var timeText = match.Groups[2].Value;

                                    _logger.Debug($"Fecha extraída: '{dateText}', Hora extraída: '{timeText}'");

                                    DateTime reportTime = DateTime.Now;

                                    if (dateText.Equals("Hoy", StringComparison.OrdinalIgnoreCase))
                                    {
                                        reportTime = DateTime.Today.Add(ParseTimeString(timeText));
                                    }
                                    else if (dateText.Equals("Ayer", StringComparison.OrdinalIgnoreCase))
                                    {
                                        reportTime = DateTime.Today.AddDays(-1).Add(ParseTimeString(timeText));
                                    }
                                    else
                                    {
                                        // Intentar parsear una fecha específica
                                        if (DateTime.TryParseExact(
                                            $"{dateText} {timeText}",
                                            new[] { "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss tt" },
                                            CultureInfo.InvariantCulture,
                                            DateTimeStyles.None,
                                            out DateTime parsedDate))
                                        {
                                            reportTime = parsedDate;
                                        }
                                    }

                                    locationInfo.Timestamp = reportTime;
                                    _logger.Debug($"Timestamp extraído del tooltip: {reportTime}");
                                }
                                else
                                {
                                    _logger.Warning($"El formato del texto del tooltip no coincide con el patrón esperado: {tooltipText}");
                                }
                            }
                            else
                            {
                                _logger.Warning("El tooltip está vacío después del hover");
                            }
                        }
                        else
                        {
                            _logger.Warning("No se pudo encontrar el elemento del tooltip después del hover");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error al extraer timestamp mediante hover: {ex.Message}");
                }

                // 3. Método alternativo: extraer directamente del contenido interno del span
                try
                {
                    var lastReportValueSpan = popup.FindElement(By.Id("iw-last-report-value"));
                    if (lastReportValueSpan != null)
                    {
                        // Intentar obtener el contenido mediante innerHTML o getProperty
                        var innerContent = ((IJavaScriptExecutor)_driver).ExecuteScript(
                            "return arguments[0].innerHTML || arguments[0].textContent;",
                            lastReportValueSpan
                        )?.ToString();

                        _logger.Debug($"Contenido interno del span de reporte: {innerContent}");

                        if (!string.IsNullOrEmpty(innerContent))
                        {
                            // Aplicar el mismo procesamiento de fecha/hora que antes
                            var dateTimePattern = @"(Hoy|Ayer|\d{2}/\d{2}/\d{4})\s*,\s*(\d{2}:\d{2}:\d{2}\s*(?:AM|PM|a\.m\.|p\.m\.)?)";
                            var match = Regex.Match(innerContent, dateTimePattern);

                            if (match.Success)
                            {
                                var dateText = match.Groups[1].Value;
                                var timeText = match.Groups[2].Value;

                                _logger.Debug($"Fecha extraída (método alternativo): '{dateText}', Hora: '{timeText}'");

                                // Mismo procesamiento que antes...
                                DateTime reportTime = DateTime.Now;

                                if (dateText.Equals("Hoy", StringComparison.OrdinalIgnoreCase))
                                {
                                    reportTime = DateTime.Today.Add(ParseTimeString(timeText));
                                }
                                else if (dateText.Equals("Ayer", StringComparison.OrdinalIgnoreCase))
                                {
                                    reportTime = DateTime.Today.AddDays(-1).Add(ParseTimeString(timeText));
                                }
                                else
                                {
                                    // Intentar parsear una fecha específica
                                    if (DateTime.TryParseExact(
                                        $"{dateText} {timeText}",
                                        new[] { "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss tt" },
                                        CultureInfo.InvariantCulture,
                                        DateTimeStyles.None,
                                        out DateTime parsedDate))
                                    {
                                        reportTime = parsedDate;
                                    }
                                }

                                locationInfo.Timestamp = reportTime;
                                _logger.Debug($"Timestamp extraído (método alternativo): {reportTime}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error en método alternativo de extracción de timestamp: {ex.Message}");
                }

                _logger.Info("Información del vehículo extraída exitosamente", true);
                return locationInfo;
            }
            catch (Exception ex)
            {
                _logger.Error("Error extrayendo información del vehículo", ex);
                throw;
            }
        }

        // Método auxiliar para convertir texto de sentido a ángulo
        private decimal ConvertHeadingToAngle(string heading)
        {
            heading = heading.ToLower().Trim();

            if (heading.Contains("norte"))
                return 0;
            else if (heading.Contains("noreste") || heading.Contains("nor-este") || heading.Contains("nor-oriente"))
                return 45;
            else if (heading.Contains("este") || heading.Contains("oriente"))
                return 90;
            else if (heading.Contains("sureste") || heading.Contains("sur-este") || heading.Contains("sur-oriente"))
                return 135;
            else if (heading.Contains("sur"))
                return 180;
            else if (heading.Contains("suroeste") || heading.Contains("sur-oeste") || heading.Contains("sur-occidente"))
                return 225;
            else if (heading.Contains("oeste") || heading.Contains("occidente"))
                return 270;
            else if (heading.Contains("noroeste") || heading.Contains("nor-oeste") || heading.Contains("nor-occidente"))
                return 315;

            return 0; // Valor predeterminado
        }

        // Método auxiliar para parsear strings de tiempo (HH:MM:SS AM/PM)
        private TimeSpan ParseTimeString(string timeText)
        {
            try
            {
                // Limpiar el texto de tiempo
                timeText = timeText.Trim();

                // Patrones comunes de formato de hora
                string[] formats = {
            "hh:mm:ss tt",
            "h:mm:ss tt",
            "HH:mm:ss",
            "H:mm:ss",
            "hh:mm tt",
            "h:mm tt"
        };

                if (DateTime.TryParseExact(timeText, formats, CultureInfo.InvariantCulture,
                                          DateTimeStyles.None, out DateTime parsedTime))
                {
                    return parsedTime.TimeOfDay;
                }

                // Si falla el parsing estándar, intentar manualmente
                var components = Regex.Match(timeText, @"(\d{1,2}):(\d{2})(?::(\d{2}))?\s*(AM|PM|a\.m\.|p\.m\.)?");

                if (components.Success)
                {
                    int hours = int.Parse(components.Groups[1].Value);
                    int minutes = int.Parse(components.Groups[2].Value);
                    int seconds = components.Groups[3].Success ? int.Parse(components.Groups[3].Value) : 0;

                    string ampm = components.Groups[4].Value.ToLower();

                    // Ajustar horas si es PM
                    if (ampm.Contains("p") && hours < 12)
                        hours += 12;
                    else if (ampm.Contains("a") && hours == 12)
                        hours = 0;

                    return new TimeSpan(hours, minutes, seconds);
                }

                return new TimeSpan(0, 0, 0); // Valor predeterminado
            }
            catch
            {
                return new TimeSpan(0, 0, 0);
            }
        }

        private async Task NavigateToVehiclesSection(DynamicWaitHelper dynamicWait)
        {
            try
            {
                _logger.Debug("Iniciando navegación a sección de vehículos/mapa");

                // Verificar si ya estamos en la sección de mapa mediante múltiples indicadores
                var alreadyInMapSection = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        // Verificar múltiples elementos que indican que estamos en la sección del mapa
                        return d.FindElements(By.CssSelector(".leaflet-container")).Any(e => e.Displayed) ||
                               d.FindElements(By.CssSelector(".vehicle-list")).Any(e => e.Displayed) ||
                               d.FindElements(By.CssSelector(".item-veh-plate")).Any(e => e.Displayed) ||
                               d.FindElements(By.CssSelector(".map-container")).Any(e => e.Displayed) ||
                               d.Url.Contains("/main");
                    }
                    catch
                    {
                        return false;
                    }
                }, "map_section_check", TimeSpan.FromSeconds(2));

                if (alreadyInMapSection)
                {
                    _logger.Debug("Ya estamos en la sección de mapa, no es necesario navegar");
                    return;
                }

                // Si no estamos en la sección correcta, intentar diferentes métodos para navegar

                // 1. Intentar hacer clic en el botón de mapa/ubicación
                var mapButtonSelectors = new[] {
            "a[href*='/main']",
            "a[href*='/map']",
            "a[href*='/vehicles']",
            "button[data-target='map']",
            "//a[contains(text(), 'Ubicación')]",
            "//a[contains(text(), 'Mapa')]",
            "//button[contains(text(), 'Mapa')]"
        };

                bool clickSuccess = false;
                foreach (var selector in mapButtonSelectors)
                {
                    try
                    {
                        var isXPath = selector.StartsWith("//");
                        var by = isXPath ? By.XPath(selector) : By.CssSelector(selector);

                        var (mapButton, mapButtonError) = await dynamicWait.WaitForElementAsync(
                            by,
                            $"map_button_{selector.GetHashCode()}",
                            ensureClickable: true
                        );

                        if (mapButton != null)
                        {
                            _logger.Debug($"Botón de mapa/vehículos encontrado con selector '{selector}', intentando hacer clic...");

                            // Intentar hacer clic mediante JavaScript para mayor fiabilidad
                            try
                            {
                                ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", mapButton);
                                await Task.Delay(2000);
                                clickSuccess = true;
                                break;
                            }
                            catch (Exception jsEx)
                            {
                                _logger.Debug($"Error en clic JS: {jsEx.Message}, intentando clic normal...");

                                // Si falla JavaScript, intentar clic normal
                                mapButton.Click();
                                await Task.Delay(2000);
                                clickSuccess = true;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error con selector '{selector}': {ex.Message}");
                        // Continuar con el siguiente selector
                    }
                }

                // 2. Si no funcionó el clic, intentar navegación directa a URL
                if (!clickSuccess)
                {
                    _logger.Debug("No se pudo hacer clic en botón de mapa, intentando navegación directa a /main");

                    try
                    {
                        _driver.Navigate().GoToUrl("https://portal.satrack.com/main");
                        await Task.Delay(3000);
                    }
                    catch (Exception urlEx)
                    {
                        _logger.Warning($"Error en navegación directa a /main: {urlEx.Message}", true);
                    }
                }

                // 3. Verificar si ahora estamos en la sección del mapa (con espera más larga)
                var mapLoaded = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        // Verificar múltiples indicadores de que estamos en la sección de mapa
                        var leafletExists = d.FindElements(By.CssSelector(".leaflet-container")).Any(e => e.Displayed);
                        var mapContainerExists = d.FindElements(By.CssSelector(".map-container, [id*='map']")).Any(e => e.Displayed);
                        var vehicleListExists = d.FindElements(By.CssSelector(".vehicle-list, .item-veh-plate")).Any(e => e.Displayed);
                        var mainUrl = d.Url.Contains("/main");

                        return leafletExists || mapContainerExists || vehicleListExists || mainUrl;
                    }
                    catch
                    {
                        return false;
                    }
                }, "map_loaded", TimeSpan.FromSeconds(8));

                if (!mapLoaded)
                {
                    // 4. Último intento: verificar mediante JavaScript elementos clave y la estructura DOM
                    bool jsCheck = false;
                    try
                    {
                        jsCheck = (bool)((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    // Verificar elementos clave que indican que estamos en la sección de mapa
                    return (
                        document.querySelector('.leaflet-container') != null ||
                        document.querySelector('.map-container') != null ||
                        document.querySelector('.vehicle-list') != null ||
                        document.querySelector('.item-veh-plate') != null ||
                        document.querySelectorAll('div[class*=""map""]').length > 0 ||
                        document.querySelectorAll('div[class*=""vehicle""]').length > 0 ||
                        window.location.href.includes('/main')
                    );
                ");
                    }
                    catch { /* Ignorar errores en la verificación JavaScript */ }

                    if (!jsCheck)
                    {
                        _logger.Warning("La sección del mapa no pudo ser detectada después de múltiples intentos", true);

                        // 5. Último recurso: refrescar la página y esperar
                        try
                        {
                            _logger.Debug("Intentando refrescar la página como último recurso...");
                            _driver.Navigate().Refresh();
                            await Task.Delay(5000);

                            // Verificar una última vez
                            var finalCheck = await dynamicWait.WaitForConditionAsync(d =>
                                d.FindElements(By.CssSelector(".leaflet-container, .map-container, .vehicle-list, .item-veh-plate")).Any(e => e.Displayed) ||
                                d.Url.Contains("/main"),
                                "final_map_check",
                                TimeSpan.FromSeconds(10)
                            );

                            if (!finalCheck)
                            {
                                throw new InvalidOperationException("La sección del mapa no cargó correctamente después de múltiples intentos");
                            }
                        }
                        catch (Exception refreshEx)
                        {
                            throw new InvalidOperationException("La sección del mapa no cargó correctamente", refreshEx);
                        }
                    }
                }

                // Verificar que el mapa esté completamente cargado esperando los marcadores
                var markersLoaded = await dynamicWait.WaitForConditionAsync(d =>
                {
                    try
                    {
                        return d.FindElements(By.CssSelector(".leaflet-marker-icon, .vehicle-marker, .marker-cluster, [class*='marker']")).Any(e => e.Displayed);
                    }
                    catch
                    {
                        return false;
                    }
                }, "map_markers_loaded", TimeSpan.FromSeconds(10));

                if (!markersLoaded)
                {
                    _logger.Warning("Los marcadores del mapa no cargaron completamente, pero continuando el proceso", true);
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

        private async Task<bool> WaitForVehicleVirtualScrollReady(DynamicWaitHelper dynamicWait, int timeoutSeconds = 20)
        {
            try
            {
                _logger.Debug("Esperando que el virtual scroll de vehículos esté listo (optimizado)...");

                var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
                var lastLogTime = DateTime.MinValue;
                bool quickCheckPassed = false;

                while (DateTime.Now < deadline)
                {
                    // Log de progreso cada 3 segundos (reducido de 5)
                    if (DateTime.Now.Subtract(lastLogTime).TotalSeconds >= 3)
                    {
                        var remaining = deadline.Subtract(DateTime.Now).TotalSeconds;
                        _logger.Debug($"Esperando virtual scroll... {remaining:F0}s restantes");
                        lastLogTime = DateTime.Now;
                    }

                    try
                    {
                        // VERIFICACIÓN RÁPIDA OPTIMIZADA - prioriza detectar placas visibles
                        var quickCheck = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    try {
                        // 1. Verificación rápida: ¿hay placas visibles inmediatamente?
                        const visiblePlates = Array.from(document.querySelectorAll(
                            'span.notranslate, [class*=""item-veh""], [id*=""alias""], [class*=""vehicle""]'
                        )).filter(el => {
                            if (el.offsetHeight === 0 || el.offsetWidth === 0) return false;
                            const text = el.textContent || '';
                            return text.match(/[A-Z]{2,3}\d{3,4}/) || text.match(/[A-Z]{3}\d{3}/) || text.match(/GVS\d+/);
                        });

                        if (visiblePlates.length > 0) {
                            return { 
                                ready: true, 
                                vehicleCount: visiblePlates.length,
                                reason: 'Placas visibles detectadas - verificación rápida',
                                quickCheck: true
                            };
                        }

                        // 2. Solo si no hay placas visibles, verificar el contenedor
                        const scrollContainer = document.querySelector(
                            '[id*=""cdk_scroll_location_vehicles_list""], cdk-virtual-scroll-viewport, .cdk-virtual-scroll-viewport'
                        );
                        
                        if (!scrollContainer || scrollContainer.offsetHeight === 0) {
                            return { ready: false, reason: 'Contenedor no encontrado o no visible' };
                        }

                        // 3. Verificación mínima de estabilidad solo si no pasó verificación rápida
                        const loadingIndicators = document.querySelectorAll(
                            '.main-loading, .app-loading, .vehicle-list-loading'
                        );
                        const criticalLoading = Array.from(loadingIndicators).some(el => 
                            el.offsetHeight > 0 && el.offsetWidth > 0
                        );
                        
                        if (criticalLoading) {
                            return { ready: false, reason: 'Indicadores de carga críticos activos' };
                        }

                        return { ready: false, reason: 'Sin placas visibles aún' };
                        
                    } catch (error) {
                        return { ready: false, reason: 'Error JavaScript: ' + error.message };
                    }
                ");

                        if (quickCheck != null)
                        {
                            var status = quickCheck as Dictionary<string, object>;
                            if (status != null && status.ContainsKey("ready"))
                            {
                                var isReady = Convert.ToBoolean(status["ready"]);
                                var reason = status.ContainsKey("reason") ? status["reason"].ToString() : "Unknown";
                                var isQuickCheck = status.ContainsKey("quickCheck") && Convert.ToBoolean(status["quickCheck"]);

                                if (isReady)
                                {
                                    var vehicleCount = status.ContainsKey("vehicleCount") ? status["vehicleCount"].ToString() : "unknown";

                                    if (isQuickCheck)
                                    {
                                        _logger.Info($"✅ Virtual scroll listo RÁPIDAMENTE con {vehicleCount} vehículos detectados", true);
                                        return true;
                                    }
                                    else
                                    {
                                        // Marcar que pasó verificación rápida para siguiente iteración
                                        quickCheckPassed = true;
                                        _logger.Info($"Virtual scroll listo con {vehicleCount} vehículos detectados", true);
                                        return true;
                                    }
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

                    // Intervalo de verificación más frecuente para respuesta más rápida
                    await Task.Delay(quickCheckPassed ? 50 : 150);
                }

                _logger.Warning("Timeout esperando que el virtual scroll esté listo", true);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error("Error en WaitForVehicleVirtualScrollReady optimizado", ex);
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

        private async Task<bool> HandlePageNotFoundError()
        {
            try
            {
                // Verificar si estamos en la página de error por URL o contenido
                var isErrorPage = _driver.Url.Contains("not-found") ||
                                 _driver.PageSource.Contains("Página no encontrada") ||
                                 _driver.PageSource.Contains("No pudimos encontrar la página");

                if (isErrorPage)
                {
                    _logger.Warning("Detectada página 'no encontrada' después del login. Intentando ir al inicio...", true);

                    // Buscar el botón "Ir al inicio" con diferentes selectores
                    var homeButtonSelectors = new[]
                    {
                "//a[contains(text(), 'Ir al inicio')]",
                "//a[contains(text(), 'inicio')]",
                "//a[contains(@class, 'home')]",
                "//button[contains(text(), 'Ir al inicio')]",
                "//button[contains(text(), 'inicio')]",
                "//a[contains(@href, 'main')]"
            };

                    foreach (var selector in homeButtonSelectors)
                    {
                        try
                        {
                            var homeButton = _driver.FindElements(By.XPath(selector)).FirstOrDefault();
                            if (homeButton != null && homeButton.Displayed)
                            {
                                _logger.Debug($"Botón 'Ir al inicio' encontrado con selector '{selector}', intentando hacer clic...");

                                // Intentar clic con JavaScript primero para mayor confiabilidad
                                try
                                {
                                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", homeButton);
                                    _logger.Debug("Clic en 'Ir al inicio' ejecutado con JavaScript");
                                }
                                catch
                                {
                                    // Si falla el JavaScript, intentar clic normal
                                    homeButton.Click();
                                    _logger.Debug("Clic en 'Ir al inicio' ejecutado normalmente");
                                }

                                // Esperar a que la página principal cargue
                                await Task.Delay(3000);

                                // Verificar si ya no estamos en la página de error
                                if (!_driver.Url.Contains("not-found") &&
                                    !_driver.PageSource.Contains("Página no encontrada") &&
                                    !_driver.PageSource.Contains("No pudimos encontrar la página"))
                                {
                                    _logger.Info("Navegación exitosa desde página de error al inicio", true);
                                    return true;
                                }
                            }
                        }
                        catch (Exception innerEx)
                        {
                            _logger.Debug($"Error al intentar con selector '{selector}': {innerEx.Message}");
                            // Continuar con el siguiente selector
                        }
                    }

                    _logger.Warning("No se pudo encontrar o usar el botón 'Ir al inicio' en la página de error", true);
                    return false;
                }
                return true; // No estamos en la página de error
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error al manejar la página 'no encontrada': {ex.Message}", true);
                return false;
            }
        }

        private async Task<bool> WaitForVehicleListReady(
        DynamicWaitHelper dynamicWait,
        TimeSpan? maxWait = null)
        {
            maxWait ??= TimeSpan.FromSeconds(20);
            var deadline = DateTime.Now + maxWait.Value;

            while (DateTime.Now < deadline)
            {
                if (await WaitForVehicleListContainer(dynamicWait) != null)
                    return true;

                await Task.Delay(500);
            }

            return false;
        }

        private async Task<IWebElement?> WaitForVehicleListContainer(
        DynamicWaitHelper dynamicWait)
        {
            _logger.Debug("Buscando contenedor de la lista de vehículos…");

            // ➜ nuevos selectores 🔽  (los anteriores se mantienen)
            var selectors = new[]
            {
                // selector exacto al id que muestra tu captura
                "#cdk_scroll_location_vehicles_list",
                // cualquier virtual-scroll que contenga “vehicles_list” en el id
                "cdk-virtual-scroll-viewport[id*='vehicles_list']",
                // cualquier virtual-scroll visible
                "cdk-virtual-scroll-viewport.cdk-virtual-scroll-viewport",
                // selectores que ya tenías
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
                var (container, _) = await dynamicWait.WaitForElementAsync(
                    By.CssSelector(sel),
                    $"vehicle_list_container_{sel.GetHashCode()}",
                    ensureClickable: false);

                if (container is not null && container.Displayed)
                {
                    _logger.Debug($"Contenedor encontrado con selector: {sel}");
                    return container;
                }
            }

            // ➊ Búsqueda JS (último recurso)
            var jsContainer = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
        return document.querySelector('cdk-virtual-scroll-viewport[id*=\""vehicles_list\""], cdk-virtual-scroll-viewport.cdk-virtual-scroll-viewport');");
            if (jsContainer is IWebElement we && we.Displayed)
            {
                _logger.Debug("Contenedor encontrado mediante JavaScript");
                return we;
            }

            _logger.Warning("No se localizó el contenedor de la lista de vehículos.");
            return null;
        }

        private async Task<IWebElement?> FindPlateElementInContainer(IWebElement container, string patent)
        {
            try
            {
                _logger.Debug($"Buscando elemento específico de placa {patent} dentro del contenedor");

                // Buscar directamente elementos span con el texto exacto de la placa
                var plateElements = container.FindElements(By.XPath($".//span[normalize-space(text())='{patent}']"));
                if (plateElements.Any(e => e.Displayed))
                {
                    var visiblePlate = plateElements.First(e => e.Displayed);
                    _logger.Debug($"Encontrado elemento de placa visible dentro del contenedor");
                    return visiblePlate;
                }

                // Buscar en cualquier elemento que contenga el texto exacto
                var anyElements = container.FindElements(By.XPath($".//*[normalize-space(text())='{patent}']"));
                if (anyElements.Any(e => e.Displayed))
                {
                    var visibleElement = anyElements.First(e => e.Displayed);
                    _logger.Debug($"Encontrado elemento con texto exacto dentro del contenedor");
                    return visibleElement;
                }

                // Si no se encuentra elemento exacto, usar JavaScript para buscar dentro del contenedor
                var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            function findPlateInContainer(container, plate) {
                // Buscar elementos con el texto exacto
                var nodes = [];
                var walk = document.createTreeWalker(
                    container,
                    NodeFilter.SHOW_TEXT,
                    { acceptNode: function(node) { return NodeFilter.FILTER_ACCEPT; } },
                    false
                );
                
                while(node = walk.nextNode()) {
                    if (node.nodeValue.trim() === plate) {
                        return node.parentNode;
                    }
                }
                
                // Si no hay coincidencia exacta, buscar el elemento más específico
                var allElements = container.querySelectorAll('*');
                for (var i = 0; i < allElements.length; i++) {
                    var el = allElements[i];
                    if (el.textContent.includes(plate) && 
                        el.offsetWidth > 0 && 
                        el.offsetHeight > 0) {
                        
                        // Preferir elementos más específicos
                        if (el.tagName === 'SPAN' || 
                            el.tagName === 'DIV' && el.className.includes('plate')) {
                            return el;
                        }
                    }
                }
                
                return null;
            }
            
            return findPlateInContainer(arguments[0], arguments[1]);
        ", container, patent);

                if (jsResult != null)
                {
                    _logger.Debug("Elemento de placa encontrado con JavaScript dentro del contenedor");
                    return (IWebElement)jsResult;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error buscando placa en contenedor: {ex.Message}");
                return null;
            }
        }

        private async Task<IWebElement?> FindVehiclePlateSpan(string patent)
        {
            try
            {
                _logger.Debug($"Buscando elemento span específico para placa {patent}");

                // Selectores específicos para el span que contiene el texto de la placa
                var spanSelectors = new[] {
            $"//span[@class='notranslate' and text()='{patent}']",
            $"//div[contains(@id, '{patent}')]//span",
            $"//div[contains(@class, 'item-veh-plate')]//span[text()='{patent}']"
        };

                foreach (var selector in spanSelectors)
                {
                    try
                    {
                        var spans = _driver.FindElements(By.XPath(selector));
                        var visibleSpan = spans.FirstOrDefault(s => s.Displayed);

                        if (visibleSpan != null)
                        {
                            _logger.Info($"Elemento span de placa {patent} encontrado con selector: {selector}", true);
                            return visibleSpan;
                        }
                    }
                    catch { /* Continuar con el siguiente selector */ }
                }

                // Búsqueda mediante JavaScript
                var jsSpan = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            // Buscar específicamente un span con el texto exacto de la placa
            const spans = Array.from(document.querySelectorAll('span'));
            for (const span of spans) {
                if (span.textContent.trim() === arguments[0] && 
                    span.offsetWidth > 0 && 
                    span.offsetHeight > 0) {
                    return span;
                }
            }
            return null;
        ", patent);

                if (jsSpan != null)
                {
                    _logger.Info($"Elemento span de placa {patent} encontrado mediante JavaScript", true);
                    return (IWebElement)jsSpan;
                }

                _logger.Debug($"No se encontró elemento span específico para placa {patent}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error en FindVehiclePlateSpan: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> IsSpecificPlateReady(string patent, int timeoutSeconds = 5)
        {
            try
            {
                _logger.Debug($"Verificación rápida si placa {patent} está lista...");

                // Aumentar tiempo por defecto a 5 segundos para dar más oportunidad de encontrar la placa
                var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
                var lastCheckTime = DateTime.MinValue;
                var checkInterval = 50; // Intervalo de verificación más rápido (ms)

                while (DateTime.Now < deadline)
                {
                    try
                    {
                        // Optimización: Verificar estado de carga antes de buscar la placa
                        if ((DateTime.Now - lastCheckTime).TotalMilliseconds > 500)
                        {
                            var loadingState = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                        return {
                            anyLoading: !!document.querySelector('.loading, .spinner, .wait, [class*=""loading""]'),
                            bodyHidden: document.body.style.display === 'none' || document.body.style.visibility === 'hidden',
                            ajaxActive: typeof $ !== 'undefined' && $.active > 0
                        };
                    ");

                            if (loadingState is Dictionary<string, object> state)
                            {
                                bool anyLoading = Convert.ToBoolean(state["anyLoading"]);
                                bool bodyHidden = Convert.ToBoolean(state["bodyHidden"]);

                                // Si todavía está cargando, esperamos un poco más antes de buscar
                                if (anyLoading || bodyHidden)
                                {
                                    await Task.Delay(100);
                                    lastCheckTime = DateTime.Now;
                                    continue;
                                }
                            }
                        }

                        // Script de búsqueda mejorado con más selectores específicos y optimizaciones
                        var plateReady = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    try {
                        const patent = arguments[0];
                        const results = { found: false, method: '', element: null };

                        // OPTIMIZACIÓN 1: Verificar si la tabla ya está lista
                        const tableReady = (
                            document.querySelector('[class*=""vehicle-list""], [class*=""veh-list""], [id*=""cdk_scroll""]') !== null &&
                            !document.querySelector('.loading, .spinner, .wait, [class*=""loading""]')
                        );
                        
                        if (!tableReady) {
                            return { 
                                found: false, 
                                reason: 'Tabla no lista',
                                tableStatus: {
                                    tableElement: !!document.querySelector('[class*=""vehicle-list""]'),
                                    loading: !!document.querySelector('.loading, .spinner')
                                }
                            };
                        }

                        // OPTIMIZACIÓN 2: Búsqueda prioritaria por ID y selectores específicos primero
                        const prioritySelectors = [
                            // Selectores exactos para la placa
                            `span.notranslate[textContent=""${patent}""]`,
                            `div[id=""${patent}_alias""]`,
                            `div[id=""${patent}alias""]`,
                            `div[id*=""${patent}""]`,
                            `[class*=""plate""][id*=""${patent}""]`,
                            // Selectores para elementos tr/td que contienen la placa
                            `td:nth-child(3)[textContent=""${patent}""]`,
                            `tr.ng-star-inserted td:nth-child(3)`,
                            // Selector más genérico pero directo para elementos visibles
                            `div[class*=""item-veh""]`
                        ];
                        
                        // Iteramos por prioridad para encontrar la placa rápidamente
                        for (const selector of prioritySelectors) {
                            let elements;
                            if (selector === 'tr.ng-star-inserted td:nth-child(3)') {
                                // Búsqueda específica para tablas Angular
                                elements = Array.from(document.querySelectorAll(selector))
                                    .filter(el => el.textContent && el.textContent.trim() === patent);
                            } else {
                                elements = Array.from(document.querySelectorAll(selector));
                            }
                            
                            // Verificar elementos encontrados
                            for (const el of elements) {
                                if (el.offsetWidth > 0 && el.offsetHeight > 0) {
                                    // Verificación adicional para texto exacto
                                    if (el.textContent && el.textContent.trim() === patent) {
                                        results.found = true;
                                        results.method = selector + ':exact-text';
                                        results.element = el;
                                        return results;
                                    }
                                    // Verificación por ID
                                    else if (el.id && el.id.includes(patent)) {
                                        results.found = true;
                                        results.method = selector + ':id-match';
                                        results.element = el;
                                        return results;
                                    }
                                    // Para elementos genéricos, verificar si tienen un hijo con la placa
                                    else if (selector === 'div[class*=""item-veh""]') {
                                        if (el.textContent && el.textContent.includes(patent)) {
                                            results.found = true;
                                            results.method = selector + ':contains-text';
                                            results.element = el;
                                            return results;
                                        }
                                    }
                                }
                            }
                        }
                        
                        // OPTIMIZACIÓN 3: Búsqueda a través de elementos de tabla
                        const rows = document.querySelectorAll('tr');
                        for (const row of rows) {
                            if (row.textContent && row.textContent.includes(patent)) {
                                const cells = row.querySelectorAll('td');
                                for (const cell of cells) {
                                    if (cell.textContent && cell.textContent.trim() === patent) {
                                        results.found = true;
                                        results.method = 'table-scan:exact-cell';
                                        results.element = cell;
                                        return results;
                                    }
                                }
                                
                                // Si encontramos la fila pero no la celda exacta, tomamos la fila
                                results.found = true;
                                results.method = 'table-scan:row-contains';
                                results.element = row;
                                return results;
                            }
                        }
                        
                        // OPTIMIZACIÓN 4: Verificar si la placa aparece en cualquier lista virtual
                        const virtualItems = document.querySelectorAll('[class*=""virtual-scroll""] [class*=""item""]');
                        for (const item of virtualItems) {
                            if (item.textContent && item.textContent.includes(patent)) {
                                results.found = true;
                                results.method = 'virtual-scroll-item';
                                results.element = item;
                                return results;
                            }
                        }
                        
                        // OPTIMIZACIÓN 5: Realizar búsqueda de nodos de texto si todo lo demás falla
                        // Solo buscar en elementos visibles para mayor rendimiento
                        const visibleElements = Array.from(document.body.querySelectorAll('*'))
                            .filter(el => {
                                const rect = el.getBoundingClientRect();
                                return rect.width > 0 && rect.height > 0 && 
                                       rect.top < window.innerHeight && 
                                       rect.left < window.innerWidth;
                            });
                            
                        for (const el of visibleElements) {
                            if (el.childNodes && el.childNodes.length) {
                                for (const node of el.childNodes) {
                                    if (node.nodeType === 3 && node.textContent && node.textContent.trim() === patent) {
                                        results.found = true;
                                        results.method = 'text-node-search';
                                        results.element = el;
                                        return results;
                                    }
                                }
                            }
                        }

                        // Recopilar información sobre la tabla para diagnóstico
                        const diagnosticInfo = {
                            visibleRows: document.querySelectorAll('tr:not([style*=""display: none""])').length,
                            totalRows: document.querySelectorAll('tr').length,
                            virtualScrollPresent: !!document.querySelector('[class*=""virtual-scroll""]'),
                            anyPlateElements: !!document.querySelector('[class*=""plate""]'),
                            bodyScrollHeight: document.body.scrollHeight,
                            bodyClientHeight: document.body.clientHeight
                        };
                        
                        return { 
                            found: false, 
                            reason: 'Placa no encontrada después de búsqueda exhaustiva',
                            diagnosticInfo: diagnosticInfo
                        };
                    } catch (error) {
                        return { 
                            found: false, 
                            reason: 'Error: ' + error.message,
                            stack: error.stack
                        };
                    }
                ", patent);

                        if (plateReady != null)
                        {
                            var result = plateReady as Dictionary<string, object>;
                            if (result != null && result.ContainsKey("found"))
                            {
                                var found = Convert.ToBoolean(result["found"]);
                                if (found)
                                {
                                    var method = result.ContainsKey("method") ? result["method"].ToString() : "unknown";
                                    _logger.Info($"✅ Placa {patent} encontrada y lista (método: {method})", true);
                                    return true;
                                }
                                else if (result.ContainsKey("reason"))
                                {
                                    var reason = result["reason"].ToString();
                                    _logger.Debug($"Placa no encontrada: {reason}");

                                    // Si tenemos diagnóstico, logeamos para ayudar en depuración
                                    if (result.ContainsKey("diagnosticInfo"))
                                    {
                                        var info = result["diagnosticInfo"] as Dictionary<string, object>;
                                        if (info != null)
                                        {
                                            _logger.Debug($"Diagnóstico: Filas visibles={info["visibleRows"]}, " +
                                                         $"Total filas={info["totalRows"]}, " +
                                                         $"Scroll virtual={info["virtualScrollPresent"]}");
                                        }
                                    }

                                    // Si tenemos info de tabla que aún está cargando, esperamos menos tiempo
                                    if (result.ContainsKey("tableStatus"))
                                    {
                                        var tableStatus = result["tableStatus"] as Dictionary<string, object>;
                                        if (tableStatus != null && Convert.ToBoolean(tableStatus["loading"]))
                                        {
                                            await Task.Delay(50); // Espera más corta si aún está cargando
                                            continue;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error verificando placa específica: {ex.Message}");
                    }

                    // Intervalo de verificación progresivo (aumenta con el tiempo)
                    int currentInterval = checkInterval;
                    if ((DateTime.Now - deadline.AddSeconds(-timeoutSeconds)).TotalSeconds > 2)
                    {
                        currentInterval = 200; // Después de 2 segundos, intervalo más largo
                    }
                    await Task.Delay(currentInterval);
                }

                _logger.Debug($"Placa {patent} no encontrada en verificación rápida optimizada");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error en IsSpecificPlateReady: {ex.Message}");
                return false;
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
            int windowsCountBefore = _driver.WindowHandles.Count;

            // Si tenemos un elemento cacheado, primero intentamos hacer clic en el elemento span hijo si existe
            if (cachedElement != null)
            {
                try
                {
                    // Buscar el span hijo que contiene el texto exacto de la placa
                    var spanElements = cachedElement.FindElements(By.XPath(".//span[@class='notranslate']"));
                    if (spanElements.Any(s => s.Displayed))
                    {
                        var spanToClick = spanElements.First(s => s.Displayed);
                        _logger.Debug("Encontrado elemento span hijo para hacer clic directamente");

                        try
                        {
                            // Intentar clic simple primero
                            spanToClick.Click();
                            await Task.Delay(500);

                            // Verificar si se abrió una nueva pestaña
                            if (_driver.WindowHandles.Count > windowsCountBefore)
                            {
                                _logger.Warning("Se abrió una nueva pestaña al hacer clic en span. Cerrándola...", true);
                                _driver.SwitchTo().Window(_driver.WindowHandles.Last());
                                _driver.Close();
                                _driver.SwitchTo().Window(_driver.WindowHandles.First());
                            }
                            else
                            {
                                _logger.Info("Clic exitoso en el span de la placa", true);
                                return true;
                            }
                        }
                        catch (Exception spanClickEx)
                        {
                            _logger.Debug($"Error en clic directo en span: {spanClickEx.Message}");
                            // Continuamos con otros métodos
                        }
                    }
                }
                catch (Exception findSpanEx)
                {
                    _logger.Debug($"Error al buscar span hijo: {findSpanEx.Message}");
                }
            }

            // Si el clic en span falló o no había span, intenta hacer clic en el elemento directamente
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

                    // Scroll al centro del elemento para asegurar visibilidad
                    ((IJavaScriptExecutor)_driver)
                        .ExecuteScript(
                            "arguments[0].scrollIntoView({block:'center',inline:'center'});",
                            element);
                    await Task.Delay(300);

                    // Método 1: Clic con prevención de apertura de nuevas pestañas
                    try
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    // Prevenir apertura de nuevas pestañas
                    var originalWindowOpen = window.open;
                    window.open = function() { return null; };
                    
                    // Prevenir navegación
                    var originalHref = arguments[0].getAttribute('href');
                    if (originalHref) {
                        arguments[0].removeAttribute('href');
                    }
                    
                    // Hacer clic
                    arguments[0].click();
                    
                    // Restaurar funciones originales
                    window.open = originalWindowOpen;
                    if (originalHref) {
                        arguments[0].setAttribute('href', originalHref);
                    }
                ", element);

                        await Task.Delay(500);

                        // Verificar si se abrió una nueva pestaña a pesar de la prevención
                        if (_driver.WindowHandles.Count > windowsCountBefore)
                        {
                            _logger.Warning("Se abrió una nueva pestaña a pesar de la prevención. Cerrándola...", true);
                            _driver.SwitchTo().Window(_driver.WindowHandles.Last());
                            _driver.Close();
                            _driver.SwitchTo().Window(_driver.WindowHandles.First());
                        }
                        else
                        {
                            _logger.Info("Clic exitoso con método de prevención", true);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error en método de prevención: {ex.Message}");
                    }

                    // Método 2: Crear y disparar un evento de clic personalizado
                    try
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    // Crear un evento de clic personalizado
                    var clickEvent = new MouseEvent('click', {
                        bubbles: true,
                        cancelable: true,
                        view: window
                    });
                    
                    // Prevenir comportamiento predeterminado que pudiera abrir nuevas pestañas
                    var preventDefaultHandler = function(e) { 
                        e.preventDefault(); 
                        e.stopPropagation();
                    };
                    
                    document.addEventListener('click', preventDefaultHandler, { once: true });
                    
                    // Disparar el evento en el elemento
                    arguments[0].dispatchEvent(clickEvent);
                    
                    // Eliminar el handler para no afectar otros clics
                    document.removeEventListener('click', preventDefaultHandler);
                ", element);

                        await Task.Delay(500);

                        // Verificar si se abrió una nueva pestaña
                        if (_driver.WindowHandles.Count > windowsCountBefore)
                        {
                            _logger.Warning("Se abrió una nueva pestaña con evento personalizado. Cerrándola...", true);
                            _driver.SwitchTo().Window(_driver.WindowHandles.Last());
                            _driver.Close();
                            _driver.SwitchTo().Window(_driver.WindowHandles.First());
                        }
                        else
                        {
                            _logger.Info("Clic exitoso con evento personalizado", true);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error en evento personalizado: {ex.Message}");
                    }

                    // Método 3: Simulación de clic nativo con Actions
                    try
                    {
                        // Usar Actions para simular un clic más preciso
                        new Actions(_driver)
                            .MoveToElement(element)
                            .Click()
                            .Perform();

                        await Task.Delay(500);

                        // Verificar si se abrió una nueva pestaña
                        if (_driver.WindowHandles.Count > windowsCountBefore)
                        {
                            _logger.Warning("Se abrió una nueva pestaña con Actions. Cerrándola...", true);
                            _driver.SwitchTo().Window(_driver.WindowHandles.Last());
                            _driver.Close();
                            _driver.SwitchTo().Window(_driver.WindowHandles.First());
                        }
                        else
                        {
                            _logger.Info("Clic exitoso con Actions", true);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error en clic con Actions: {ex.Message}");
                    }

                    // Método 4: Clic directo en un elemento hijo que sea el texto (si existe)
                    try
                    {
                        // Buscar elementos de texto dentro del elemento
                        var textNodes = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    function getTextNodes(element) {
                        var textNodes = [];
                        var walk = document.createTreeWalker(
                            element, 
                            NodeFilter.SHOW_TEXT, 
                            null, 
                            false
                        );
                        
                        while(node = walk.nextNode()) {
                            if (node.nodeValue.trim()) {
                                textNodes.push(node.parentNode);
                            }
                        }
                        
                        return textNodes.filter(n => 
                            n.nodeType === 1 && 
                            n.offsetWidth > 0 && 
                            n.offsetHeight > 0
                        );
                    }
                    
                    return getTextNodes(arguments[0]);
                ", element);

                        if (textNodes != null && textNodes is IEnumerable<object> nodes && nodes.Any())
                        {
                            var textElement = (IWebElement)nodes.First();
                            _logger.Debug("Encontrado nodo de texto para clic directo");

                            textElement.Click();
                            await Task.Delay(500);

                            // Verificar si se abrió una nueva pestaña
                            if (_driver.WindowHandles.Count > windowsCountBefore)
                            {
                                _logger.Warning("Se abrió una nueva pestaña con clic en nodo de texto. Cerrándola...", true);
                                _driver.SwitchTo().Window(_driver.WindowHandles.Last());
                                _driver.Close();
                                _driver.SwitchTo().Window(_driver.WindowHandles.First());
                            }
                            else
                            {
                                _logger.Info("Clic exitoso en nodo de texto", true);
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error en clic en nodo de texto: {ex.Message}");
                    }

                    // Método 5: Cambiar dinámicamente la estructura DOM para hacer la placa seleccionable sin navegación
                    try
                    {
                        var jsResult = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    try {
                        // Guardar referencias a atributos que pueden causar navegación
                        const originalHref = arguments[0].getAttribute('href');
                        const originalOnClick = arguments[0].onclick;
                        const originalTarget = arguments[0].getAttribute('target');
                        
                        // Eliminar temporalmente estos atributos
                        arguments[0].removeAttribute('href');
                        arguments[0].removeAttribute('target');
                        arguments[0].onclick = null;
                        
                        // Buscar todos los elementos anidados que podrían tener enlaces
                        const allNestedElements = arguments[0].querySelectorAll('*');
                        for (const el of allNestedElements) {
                            el.removeAttribute('href');
                            el.removeAttribute('target');
                            el.onclick = null;
                        }
                        
                        // Hacer clic
                        arguments[0].click();
                        
                        // Restaurar atributos originales
                        if (originalHref) arguments[0].setAttribute('href', originalHref);
                        if (originalTarget) arguments[0].setAttribute('target', originalTarget);
                        arguments[0].onclick = originalOnClick;
                        
                        return true;
                    } catch (e) {
                        console.error('Error en modificación DOM:', e);
                        return false;
                    }
                ", element);

                        if (jsResult is bool success && success)
                        {
                            await Task.Delay(500);

                            // Verificar si se abrió una nueva pestaña
                            if (_driver.WindowHandles.Count > windowsCountBefore)
                            {
                                _logger.Warning("Se abrió una nueva pestaña con modificación DOM. Cerrándola...", true);
                                _driver.SwitchTo().Window(_driver.WindowHandles.Last());
                                _driver.Close();
                                _driver.SwitchTo().Window(_driver.WindowHandles.First());
                            }
                            else
                            {
                                _logger.Info("Clic exitoso con modificación DOM", true);
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error en modificación DOM: {ex.Message}");
                    }
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

            // Método final: Simulación de selección de placa sin clic real
            try
            {
                _logger.Debug("Intentando simulación de selección sin clic real...");

                // Obtener elemento si no está cacheado
                IWebElement element = cachedElement ?? _driver.FindElement(locator);

                // Intentar extraer el texto de la placa del elemento
                string plateText = "";
                try
                {
                    plateText = element.Text.Trim();
                    if (string.IsNullOrEmpty(plateText))
                    {
                        plateText = ((IJavaScriptExecutor)_driver).ExecuteScript("return arguments[0].textContent.trim();", element)?.ToString() ?? "";
                    }
                }
                catch { /* Ignorar errores de extracción de texto */ }

                // Simular la selección del vehículo usando JavaScript
                var result = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            try {
                // Obtener información del elemento para simular selección
                const rect = arguments[0].getBoundingClientRect();
                const plateText = arguments[1] || '';
                
                // Disparar eventos de ratón manualmente
                const mouseEvents = ['mousedown', 'mouseup', 'click'];
                for (const eventType of mouseEvents) {
                    const event = new MouseEvent(eventType, {
                        bubbles: true,
                        cancelable: true,
                        view: window,
                        clientX: rect.left + rect.width / 2,
                        clientY: rect.top + rect.height / 2
                    });
                    arguments[0].dispatchEvent(event);
                }
                
                // Crear evento personalizado para notificar selección de vehículo
                const selectEvent = new CustomEvent('vehicleSelected', {
                    detail: { 
                        plate: plateText,
                        element: arguments[0]
                    },
                    bubbles: true
                });
                arguments[0].dispatchEvent(selectEvent);
                document.dispatchEvent(selectEvent);
                
                // Intento adicional: buscar y hacer clic en el marcador del mapa correspondiente
                const markers = document.querySelectorAll('.leaflet-marker-icon, [class*=""marker""]');
                for (const marker of markers) {
                    if (marker.title && marker.title.includes(plateText) || 
                        marker.alt && marker.alt.includes(plateText)) {
                        marker.click();
                        return true;
                    }
                }
                
                // Intento adicional: buscar por ID en el mapa
                if (plateText) {
                    const mapMarkers = document.querySelectorAll(`[id*=""${plateText}""]`);
                    for (const marker of mapMarkers) {
                        if (marker.offsetWidth > 0 && marker.offsetHeight > 0) {
                            marker.click();
                            return true;
                        }
                    }
                }
                
                // Indicar que al menos se intentó
                return 'attempted';
            } catch (e) {
                console.error('Error en simulación:', e);
                return false;
            }
        ", element, plateText);

                if (result != null && (result.ToString() == "True" || result.ToString() == "attempted"))
                {
                    _logger.Info("Simulación de selección completada", true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error en simulación final: {ex.Message}", true);
            }

            _logger.Warning("Todos los intentos de clic fallaron", true);
            return false;
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
