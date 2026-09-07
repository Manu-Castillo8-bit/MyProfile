using Proyecto.Services;

namespace Proyecto;

public partial class DashboardPage : ContentPage
{
	public DashboardPage()
	{
		InitializeComponent();
	}

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarDatosAsync();
    }

    private async Task CargarDatosAsync()
    {
        if (SupabaseService.UsuarioActual is null)
        {
            await Shell.Current.GoToAsync("//LoginPage");
            return;
        }

        try
        {
            LblNombre.Text = $"Hola, {SupabaseService.UsuarioActual.Nombre}";

            var tareas = await SupabaseService.ObtenerTareasAsync();
            int total = tareas.Count;
            int completadas = tareas.Count(t => string.Equals(t.Estado, "completada", StringComparison.OrdinalIgnoreCase));
            int pendientes = total - completadas;

            LblConteoTareas.Text = $"{pendientes} pendientes";
            double progreso = total == 0 ? 0 : (double)completadas / total;
            ProgTareas.Progress = progreso;
            LblProgresoTareas.Text = $"{(int)Math.Round(progreso * 100)}% completado";

            var saldo = await SupabaseService.ObtenerSaldoAsync();
            LblSaldoDashboard.Text = $"${saldo:N2}";

            int vasosHoy = ProgresoSalud.VasosHoy();
            int descansosHoy = ProgresoSalud.DescansosHoy();
            LblDatosAgua.Text = $"Vasos hoy: {vasosHoy}";
            LblDatosDescanso.Text = $"{descansosHoy} hoy";
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudieron cargar los datos: {ex.Message}", "OK");
        }
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        bool confirmar = await DisplayAlert("Cerrar sesión", "¿Seguro que deseas salir?", "Sí", "No");
        if (!confirmar) return;

        SupabaseService.CerrarSesion();
        await Shell.Current.GoToAsync("//LoginPage");
    }

    private async void OnAccesoTareasClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("//Principal/MainPage");
    private async void OnAccesoAhorroClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("//Principal/Ahorro");
    private async void OnAccesoSaludClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("//Principal/Salud");
    private async void OnAccesoPasswordClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("//Principal/Password");
}