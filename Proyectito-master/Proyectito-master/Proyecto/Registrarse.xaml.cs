using Proyecto.Services;

namespace Proyecto;

public partial class Registrarse : ContentPage
{
    private bool _isPasswordVisible = false;
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

        try
        {
            // Pasa el nombre como primer argumento
            await SupabaseService.RegistrarAsync(nombre, correo, contrasena);

            await DisplayAlert("Éxito", "Usuario registrado correctamente.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }

    }

    private async void OnRegisterTapped(object sender, TappedEventArgs e)
    {
        await Navigation.PopAsync();
    }
}