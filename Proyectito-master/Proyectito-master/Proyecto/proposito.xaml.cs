namespace Proyecto;

public partial class Proposito : ContentPage
{
    public Proposito()
    {
        InitializeComponent();
    }

    private async void OnVolverTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
