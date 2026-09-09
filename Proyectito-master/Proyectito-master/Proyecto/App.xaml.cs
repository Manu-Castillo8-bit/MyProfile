using Microsoft.Extensions.DependencyInjection;
using Proyecto.Services;

namespace Proyecto
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            SupabaseService.RestaurarSesion();

            var window = new Window(new AppShell());

            window.Created += (_, _) => RecordatorioScheduler.EnPrimerPlano = true;
            window.Resumed += (_, _) => RecordatorioScheduler.EnPrimerPlano = true;
            window.Activated += (_, _) => RecordatorioScheduler.EnPrimerPlano = true;
            window.Deactivated += (_, _) => RecordatorioScheduler.EnPrimerPlano = false;
            window.Stopped += (_, _) => RecordatorioScheduler.EnPrimerPlano = false;

            if (SupabaseService.UsuarioActual is not null)
                _ = RecordatorioScheduler.IniciarAsync();

            return window;
        }
    }
}