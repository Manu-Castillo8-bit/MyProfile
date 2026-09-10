using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Proyecto.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();

            // En apps "unpackaged" es obligatorio llamar a Register() nada más
            // arrancar para poder mostrar toasts (la registración de la app
            // ante Windows como destino de notificaciones).
            try
            {
                AppNotificationManager.Default.Register();
            }
            catch
            {
                // Sin el Windows App Runtime o con las notificaciones
                // desactivadas, el registro no es posible; los recordatorios
                // seguirán avisando por pantalla dentro de la app.
            }
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

}
