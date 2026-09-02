namespace Proyecto;

public partial class BottomNavBar : ContentView
{
    public static readonly BindableProperty SelectedTabProperty = BindableProperty.Create(
        nameof(SelectedTab),
        typeof(int),
        typeof(BottomNavBar),
        0,
        propertyChanged: (b, o, n) => ((BottomNavBar)b).UpdateSelectedTab());

    public int SelectedTab
    {
        get => (int)GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    public BottomNavBar()
    {
        InitializeComponent();
        UpdateSelectedTab();
    }

    private void UpdateSelectedTab()
    {
        SetActive(LblTareas, SelectedTab == 0);
        SetActive(LblDashboard, SelectedTab == 1);
        SetActive(LblSalud, SelectedTab == 2);
        SetActive(LblAhorro, SelectedTab == 3);
        SetActive(LblPassword, SelectedTab == 4);
    }

    private static void SetActive(Label? label, bool active)
    {
        if (label != null)
        {
            label.TextColor = active ? Colors.White : Color.FromArgb("#8E8E93");
        }
    }

    private void OnTareasTapped(object? sender, TappedEventArgs e) => NavigateTo("MainPage");
    private void OnDashboardTapped(object? sender, TappedEventArgs e) => NavigateTo("DashboardPage");
    private void OnSaludTapped(object? sender, TappedEventArgs e) => NavigateTo("Salud");
    private void OnAhorroTapped(object? sender, TappedEventArgs e) => NavigateTo("Ahorro");
    private void OnPasswordTapped(object? sender, TappedEventArgs e) => NavigateTo("Password");

    private static void NavigateTo(string route)
    {
        if (Shell.Current is not Shell shell)
        {
            return;
        }

        foreach (var item in shell.Items)
        {
            foreach (var section in item.Items)
            {
                foreach (var content in section.Items)
                {
                    if (string.Equals(content.Route, route, StringComparison.Ordinal))
                    {
                        shell.CurrentItem = item;
                        return;
                    }
                }
            }
        }
    }
}