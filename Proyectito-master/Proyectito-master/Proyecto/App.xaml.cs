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
            return new Window(new AppShell());
        }
    }
}