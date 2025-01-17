using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace VehicleTracking.Util.Constants
{
    public class PanelControl
    {
        private readonly IWebDriver _driver;
        private readonly WebDriverWait _wait;
        private readonly ILogger _logger;
        private bool _isPanelExpanded = true;

        public PanelControl(IWebDriver driver, WebDriverWait wait, ILogger logger)
        {
            _driver = driver;
            _wait = wait;
            _logger = logger;
        }

        public async Task MinimizePanel()
        {
            if (!_isPanelExpanded) return;

            try
            {
                // Buscar el botón de minimizar usando el ID específico
                var minimizeButton = _wait.Until(d => d.FindElement(By.CssSelector("div.x-tool.x-tool-toggle#ext-gen17")));

                if (minimizeButton != null && minimizeButton.Displayed)
                {
                    // Intentar múltiples estrategias de clic
                    try
                    {
                        await ClickElementWithRetry(minimizeButton);
                        _isPanelExpanded = false;
                        await Task.Delay(1000); // Esperar a que la animación termine
                    }
                    catch (Exception clickEx)
                    {
                        _logger.LogWarning(clickEx, "Error al intentar hacer clic directo, intentando con JavaScript");

                        try
                        {
                            IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;
                            js.ExecuteScript("arguments[0].click();", minimizeButton);
                            _isPanelExpanded = false;
                            await Task.Delay(1000);
                        }
                        catch (Exception jsEx)
                        {
                            _logger.LogError(jsEx, "Error al intentar hacer clic con JavaScript");
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al intentar minimizar el panel de opciones");
                // Intentar con selector alternativo si el primero falla
                try
                {
                    var alternativeButton = _wait.Until(d => d.FindElement(By.CssSelector(".x-tool-toggle")));
                    if (alternativeButton != null && alternativeButton.Displayed)
                    {
                        await ClickElementWithRetry(alternativeButton);
                        _isPanelExpanded = false;
                        await Task.Delay(1000);
                    }
                }
                catch (Exception altEx)
                {
                    _logger.LogError(altEx, "Error al intentar minimizar el panel con selector alternativo");
                    throw;
                }
            }
        }

        public async Task ExpandPanel()
        {
            if (_isPanelExpanded) return;

            try
            {
                // El mismo botón sirve para expandir, solo cambia el estado
                var expandButton = _wait.Until(d => d.FindElement(By.CssSelector("div.x-tool.x-tool-toggle#ext-gen17")));
                if (expandButton != null)
                {
                    await ClickElementWithRetry(expandButton);
                    _isPanelExpanded = true;
                    await Task.Delay(1000); // Esperar a que la animación termine
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al intentar expandir el panel de opciones");
                // Intentar con selector alternativo
                try
                {
                    var alternativeButton = _wait.Until(d => d.FindElement(By.CssSelector(".x-tool-toggle")));
                    if (alternativeButton != null && alternativeButton.Displayed)
                    {
                        await ClickElementWithRetry(alternativeButton);
                        _isPanelExpanded = true;
                        await Task.Delay(1000);
                    }
                }
                catch (Exception altEx)
                {
                    _logger.LogError(altEx, "Error al intentar expandir el panel con selector alternativo");
                    throw;
                }
            }
        }

        private async Task ClickElementWithRetry(IWebElement element, int maxAttempts = 3)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    // Scrollear al elemento si es necesario
                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView(true);", element);
                    await Task.Delay(500);

                    // Intentar clic normal
                    element.Click();
                    return;
                }
                catch
                {
                    try
                    {
                        // Intentar clic con JavaScript
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", element);
                        return;
                    }
                    catch
                    {
                        if (i == maxAttempts - 1) throw;
                        await Task.Delay(500);
                    }
                }
            }
        }
       
    }
}
