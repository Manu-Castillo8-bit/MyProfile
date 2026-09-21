using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Proyecto.Services;

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

                // Atiende los botones de la notificación de "suspender pantalla".
                AppNotificationManager.Default.NotificationInvoked += (_, args) =>
                {
                    if (args.Arguments is not null &&
                        args.Arguments.TryGetValue("accion", out var valor) &&
                        int.TryParse(valor, out int accion))
                    {
                        RecordatorioScheduler.EjecutarAccion(accion);
                    }
                };
            }
            catch
            {
                // Sin el Windows App Runtime o con las notificaciones
                // desactivadas, el registro no es posible; los recordatorios
                // seguirán avisando por pantalla dentro de la app.
            }
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            base.OnLaunched(args);

            // Atiende los botones de la notificación de "suspender pantalla"
            // cuando la activación llega por arranque (app unpackaged): la
            // pulsación del botón de un toast lanza/activa la app por
            // OnLaunched en lugar de por el evento NotificationInvoked.
            try
            {
                var activacion = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                if (activacion.Data is Microsoft.Windows.AppNotifications.AppNotificationActivatedEventArgs notificacion &&
                    notificacion.Arguments is not null &&
                    notificacion.Arguments.TryGetValue("accion", out var valor) &&
                    int.TryParse(valor, out int accion))
                {
                    RecordatorioScheduler.EjecutarAccion(accion);
                }
            }
            catch { }

            // Mantiene la app viva en la bandeja del sistema al cerrar la
            // ventana, para no perder los recordatorios de Windows.
            try
            {
                var ventanaMaui = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
                if (ventanaMaui?.Handler?.PlatformView is Microsoft.UI.Xaml.Window ventana)
                    BandejaSistema.Configurar(ventana);
            }
            catch { }
        }
    }
}
