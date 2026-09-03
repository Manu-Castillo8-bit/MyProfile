using Proyecto.Services;

namespace Proyecto;

public partial class Password : ContentPage
{
    private List<Contrasena> _todasLasContrasenas = new();
    private int? _editandoId = null;

    public Password()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarContrasenasAsync();
    }

    private async Task CargarContrasenasAsync()
    {
        if (SupabaseService.UsuarioActual is null)
        {
            await DisplayAlert("Aviso", "Debes iniciar sesión primero.", "OK");
            return;
        }

        try
        {
            _todasLasContrasenas = await SupabaseService.ObtenerContrasenasAsync();
            MostrarContrasenas(_todasLasContrasenas);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudieron cargar las credenciales: {ex.Message}", "OK");
        }
    }

    private void MostrarContrasenas(List<Contrasena> lista)
    {
        ListaCuentas.Children.Clear();

        if (lista.Count == 0)
        {
            ListaCuentas.Children.Add(new Label
            {
                Text = "No hay credenciales guardadas.",
                TextColor = Color.FromArgb("#8E8E93"),
                FontSize = 14,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            });
            return;
        }

        foreach (var c in lista)
        {
            var frame = new Frame
            {
                BackgroundColor = Color.FromArgb("#1E1E1E"),
                BorderColor = Color.FromArgb("#2C2C2C"),
                CornerRadius = 14,
                Padding = new Thickness(16),
                HasShadow = false
            };

            var grid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(new GridLength(0, GridUnitType.Auto)),
                    new RowDefinition(new GridLength(0, GridUnitType.Auto)),
                    new RowDefinition(new GridLength(0, GridUnitType.Auto))
                },
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(0, GridUnitType.Auto))
                },
                ColumnSpacing = 10
            };

            var sitioLabel = new Label
            {
                Text = c.SitioWeb,
                TextColor = Colors.White,
                FontSize = 16,
                FontAttributes = FontAttributes.Bold
            };

            var usuarioLabel = new Label
            {
                Text = c.UsuarioCuenta,
                TextColor = Color.FromArgb("#8E8E93"),
                FontSize = 13
            };

            var claveLabel = new Label
            {
                Text = "••••••••",
                TextColor = Color.FromArgb("#555555"),
                FontSize = 13
            };

            var btnEditar = new Button
            {
                Text = "Editar",
                BackgroundColor = Color.FromArgb("#2C2C2C"),
                TextColor = Colors.White,
                FontSize = 12,
                CornerRadius = 8,
                HeightRequest = 34,
                WidthRequest = 70,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 6)
            };
            btnEditar.Clicked += (s, e) => OnEditarClicked(c);

            var btnEliminar = new Button
            {
                Text = "Eliminar",
                BackgroundColor = Color.FromArgb("#3A1C1C"),
                TextColor = Color.FromArgb("#FF3B30"),
                FontSize = 12,
                CornerRadius = 8,
                HeightRequest = 34,
                WidthRequest = 70,
                Padding = new Thickness(0)
            };
            btnEliminar.Clicked += (s, e) => OnEliminarClicked(c);

            var botonesStack = new VerticalStackLayout
            {
                Children = { btnEditar, btnEliminar },
                VerticalOptions = LayoutOptions.Center
            };

            Grid.SetRow(sitioLabel, 0);
            Grid.SetColumn(sitioLabel, 0);
            Grid.SetRow(usuarioLabel, 1);
            Grid.SetColumn(usuarioLabel, 0);
            Grid.SetRow(claveLabel, 2);
            Grid.SetColumn(claveLabel, 0);
            Grid.SetRow(botonesStack, 0);
            Grid.SetColumn(botonesStack, 1);
            Grid.SetRowSpan(botonesStack, 3);

            grid.Children.Add(sitioLabel);
            grid.Children.Add(usuarioLabel);
            grid.Children.Add(claveLabel);
            grid.Children.Add(botonesStack);

            frame.Content = grid;
            ListaCuentas.Children.Add(frame);
        }
    }

    private void OnBuscarChanged(object sender, TextChangedEventArgs e)
    {
        var texto = e.NewTextValue?.Trim().ToLower() ?? "";
        if (string.IsNullOrEmpty(texto))
        {
            MostrarContrasenas(_todasLasContrasenas);
            return;
        }

        var filtradas = _todasLasContrasenas
            .Where(c => c.SitioWeb.ToLower().Contains(texto) || c.UsuarioCuenta.ToLower().Contains(texto))
            .ToList();

        MostrarContrasenas(filtradas);
    }

    private void OnAgregarClicked(object sender, EventArgs e)
    {
        _editandoId = null;
        LblFormTitulo.Text = "Nueva credencial";
        TxtSitioWeb.Text = "";
        TxtUsuarioCuenta.Text = "";
        TxtClave.Text = "";
        TxtClave.IsPassword = true;
        LblClave.Text = "Clave";
        FormOverlay.IsVisible = true;
    }

    private void OnEditarClicked(Contrasena contrasena)
    {
        _editandoId = contrasena.IdContrasena;
        LblFormTitulo.Text = "Editar credencial";
        TxtSitioWeb.Text = contrasena.SitioWeb;
        TxtUsuarioCuenta.Text = contrasena.UsuarioCuenta;
        TxtClave.Text = "";
        TxtClave.IsPassword = true;
        LblClave.Text = "Nueva clave (dejar vacío para mantener)";
        FormOverlay.IsVisible = true;
    }

    private void OnCancelarClicked(object sender, EventArgs e)
    {
        FormOverlay.IsVisible = false;
        _editandoId = null;
    }

    private async void OnGuardarClicked(object sender, EventArgs e)
    {
        var sitioWeb = TxtSitioWeb.Text?.Trim() ?? "";
        var usuarioCuenta = TxtUsuarioCuenta.Text?.Trim() ?? "";
        var clave = TxtClave.Text ?? "";

        if (string.IsNullOrWhiteSpace(sitioWeb) || string.IsNullOrWhiteSpace(usuarioCuenta))
        {
            await DisplayAlert("Error", "Sitio web y usuario son obligatorios.", "OK");
            return;
        }

        try
        {
            if (_editandoId.HasValue)
            {
                if (string.IsNullOrWhiteSpace(clave))
                    await SupabaseService.ActualizarContrasenaAsync(_editandoId.Value, sitioWeb, usuarioCuenta, "");
                else
                    await SupabaseService.ActualizarContrasenaAsync(_editandoId.Value, sitioWeb, usuarioCuenta, clave);

                await DisplayAlert("Listo", "Credencial actualizada.", "OK");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(clave))
                {
                    await DisplayAlert("Error", "Debes ingresar una clave.", "OK");
                    return;
                }
                await SupabaseService.CrearContrasenaAsync(sitioWeb, usuarioCuenta, clave);
                await DisplayAlert("Listo", "Credencial creada.", "OK");
            }

            FormOverlay.IsVisible = false;
            _editandoId = null;
            await CargarContrasenasAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void OnEliminarClicked(Contrasena contrasena)
    {
        var confirmar = await DisplayAlert("Confirmar", $"Eliminar credencial de \"{contrasena.SitioWeb}\"?", "Sí", "No");
        if (!confirmar) return;

        try
        {
            await SupabaseService.EliminarContrasenaAsync(contrasena.IdContrasena);
            await DisplayAlert("Listo", "Credencial eliminada.", "OK");
            await CargarContrasenasAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }
}
