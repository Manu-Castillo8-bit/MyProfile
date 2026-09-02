using Proyecto.Services;

namespace Proyecto;

public partial class LoginPage : ContentPage
{
    private bool _isPasswordVisible = false;
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

        try
        {
            var usuario = await SupabaseService.LoginAsync(correo, contrasena);
            if (usuario is null)
            {
                await DisplayAlert("Error", "Correo o contraseña incorrectos.", "OK");
                return;
            }

            await Shell.Current.GoToAsync("//Principal/DashboardPage");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo iniciar sesión: {ex.Message}", "OK");
        }
    }

    private async void OnForgotPasswordTapped(object sender, TappedEventArgs e)
    {
    }

    private async void OnRegisterTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("Registrarse");
    }
}