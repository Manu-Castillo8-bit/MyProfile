using Proyecto.Services;

namespace Proyecto;

public partial class LoginPage : ContentPage
{
    private bool _isPasswordVisible = false;
    private bool _procesando = false;
    public LoginPage()
	{
		InitializeComponent();
	}

    private void OnTogglePasswordClicked(object sender, EventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        TxtContrasena.IsPassword = !_isPasswordVisible;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        var correo = TxtCorreo.Text?.Trim() ?? "";
        var contrasena = TxtContrasena.Text ?? "";

        if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrEmpty(contrasena))
        {
            await DisplayAlert("Error", "Ingresa tu correo y contraseña.", "OK");
            return;
        }

        if (_procesando) return;
        _procesando = true;

        var btn = (Button)sender;
        var textoOriginal = btn.Text;
        btn.IsEnabled = false;
        btn.Text = "";
        IndicadorLogin.IsVisible = true;
        IndicadorLogin.IsRunning = true;

        // Animación de presionado (rebote)
        await btn.ScaleTo(0.93, 80, Easing.CubicOut);
        await btn.ScaleTo(1.0, 130, Easing.CubicOut);

        // Pulso tecnológico mientras se procesa
        _ = AnimarPulsosAsync(btn);

        try
        {
            var usuario = await SupabaseService.LoginAsync(correo, contrasena);
            if (usuario is null)
            {
                await DisplayAlert("Error", "Correo o contraseña incorrectos.", "OK");
                return;
            }

            SupabaseService.EstablecerSesion(usuario);
            await Shell.Current.GoToAsync("//Principal/DashboardPage");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo iniciar sesión: {ex.Message}", "OK");
        }
        finally
        {
            _procesando = false;
            IndicatorDetenido(IndicadorLogin, btn, textoOriginal);
        }
    }

    private static void IndicatorDetenido(ActivityIndicator indicador, Button btn, string textoOriginal)
    {
        indicador.IsRunning = false;
        indicador.IsVisible = false;
        btn.Text = textoOriginal;
        btn.IsEnabled = true;
        btn.Scale = 1;
        btn.Opacity = 1;
    }

    private async Task AnimarPulsosAsync(Button btn)
    {
        while (_procesando)
        {
            await btn.ScaleTo(0.97, 180, Easing.SinInOut);
            await btn.ScaleTo(1.05, 180, Easing.SinInOut);
        }
    }

    private async void OnForgotPasswordTapped(object sender, TappedEventArgs e)
    {
    }

    private async void OnRegisterTapped(object sender, TappedEventArgs e)
    {
        var label = (Label)sender;
        await label.ScaleTo(0.92, 80, Easing.CubicOut);
        await label.ScaleTo(1.0, 120, Easing.CubicOut);
        await Shell.Current.GoToAsync("///Registrarse");
    }
}