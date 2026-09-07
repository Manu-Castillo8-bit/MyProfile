using Proyecto.Services;

namespace Proyecto;

public partial class Registrarse : ContentPage
{
    private bool _isPasswordVisible = false;
    private bool _procesando = false;
	public Registrarse()
	{
		InitializeComponent();
	}

    private void OnTogglePasswordClicked(object sender, EventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        TxtContrasena.IsPassword = !_isPasswordVisible;
    }

    private async void OnForgotPasswordTapped(object sender, TappedEventArgs e)
    {
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        string nombre = TxtNombre.Text;
        string correo = TxtCorreo.Text;
        string contrasena = TxtContrasena.Text;

        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(contrasena))
        {
            await DisplayAlert("Atención", "Por favor llena todos los campos.", "OK");
            return;
        }

        if (_procesando) return;
        _procesando = true;

        var btn = (Button)sender;
        var textoOriginal = btn.Text;
        btn.IsEnabled = false;
        btn.Text = "";
        IndicadorRegistro.IsVisible = true;
        IndicadorRegistro.IsRunning = true;

        // Animación de presionado (rebote)
        await btn.ScaleTo(0.93, 80, Easing.CubicOut);
        await btn.ScaleTo(1.0, 130, Easing.CubicOut);

        // Pulso tecnológico mientras se procesa
        _ = AnimarPulsosAsync(btn);

        try
        {
            await SupabaseService.RegistrarAsync(nombre, correo, contrasena);

            await DisplayAlert("Éxito", "Usuario registrado correctamente.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            _procesando = false;
            IndicatorDetenido(IndicadorRegistro, btn, textoOriginal);
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

    private async void OnRegisterTapped(object sender, TappedEventArgs e)
    {
        var label = (Label)sender;
        await label.ScaleTo(0.92, 80, Easing.CubicOut);
        await label.ScaleTo(1.0, 120, Easing.CubicOut);
        await Shell.Current.GoToAsync("//LoginPage");
    }
}