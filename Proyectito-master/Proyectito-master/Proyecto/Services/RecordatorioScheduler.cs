using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace Proyecto.Services;

// Planificador de recordatorios (híbrido):
//  - Notificaciones del sistema: se programa una única notificación a la próxima
//    hora de aviso. Si la app está cerrada, la lanza el propio sistema (Android/iOS/Mac).
//  - Avisos dentro de la app: un temporizador comprueba cada 60 s si ya toca avisar
//    y muestra un aviso por pantalla (funciona en todas las plataformas con la app abierta).
public static class RecordatorioScheduler
{
    public const string TipoAgua = "agua";
    public const string TipoDescanso = "descanso";
    public const string TipoTareas = "tareas";

    private const string PrefUltimo = "rec_ultimo_";
    private const int AppIdBase = 1100;
    private static readonly Dictionary<string, int> IdsPorTipo = new()
    {
        [TipoAgua] = AppIdBase + 1,
        [TipoDescanso] = AppIdBase + 2,
        [TipoTareas] = AppIdBase + 3
    };

    // Para saber si la app está en primer plano y decidir el aviso en pantalla.
    public static bool EnPrimerPlano { get; set; } = true;

    private static IDispatcherTimer? _temporizador;
    private static bool _iniciado;
    private static bool _evaluando;

    public static async Task IniciarAsync()
    {
        if (SupabaseService.UsuarioActual is null || _iniciado)
            return;

        var timer = Application.Current?.Dispatcher.CreateTimer();
        if (timer is null)
            return;

        _iniciado = true;
        _temporizador = timer;
        timer.Interval = TimeSpan.FromSeconds(60);
        timer.Tick += async (_, _) => await EvaluarAsync();
        timer.Start();

        await RefrescarAsync();
    }

