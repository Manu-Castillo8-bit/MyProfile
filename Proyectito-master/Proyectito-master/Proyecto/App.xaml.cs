using Microsoft.Maui.Networking;
using Microsoft.Extensions.DependencyInjection;
using Proyecto.Services;

namespace Proyecto
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            SupabaseService.RestaurarSesion();

            var window = new Window(new AppShell());

            window.Created += (_, _) => RecordatorioScheduler.EnPrimerPlano = true;
            window.Resumed += OnResumed;
            window.Activated += (_, _) =>
            {
                RecordatorioScheduler.EnPrimerPlano = true;
                _ = SyncService.SincronizarAsync();
            };
            window.Deactivated += (_, _) => RecordatorioScheduler.EnPrimerPlano = false;
            window.Stopped += (_, _) => RecordatorioScheduler.EnPrimerPlano = false;

            if (SupabaseService.UsuarioActual is not null)
                _ = RecordatorioScheduler.IniciarAsync();

            // Primer intento de sincronización al iniciar la app.
            _ = SyncService.SincronizarAsync();

            return window;
        }

        private void OnResumed(object? sender, EventArgs e)
        {
            RecordatorioScheduler.EnPrimerPlano = true;
            _ = SyncService.SincronizarAsync();
        }

        private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
                _ = SyncService.SincronizarAsync();
        }
    }
}