using System.Globalization;
using Proyecto.Services;

namespace Proyecto;

public partial class Ahorro : ContentPage
{
    private bool _procesando = false;

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
            LblSaldo.Text = "$" + saldo.ToString("N2", CultureInfo.InvariantCulture);

            var movimientos = await SupabaseService.ObtenerHistorialAsync();
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
                        new ColumnDefinition(new GridLength(0, GridUnitType.Auto)),
                        new ColumnDefinition(new GridLength(0, GridUnitType.Auto)),
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
                    Text = $"{signo}${m.Monto.ToString("N2", CultureInfo.InvariantCulture)}",
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

                var btnEditar = new Button
                {
                    Text = "Editar",
                    BackgroundColor = Color.FromArgb("#1C3A2E"),
                    TextColor = Color.FromArgb("#34C759"),
                    FontSize = 11,
                    CornerRadius = 8,
                    HeightRequest = 32,
                    WidthRequest = 70,
                    Padding = new Thickness(0),
                    VerticalOptions = LayoutOptions.Center
                };
                var btnEliminar = new Button
                {
                    Text = "Eliminar",
                    BackgroundColor = Color.FromArgb("#3A1C1C"),
                    TextColor = Color.FromArgb("#FF3B30"),
                    FontSize = 11,
                    CornerRadius = 8,
                    HeightRequest = 32,
                    WidthRequest = 70,
                    Padding = new Thickness(0),
                    VerticalOptions = LayoutOptions.Center
                };
                var movimientoCapturado = m;
                btnEditar.Clicked += async (s, e) => await OnEditarMovimientoClicked(movimientoCapturado);
                btnEliminar.Clicked += async (s, e) => await OnEliminarMovimientoClicked(movimientoCapturado);

                Grid.SetColumn(descLabel, 0);
                Grid.SetColumn(rightStack, 1);
                Grid.SetColumn(btnEditar, 2);
                Grid.SetColumn(btnEliminar, 3);
                grid.Children.Add(descLabel);
                grid.Children.Add(rightStack);
                grid.Children.Add(btnEditar);
                grid.Children.Add(btnEliminar);

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
        await RegistrarAsync("ingreso", (Button)sender, IndicadorIngreso);
    }

    private async void OnGastoClicked(object sender, EventArgs e)
    {
        await RegistrarAsync("gasto", (Button)sender, IndicadorGasto);
    }

    private async Task OnEliminarMovimientoClicked(MovimientoFinanciero movimiento)
    {
        var confirmar = await DisplayAlert("Confirmar",
            $"¿Quitar del historial el movimiento de ${movimiento.Monto.ToString("N2", CultureInfo.InvariantCulture)}? (El saldo total no cambia).", "Sí", "No");
        if (!confirmar) return;

        try
        {
            await SupabaseService.EliminarMovimientoAsync(movimiento.IdMovimiento);
            await DisplayAlert("Listo", "Movimiento eliminado del historial.", "OK");
            await CargarDatosAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async Task OnEditarMovimientoClicked(MovimientoFinanciero movimiento)
    {
        try
        {
            // 1) Monto
            var montoStr = await DisplayPromptAsync("Editar movimiento",
                "Nuevo monto:",
                "Aceptar", "Cancelar",
                placeholder: movimiento.Monto.ToString("0.00", CultureInfo.InvariantCulture),
                keyboard: Keyboard.Numeric);
            if (montoStr is null) return;

            var montoNormalizado = montoStr.Trim().Replace(",", "");
            if (!decimal.TryParse(montoNormalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal montoNuevo) || montoNuevo <= 0)
            {
                await DisplayAlert("Error", "El monto debe ser un número mayor a 0.", "OK");
                return;
            }

            // 2) Tipo (ingreso / gasto)
            var tipoNuevo = await DisplayActionSheet("¿Es ingreso o gasto?", "Cancelar", null,
                "Ingreso", "Gasto");
            if (tipoNuevo is null || tipoNuevo == "Cancelar") return;
            tipoNuevo = tipoNuevo.Equals("Ingreso", StringComparison.OrdinalIgnoreCase) ? "ingreso" : "gasto";

            // 3) Descripción
            var descripcionNueva = await DisplayPromptAsync("Editar movimiento",
                "Descripción (opcional):",
                "Aceptar", "Cancelar",
                placeholder: movimiento.Descripcion,
                initialValue: movimiento.Descripcion,
                maxLength: 100);
            if (descripcionNueva is null) return;

            // Verificar que un gasto no deje el saldo en negativo tras el ajuste.
            var saldoActual = await SupabaseService.ObtenerSaldoAsync();
            var ajusteAntiguo = movimiento.Tipo == "ingreso" ? movimiento.Monto : -movimiento.Monto;
            var ajusteNuevo = tipoNuevo == "ingreso" ? montoNuevo : -montoNuevo;
            var saldoAjustado = saldoActual - ajusteAntiguo + ajusteNuevo;

            if (tipoNuevo == "gasto" && saldoAjustado < 0)
            {
                await DisplayAlert("Saldo insuficiente",
                    $"Tras el cambio, el gasto dejaría el saldo en ${saldoAjustado.ToString("N2", CultureInfo.InvariantCulture)}.",
                    "OK");
                return;
            }

            await SupabaseService.ActualizarMovimientoAsync(movimiento.IdMovimiento, montoNuevo, tipoNuevo, descripcionNueva);
            await DisplayAlert("Listo", "Movimiento actualizado.", "OK");
            await CargarDatosAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async Task RegistrarAsync(string tipo, Button btn, ActivityIndicator indicador)
    {
        if (_procesando) return;
        _procesando = true;

        var textoOriginal = btn.Text;
        btn.IsEnabled = false;
        btn.Text = "";
        indicador.IsVisible = true;
        indicador.IsRunning = true;

        try
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

            // Formato El Salvador: "." decimal, "," miles
            var montoNormalizado = montoStr.Replace(",", "");
            if (!decimal.TryParse(montoNormalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal monto) || monto <= 0)
            {
                await DisplayAlert("Error", "El monto debe ser un número mayor a 0.", "OK");
                return;
            }

            if (tipo == "gasto")
            {
                var saldoActual = await SupabaseService.ObtenerSaldoAsync();
                if (monto > saldoActual)
                {
                    await DisplayAlert("Saldo insuficiente",
                        $"El gasto de ${monto.ToString("N2", CultureInfo.InvariantCulture)} supera tu saldo actual de ${saldoActual.ToString("N2", CultureInfo.InvariantCulture)}.",
                        "OK");
                    return;
                }
            }

            await SupabaseService.RegistrarMovimientoAsync(monto, tipo, descripcion);
            await DisplayAlert("Listo", tipo == "ingreso"
                ? $"Ingreso de ${monto.ToString("N2", CultureInfo.InvariantCulture)} registrado."
                : $"Gasto de ${monto.ToString("N2", CultureInfo.InvariantCulture)} registrado.", "OK");

            TxtMonto.Text = "";
            TxtDescripcion.Text = "";
            await CargarDatosAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo registrar: {ex.Message}", "OK");
        }
        finally
        {
            indicador.IsRunning = false;
            indicador.IsVisible = false;
            btn.Text = textoOriginal;
            btn.IsEnabled = true;
            _procesando = false;
        }
    }
}