    public static void Detener()
    {
        _iniciado = false;
        _temporizador?.Stop();
        _temporizador = null;

        foreach (var id in IdsPorTipo.Values)
        {
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }
        }
    }

    // Recarga las preferencias (tras iniciar sesión o al guardar en la pantalla de Salud)
    // y deja programadas las notificaciones del sistema.
    public static async Task RefrescarAsync()
    {
        if (SupabaseService.UsuarioActual is null)
            return;

        await EvaluarAsync(onlyReSchedule: true);
    }

    public static async Task AsegurarPermisoAsync()
    {
        try
        {
            if (!await LocalNotificationCenter.Current.AreNotificationsEnabled())
                await LocalNotificationCenter.Current.RequestNotificationPermission();
        }
        catch { }
    }

    private static async Task EvaluarAsync(bool onlyReSchedule = false)
    {
        if (_evaluando)
            return;

        _evaluando = true;
        try
        {
            if (SupabaseService.UsuarioActual is null)
                return;

            var lista = await SupabaseService.ObtenerRecordatoriosAsync();
            if (lista.Count == 0)
            {
                foreach (var id in IdsPorTipo.Values)
                {
                    try { LocalNotificationCenter.Current.Cancel(id); } catch { }
                }
                return;
            }

            await EvaluarTipoAsync(lista, TipoAgua, "Hidratación",
                "¡Hora de beber agua! 💧 Tu cuerpo lo agradece.", onlyReSchedule);
            await EvaluarTipoAsync(lista, TipoDescanso, "Descanso visual",
                "Mira a lo lejos durante 20 segundos para cuidar tu vista 👁️", onlyReSchedule);
            await EvaluarTipoTareasAsync(lista, onlyReSchedule);
        }
        catch
        {
            // Si no hay conexión o falla la consulta, se intenta de nuevo en el siguiente ciclo.
        }
        finally
        {
            _evaluando = false;
        }
    }

    private static async Task EvaluarTipoAsync(List<RecordatorioSalud> lista, string tipo, string titulo, string mensaje, bool onlyReSchedule)
    {
        var rec = lista.FirstOrDefault(r => string.Equals(r.Tipo, tipo, StringComparison.OrdinalIgnoreCase));
        int id = IdsPorTipo[tipo];

        if (rec is null || !rec.Activo || rec.FrecuenciaMinutos <= 0)
        {
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }
            return;
        }

        if (!onlyReSchedule && LeDebeAvisar(tipo, rec.FrecuenciaMinutos))
        {
            GuardarUltimo(tipo, DateTime.Now);
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }
            await MostrarAsync(id, titulo, mensaje);
        }

        ProgramarProximaNotificacion(id, tipo, rec.FrecuenciaMinutos, titulo, mensaje);
    }

    private static async Task EvaluarTipoTareasAsync(List<RecordatorioSalud> lista, bool onlyReSchedule)
    {
        var rec = lista.FirstOrDefault(r => string.Equals(r.Tipo, TipoTareas, StringComparison.OrdinalIgnoreCase));
        int id = IdsPorTipo[TipoTareas];

        if (rec is null || !rec.Activo || rec.FrecuenciaMinutos <= 0)
        {
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }
            return;
        }

        int pendientes = (await SupabaseService.ObtenerTareasAsync())
            .Count(t => !string.Equals(t.Estado, "completada", StringComparison.OrdinalIgnoreCase));

        // Si no hay tareas pendientes, no tiene sentido recordarlas.
        if (pendientes == 0)
        {
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }
            return;
        }

        var titulo = pendientes == 1 ? "Tienes 1 tarea pendiente" : $"Tienes {pendientes} tareas pendientes";
        var mensaje = "Abre tu lista de tareas y organiza tu día 📝";

        if (!onlyReSchedule && LeDebeAvisar(TipoTareas, rec.FrecuenciaMinutos))
        {
            GuardarUltimo(TipoTareas, DateTime.Now);
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }
            await MostrarAsync(id, titulo, mensaje);
        }

        ProgramarProximaNotificacion(id, TipoTareas, rec.FrecuenciaMinutos, titulo, mensaje);
    }

    private static bool LeDebeAvisar(string tipo, int frecuenciaMin)
    {
        var ultimo = ObtenerUltimo(tipo);
        if (ultimo == DateTime.MinValue)
            return false;

        return DateTime.Now >= ultimo.AddMinutes(frecuenciaMin);
    }

    private static void ProgramarProximaNotificacion(int id, string tipo, int frecuenciaMin, string titulo, string mensaje)
    {
        try
        {
            var proximo = ObtenerUltimo(tipo) == DateTime.MinValue
                ? DateTime.Now.AddMinutes(frecuenciaMin)
                : ObtenerUltimo(tipo).AddMinutes(frecuenciaMin);

            if (proximo <= DateTime.Now)
                proximo = DateTime.Now.AddMinutes(frecuenciaMin);

            var request = new NotificationRequest
            {
                NotificationId = id,
                Title = titulo,
                Description = mensaje,
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = new DateTimeOffset(proximo)
                }
            };

            LocalNotificationCenter.Current.Show(request);
        }
        catch
        {
            // En Windows el agendamiento puede no estar disponible; el temporizador in-app cubre ese caso.
        }
    }

    private static async Task MostrarAsync(int id, string titulo, string mensaje)
    {
        await AsegurarPermisoAsync();

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await LocalNotificationCenter.Current.Show(new NotificationRequest
                {
                    NotificationId = id,
                    Title = titulo,
                    Description = mensaje
                });
            }
            catch { }

            if (EnPrimerPlano && Shell.Current is not null)
            {
                try { await Shell.Current.DisplayAlert(titulo, mensaje, "OK"); }
                catch { }
            }
        });
    }

    private static DateTime ObtenerUltimo(string tipo)
    {
        var valor = Preferences.Default.Get(PrefUltimo + tipo, "");
        return DateTime.TryParse(valor, out var fecha) ? fecha : DateTime.MinValue;
    }

    private static void GuardarUltimo(string tipo, DateTime valor)
        => Preferences.Default.Set(PrefUltimo + tipo, valor.ToString("o"));
}