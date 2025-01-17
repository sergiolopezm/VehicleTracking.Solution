using System.Diagnostics;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace VehicleTracking.Utils.Helpers
{
    public class DynamicWaitHelper
    {
        private readonly IWebDriver _driver;
        private readonly int _maxWaitSeconds;
        private readonly int _pollingIntervalMs;
        private readonly Dictionary<string, ElementMetrics> _elementMetrics;
        private readonly object _lockObject = new object();

        private class ElementMetrics
        {
            public double AverageLoadTime { get; set; }
            public double MinLoadTime { get; set; }
            public double MaxLoadTime { get; set; }
            public int SampleCount { get; set; }
            public DateTime LastSuccess { get; set; }
            public bool WasRecentlyFast => (DateTime.UtcNow - LastSuccess).TotalSeconds < 30;
        }

        public DynamicWaitHelper(IWebDriver driver, int maxWaitSeconds = 60, int pollingIntervalMs = 250)
        {
            _driver = driver;
            _maxWaitSeconds = maxWaitSeconds;
            _pollingIntervalMs = pollingIntervalMs;
            _elementMetrics = new Dictionary<string, ElementMetrics>();
        }

        public async Task<IWebElement?> WaitForElementAsync(By locator, string elementId = "", bool ensureClickable = false)
        {
            elementId = string.IsNullOrEmpty(elementId) ? locator.ToString() : elementId;
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                // Intento rápido inicial si el elemento fue encontrado rápidamente antes
                if (ShouldTryFastPath(elementId))
                {
                    var fastResult = await TryFastFind(locator, ensureClickable);
                    if (fastResult != null)
                    {
                        UpdateMetrics(elementId, stopwatch.Elapsed.TotalSeconds);
                        return fastResult;
                    }
                }

                // Si el intento rápido falla o no aplica, usar espera dinámica
                var timeout = GetDynamicTimeout(elementId);
                var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeout));
                wait.PollingInterval = TimeSpan.FromMilliseconds(GetDynamicPollingInterval(elementId));

                var element = await Task.Run(() =>
                {
                    try
                    {
                        return wait.Until(driver =>
                        {
                            try
                            {
                                var elem = driver.FindElement(locator);
                                if (!ensureClickable)
                                    return elem.Displayed ? elem : null;

                                if (!elem.Displayed || !elem.Enabled)
                                    return null;

                                // Verificación mejorada de clickeable
                                var isClickable = (bool)((IJavaScriptExecutor)driver).ExecuteScript(@"
                                    var elem = arguments[0];
                                    var rect = elem.getBoundingClientRect();
                                    return (
                                        rect.width > 0 &&
                                        rect.height > 0 &&
                                        !elem.disabled &&
                                        window.getComputedStyle(elem).display !== 'none' &&
                                        window.getComputedStyle(elem).visibility !== 'hidden' &&
                                        !document.hidden
                                    );
                                ", elem);

                                return isClickable ? elem : null;
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
                });

                if (element != null)
                {
                    UpdateMetrics(elementId, stopwatch.Elapsed.TotalSeconds);
                }

                return element;
            }
            catch
            {
                return null;
            }
        }

        private async Task<IWebElement?> TryFastFind(By locator, bool ensureClickable)
        {
            try
            {
                var element = _driver.FindElement(locator);
                if (!element.Displayed)
                    return null;

                if (!ensureClickable)
                    return element;

                if (!element.Enabled)
                    return null;

                // Verificación rápida de clickeable
                var isClickable = (bool)((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    var elem = arguments[0];
                    return (
                        elem.offsetWidth > 0 &&
                        elem.offsetHeight > 0 &&
                        !elem.disabled
                    );
                ", element);

                return isClickable ? element : null;
            }
            catch
            {
                return null;
            }
        }

        private bool ShouldTryFastPath(string elementId)
        {
            lock (_lockObject)
            {
                if (!_elementMetrics.TryGetValue(elementId, out var metrics))
                    return false;

                return metrics.WasRecentlyFast && metrics.MinLoadTime < 1.0;
            }
        }

        private void UpdateMetrics(string elementId, double currentTime)
        {
            lock (_lockObject)
            {
                if (!_elementMetrics.TryGetValue(elementId, out var metrics))
                {
                    metrics = new ElementMetrics
                    {
                        AverageLoadTime = currentTime,
                        MinLoadTime = currentTime,
                        MaxLoadTime = currentTime,
                        SampleCount = 1,
                        LastSuccess = DateTime.UtcNow
                    };
                    _elementMetrics[elementId] = metrics;
                }
                else
                {
                    // Actualizar métricas con peso en tiempos recientes
                    metrics.AverageLoadTime = (metrics.AverageLoadTime * 0.7) + (currentTime * 0.3);
                    metrics.MinLoadTime = Math.Min(metrics.MinLoadTime, currentTime);
                    metrics.MaxLoadTime = Math.Max(metrics.MaxLoadTime, currentTime);
                    metrics.SampleCount++;
                    metrics.LastSuccess = DateTime.UtcNow;
                }
            }
        }

        private double GetDynamicTimeout(string elementId)
        {
            lock (_lockObject)
            {
                if (!_elementMetrics.TryGetValue(elementId, out var metrics))
                    return Math.Min(_maxWaitSeconds * 0.3, 10);

                // Si el elemento ha sido consistentemente rápido, usar un timeout más corto
                if (metrics.WasRecentlyFast && metrics.MaxLoadTime < 2.0)
                    return Math.Max(metrics.MaxLoadTime * 2, 2.0);

                // Usar un timeout basado en el historial con margen de seguridad
                var timeout = metrics.AverageLoadTime * 1.5;
                return Math.Min(Math.Max(timeout, 2.0), _maxWaitSeconds);
            }
        }

        private int GetDynamicPollingInterval(string elementId)
        {
            lock (_lockObject)
            {
                if (!_elementMetrics.TryGetValue(elementId, out var metrics))
                    return _pollingIntervalMs;

                // Ajustar el intervalo de polling basado en el tiempo de carga típico
                if (metrics.WasRecentlyFast && metrics.AverageLoadTime < 1.0)
                    return 100; // Polling más frecuente para elementos rápidos

                return _pollingIntervalMs;
            }
        }

        public async Task<bool> WaitForPageLoadAsync(string context = "default")
        {
            try
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                var timeoutSeconds = GetDynamicTimeout($"page_load_{context}");
                var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));
                wait.PollingInterval = TimeSpan.FromMilliseconds(100); // Polling más frecuente para mejor respuesta

                var result = await Task.Run(() =>
                {
                    try
                    {
                        return wait.Until(driver =>
                        {
                            try
                            {
                                // Verificar estado básico del documento
                                var readyState = ((IJavaScriptExecutor)driver)
                                    .ExecuteScript("return document.readyState")
                                    ?.ToString();

                                if (readyState != "complete")
                                    return false;

                                // Verificación específica para el contexto post-login
                                if (context == "post_login")
                                {
                                    try
                                    {
                                        // Verificar elementos específicos de la página post-login
                                        var menuElements = driver.FindElements(By.CssSelector("td.myMenu"));
                                        if (!menuElements.Any(e => e.Displayed))
                                            return false;

                                        // Verificar que no haya overlays o spinners visibles
                                        var overlays = driver.FindElements(By.CssSelector(
                                            ".loading, .spinner, .wait, .x-mask, .x-masked, " +
                                            "[class*='loading'], [class*='spinner'], [class*='wait']"));

                                        if (overlays.Any(e => e.Displayed))
                                            return false;

                                        // Verificar que el DOM está estable
                                        var isDomStable = (bool)((IJavaScriptExecutor)driver).ExecuteScript(@"
                                            return !(document.querySelector('.x-mask') || 
                                                   document.querySelector('.x-masked') ||
                                                   document.querySelector('[class*=""loading""]') ||
                                                   document.querySelector('[class*=""wait""]'))");

                                        if (!isDomStable)
                                            return false;
                                    }
                                    catch
                                    {
                                        return false;
                                    }
                                }

                                // Verificar si hay peticiones AJAX pendientes
                                var jQueryPresent = (bool)((IJavaScriptExecutor)driver)
                                    .ExecuteScript("return typeof jQuery !== 'undefined'");

                                if (jQueryPresent)
                                {
                                    var ajaxComplete = (bool)((IJavaScriptExecutor)driver)
                                        .ExecuteScript("return jQuery.active === 0");
                                    if (!ajaxComplete)
                                        return false;
                                }

                                return true;
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
                });

                if (result)
                {
                    UpdateMetrics($"page_load_{context}", stopwatch.Elapsed.TotalSeconds);
                }

                return result;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> WaitForAjaxCompletionAsync()
        {
            try
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(GetDynamicTimeout("ajax_completion")));
                var result = await Task.Run(() =>
                {
                    try
                    {
                        wait.Until(driver =>
                        {
                            try
                            {
                                // Verificar jQuery si está definido
                                var jQueryDefined = (bool)((IJavaScriptExecutor)driver)
                                    .ExecuteScript("return typeof jQuery != 'undefined'");

                                if (!jQueryDefined)
                                    return true;

                                // Verificar peticiones AJAX pendientes
                                var ajaxCompleted = (bool)((IJavaScriptExecutor)driver)
                                    .ExecuteScript(@"
                                        var pending = jQuery.active;
                                        var queue = jQuery.queue();
                                        return pending === 0 && queue.length === 0;
                                    ");

                                // Verificar animaciones
                                var animationsCompleted = (bool)((IJavaScriptExecutor)driver)
                                    .ExecuteScript("return jQuery(':animated').length === 0");

                                return ajaxCompleted && animationsCompleted;
                            }
                            catch
                            {
                                return true;
                            }
                        });
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (result)
                {
                    UpdateMetrics("ajax_completion", stopwatch.Elapsed.TotalSeconds);
                }

                return result;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> WaitForConditionAsync(Func<IWebDriver, bool> condition, string conditionId = "default_condition", TimeSpan? timeout = null)
        {
            try
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                // Usar el timeout proporcionado o calcular uno dinámico
                var waitTimeout = timeout ?? TimeSpan.FromSeconds(GetDynamicTimeout(conditionId));
                var wait = new WebDriverWait(_driver, waitTimeout);
                wait.PollingInterval = TimeSpan.FromMilliseconds(GetDynamicPollingInterval(conditionId));

                var result = await Task.Run(() =>
                {
                    try
                    {
                        return wait.Until(condition);
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (result)
                {
                    UpdateMetrics(conditionId, stopwatch.Elapsed.TotalSeconds);
                }

                return result;
            }
            catch
            {
                return false;
            }
        }
    }
}
