using Proyecto.Services;

namespace Proyecto;

public partial class AdminPage : ContentPage
{
    private List<UsuarioAdmin> _todos = new();

    public AdminPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!AdminService.EsAdmin)
        {
            await DisplayAlert("Aviso", "No tienes permisos de administrador.", "OK");
            await Shell.Current.GoToAsync("//LoginPage");
            return;
        }

        await CargarUsuariosAsync();
    }

    private async Task CargarUsuariosAsync()
    {
        MostrarIndicador(true);

        try
        {
            _todos = await AdminService.ListarUsuariosAsync();
            int admins = _todos.Count(u => u.EsAdmin);
            LblSubtitulo.Text = $"{_todos.Count} cuenta(s) · {admins} administrador(es) · {_todos.Count - admins} usuario(s)";
            AplicarFiltro();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            MostrarIndicador(false);
        }
    }

    private void MostrarIndicador(bool visible)
    {
        Indicador.IsRunning = visible;
        Indicador.IsVisible = visible;
    }

    private void AplicarFiltro()
    {
        var texto = TxtBuscar.Text?.Trim().ToLower() ?? "";
        var lista = string.IsNullOrEmpty(texto)
            ? _todos
            : _todos.Where(u =>
                (u.Nombre ?? "").ToLower().Contains(texto) ||
                (u.Correo ?? "").ToLower().Contains(texto)).ToList();

        ListaUsuarios.ItemsSource = lista;
        ListaUsuarios.IsVisible = lista.Count > 0;
        LblVacio.IsVisible = lista.Count == 0;
    }

    private async void OnRefrescarClicked(object? sender, EventArgs e) => await CargarUsuariosAsync();

    private async void OnCerrarSesionClicked(object? sender, EventArgs e)
    {
        bool confirmar = await DisplayAlert("Cerrar sesión", "¿Seguro que deseas salir del panel?", "Sí", "No");
        if (!confirmar) return;

        SupabaseService.CerrarSesion();
        await Shell.Current.GoToAsync("//LoginPage");
    }

    private void OnBuscarChanged(object sender, TextChangedEventArgs e)
    {
        AplicarFiltro();
    }

    private async void OnUsuarioSeleccionado(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not UsuarioAdmin usuario)
            return;

        ListaUsuarios.SelectedItem = null;

        await Navigation.PushAsync(new AdminDetallePage(usuario));
    }
}