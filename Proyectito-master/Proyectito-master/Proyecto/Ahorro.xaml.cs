using Proyecto.Services;

namespace Proyecto;

public partial class Ahorro : ContentPage
{
    public Ahorro()
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
            await DisplayAlert("Aviso", "Debes iniciar sesión primero.", "OK");
            return;
        }

        try
        {
            var saldo = await SupabaseService.ObtenerSaldoAsync();
            LblSaldo.Text = $"${saldo:N2}";

            var movimientos = await SupabaseService.ObtenerMovimientosAsync();
            HistorialStack.Children.Clear();

            foreach (var m in movimientos)
            {
                var color = m.Tipo == "ingreso" ? "#34C759" : "#FF3B30";
                var signo = m.Tipo == "ingreso" ? "+" : "-";
                var fecha = m.Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

                var frame = new Frame
                {
                    BackgroundColor = Color.FromArgb("#1E1E1E"),
                    BorderColor = Color.FromArgb("#2C2C2C"),
                    CornerRadius = 12,
                    Padding = new Thickness(14),
                    HasShadow = false
                };

                var grid = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                        new ColumnDefinition(new GridLength(0, GridUnitType.Auto))
                    },
                    ColumnSpacing = 10
                };

                var descLabel = new Label
                {
                    Text = string.IsNullOrWhiteSpace(m.Descripcion) ? "(Sin descripción)" : m.Descripcion,
                    TextColor = Colors.White,
                    FontSize = 14,
                    VerticalTextAlignment = TextAlignment.Center
                };

                var montoLabel = new Label
                {
                    Text = $"{signo}${m.Monto:N2}",
                    TextColor = Color.FromArgb(color),
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    VerticalTextAlignment = TextAlignment.Center
                };

                var fechaLabel = new Label
                {
                    Text = fecha,
                    TextColor = Color.FromArgb("#8E8E93"),
                    FontSize = 11,
                    VerticalTextAlignment = TextAlignment.End
                };

                var rightStack = new VerticalStackLayout
                {
                    Children = { montoLabel, fechaLabel },
                    VerticalOptions = LayoutOptions.Center
                };

                Grid.SetColumn(descLabel, 0);
                Grid.SetColumn(rightStack, 1);
                grid.Children.Add(descLabel);
                grid.Children.Add(rightStack);

                frame.Content = grid;
                HistorialStack.Children.Add(frame);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudieron cargar los datos: {ex.Message}", "OK");
        }
    }

    private async void OnIngresoClicked(object sender, EventArgs e)
    {
        await RegistrarAsync("ingreso");
    }

    private async void OnGastoClicked(object sender, EventArgs e)
    {
        await RegistrarAsync("gasto");
    }

    private async Task RegistrarAsync(string tipo)
    {
        if (SupabaseService.UsuarioActual is null)
        {
            await DisplayAlert("Aviso", "Debes iniciar sesión.", "OK");
            return;
        }

        var montoStr = TxtMonto.Text?.Trim() ?? "";
        var descripcion = TxtDescripcion.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(montoStr))
        {
            await DisplayAlert("Error", "Ingresa un monto.", "OK");
            return;
        }

        if (!decimal.TryParse(montoStr, out decimal monto) || monto <= 0)
        {
            await DisplayAlert("Error", "El monto debe ser un número mayor a 0.", "OK");
            return;
        }

        try
        {
            await SupabaseService.RegistrarMovimientoAsync(monto, tipo, descripcion);
            await DisplayAlert("Listo", tipo == "ingreso"
                ? $"Ingreso de ${monto:N2} registrado."
                : $"Gasto de ${monto:N2} registrado.", "OK");

            TxtMonto.Text = "";
            TxtDescripcion.Text = "";
            await CargarDatosAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo registrar: {ex.Message}", "OK");
        }
    }
}
