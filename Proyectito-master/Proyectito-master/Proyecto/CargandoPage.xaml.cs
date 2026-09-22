using System.Diagnostics;
using Proyecto.Services;

namespace Proyecto;

public partial class CargandoPage : ContentPage
{
    private static readonly string[] Consejos =
    {
        "MyProfile reúne tus tareas, finanzas, salud y contraseñas en un solo lugar.",
        "Organiza tus tareas y marca tu progreso día con día.",
        "Controla tus ingresos y gastos para alcanzar tus metas de ahorro.",
        "Cuida tu salud con recordatorios de hidratación y descanso visual.",
        "Guarda tus contraseñas de forma segura y encuéntralas al instante.",
        "Funciona incluso sin conexión: tus datos se sincronizan solos."
    };

    private const double DuracionSegundos = 15.0;
    private const double SegundosPorConsejo = 2.3;
    private bool _animando;

    public CargandoPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (SupabaseService.UsuarioActual is null)
        {
            await Shell.Current.GoToAsync("//LoginPage");
            return;
        }

        if (_animando) return;
        _animando = true;

        try
        {
            // Las cuentas de administrador entran directo al panel y no ven
            // las secciones de usuario normal (tareas, ahorro, salud, etc.).
            await SupabaseService.RefrescarRolAsync();
            bool esAdmin = string.Equals(SupabaseService.UsuarioActual?.Rol, "admin", StringComparison.OrdinalIgnoreCase);

            int indice = 0;
            LblConsejo.Text = Consejos[indice];
            LblConsejo.Opacity = 0;
            await LblConsejo.FadeTo(1, 300);

            var cronometro = Stopwatch.StartNew();
            var proximoCambio = TimeSpan.FromSeconds(SegundosPorConsejo);

            while (cronometro.Elapsed.TotalSeconds < DuracionSegundos)
            {
                double avance = Math.Min(1.0, cronometro.Elapsed.TotalSeconds / DuracionSegundos);
                ProgCarga.Progress = avance;
                LblPorcentaje.Text = $"{(int)Math.Round(avance * 100)}%";

                if (cronometro.Elapsed >= proximoCambio)
                {
                    proximoCambio = cronometro.Elapsed + TimeSpan.FromSeconds(SegundosPorConsejo);
                    indice = (indice + 1) % Consejos.Length;

                    await LblConsejo.FadeTo(0, 150);
                    LblConsejo.Text = Consejos[indice];
                    await LblConsejo.FadeTo(1, 250);
                }

                await Task.Delay(50);
            }

            ProgCarga.Progress = 1;
            LblPorcentaje.Text = "100%";

            await Shell.Current.GoToAsync(esAdmin
                ? "//AdminPrincipal/AdminPage"
                : "//Principal/DashboardPage");
        }
        finally
        {
            _animando = false;
        }
    }
}
