using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

#if WINDOWS
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
#endif

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
    public const string UnidadMin = "min";
    public const string UnidadHora = "h";
    public const string UnidadDia = "d";

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
        timer.Interval = TimeSpan.FromSeconds(30);
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

#if ANDROID
    // Se llama una sola vez, al ACTIVAR un recordatorio: pide el permiso
    // "Alarmas y recordatorios" si Android aún no lo ha concedido.
    public static void AsegurarAlarmasExactas()
    {
        try
        {
            var contexto = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var alarmas = Android.App.AlarmManager.FromContext(contexto);
            if (alarmas is not null && !alarmas.CanScheduleExactAlarms())
            {
                var intent = new Android.Content.Intent(
                    Android.Provider.Settings.ActionRequestScheduleExactAlarm,
                    Android.Net.Uri.Parse("package:" + contexto.PackageName));
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                contexto.StartActivity(intent);
            }
        }
        catch { }
    }
#else
    public static void AsegurarAlarmasExactas()
    {
    }
#endif

    // Resuelve el intervalo real (minutos/horas/días) elegido por el usuario.
    public static TimeSpan ObtenerIntervalo(RecordatorioSalud rec)
    {
        if (rec is null)
            return TimeSpan.FromMinutes(1);

        int valor = rec.FrecuenciaValor > 0 ? rec.FrecuenciaValor : rec.FrecuenciaMinutos;
        return TimeSpan.FromMinutes(SupabaseService.ResolverMinutos(valor, rec.FrecuenciaUnidad));
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

        if (rec is null || !rec.Activo)
        {
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }
            return;
        }

        var intervalo = ObtenerIntervalo(rec);
        if (intervalo <= TimeSpan.Zero)
        {
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }
            return;
        }

        if (!onlyReSchedule && LeDebeAvisar(tipo, intervalo))
        {
            GuardarUltimo(tipo, DateTime.Now);
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }
            await MostrarAsync(id, titulo, mensaje);
        }
        else if (ObtenerUltimo(tipo) == DateTime.MinValue)
        {
            // Primera vez: fija el ancla para que tanto el aviso en pantalla
            // como la notificación del sistema salgan exactamente dentro de
            // "intervalo" (y que no se vaya desplazando en cada ciclo).
            GuardarUltimo(tipo, DateTime.Now);
        }

        ProgramarProximaNotificacion(id, tipo, intervalo, titulo, mensaje);
    }

    private static async Task EvaluarTipoTareasAsync(List<RecordatorioSalud> lista, bool onlyReSchedule)
    {
        var rec = lista.FirstOrDefault(r => string.Equals(r.Tipo, TipoTareas, StringComparison.OrdinalIgnoreCase));
        int id = IdsPorTipo[TipoTareas];

        if (rec is null || !rec.Activo)
        {
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }
            return;
        }

        var intervalo = ObtenerIntervalo(rec);
        if (intervalo <= TimeSpan.Zero)
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

        if (!onlyReSchedule && LeDebeAvisar(TipoTareas, intervalo))
        {
            GuardarUltimo(TipoTareas, DateTime.Now);
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }
            await MostrarAsync(id, titulo, mensaje);
        }
        else if (ObtenerUltimo(TipoTareas) == DateTime.MinValue)
        {
            GuardarUltimo(TipoTareas, DateTime.Now);
        }

        ProgramarProximaNotificacion(id, TipoTareas, intervalo, titulo, mensaje);
    }

    private static bool LeDebeAvisar(string tipo, TimeSpan intervalo)
    {
        var ultimo = ObtenerUltimo(tipo);
        if (ultimo == DateTime.MinValue)
            return false;

        return DateTime.Now >= ultimo.Add(intervalo);
    }

    private static void ProgramarProximaNotificacion(int id, string tipo, TimeSpan intervalo, string titulo, string mensaje)
    {
        try
        {
            var ultimo = ObtenerUltimo(tipo);
            var proximo = ultimo == DateTime.MinValue
                ? DateTime.Now.Add(intervalo)
                : ultimo.Add(intervalo);

            if (proximo <= DateTime.Now)
                proximo = DateTime.Now.Add(intervalo);

#if !WINDOWS
            // En Android la repetición la gestiona el propio sistema: la
            // notificación sigue llegando a la hora fijada cada ciclo aunque
            // la app esté cerrada o el teléfono bloqueado. El temporizador
            // in-app solo la re-arranca al cambiar la configuración.
            // En iOS/Mac la combinación "retraso + repetición por intervalo"
            // no está soportada, así que ahí solo se agenda la próxima.
            var schedule = new NotificationRequestSchedule
            {
                NotifyTime = new DateTimeOffset(proximo)
            };

#if ANDROID
            schedule.RepeatType = NotificationRepeat.TimeInterval;
            schedule.NotifyRepeatInterval = intervalo;
#endif

            var request = new NotificationRequest
            {
                NotificationId = id,
                Title = titulo,
                Description = mensaje,
                Schedule = schedule
            };

            LocalNotificationCenter.Current.Show(request);
#else
            // Windows: el Windows App SDK no soporta notificaciones
            // "programadas" (se muestran solo mientras la app está en
            // ejecución). El temporizador in-app dispara cada aviso a su
            // hora; aquí no se agenda nada para evitar avisos duplicados.
#endif
        }
        catch
        {
            // El agendamiento puede fallar temporalmente (permisos, plataforma):
            // el temporizador in-app reintenta en el siguiente ciclo.
        }
    }

    private static async Task MostrarAsync(int id, string titulo, string mensaje)
    {
        await AsegurarPermisoAsync();

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
#if WINDOWS
                MostrarNotificacionWindows(id, titulo, mensaje);
#else
                await LocalNotificationCenter.Current.Show(new NotificationRequest
                {
                    NotificationId = id,
                    Title = titulo,
                    Description = mensaje
                });
#endif
            }
            catch { }

            if (EnPrimerPlano && Shell.Current is not null)
            {
                try { await Shell.Current.DisplayAlert(titulo, mensaje, "OK"); }
                catch { }
            }
        });
    }

#if WINDOWS
    // Toast nativo de Windows. Requiere que el Windows App SDK esté
    // disponible y la app registrada (ver Platforms/Windows/App.xaml.cs).
    private static void MostrarNotificacionWindows(int id, string titulo, string mensaje)
    {
        var notificacion = new AppNotificationBuilder()
            .AddArgument("notificationId", id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .AddText(titulo)
            .AddText(mensaje)
            .BuildNotification();

        notificacion.Tag = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AppNotificationManager.Default.Show(notificacion);
    }
#endif

    private static DateTime ObtenerUltimo(string tipo)
    {
        var valor = Preferences.Default.Get(PrefUltimo + tipo, "");
        return DateTime.TryParse(valor, out var fecha) ? fecha : DateTime.MinValue;
    }

    private static void GuardarUltimo(string tipo, DateTime valor)
        => Preferences.Default.Set(PrefUltimo + tipo, valor.ToString("o"));
}