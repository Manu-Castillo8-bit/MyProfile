using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AndroidOption;
using Plugin.LocalNotification.EventArgs;

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

    // IDs de los botones de la notificación de "suspender pantalla".
    public const int AccionSuspender = 100;
    public const int AccionCancelar = 101;

    private const string PrefUltimo = "rec_ultimo_";
    private const string PrefSuspenderPantalla = "rec_suspender_pantalla";
    private const int AppIdBase = 1100;
    private static readonly Dictionary<string, int> IdsPorTipo = new()
    {
        [TipoAgua] = AppIdBase + 1,
        [TipoDescanso] = AppIdBase + 2,
        [TipoTareas] = AppIdBase + 3
    };

    // Para saber si la app está en primer plano y decidir el aviso en pantalla.
    public static bool EnPrimerPlano { get; set; } = true;

    // Preferencia local de "descanso visual": si el aviso de descanso debe
    // además apagar la pantalla (Windows → suspende la PC; Android → pantalla
    // se apaga/bloquea). Se guarda solo en el dispositivo, como ProgresoSalud.
    public static bool SuspenderPantalla
    {
        get => Preferences.Default.Get(PrefSuspenderPantalla, false);
        set => Preferences.Default.Set(PrefSuspenderPantalla, value);
    }

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
        CancelarSuspension();

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

    // Atiende los botones de la notificación de suspensión. Se registra en
    // MauiProgram para que funcione incluso si la app estaba cerrada.
    public static void OnAccionNotificacion(NotificationActionEventArgs e)
    {
        if (e.IsDismissed)
            return;

        EjecutarAccion(e.ActionId);
    }

    private static DateTime _ultimaSuspension = DateTime.MinValue;

    // "Suspender ahora" apaga la pantalla de inmediato; "Cancelar" no hace
    // nada: el aviso volverá a dispararse en el siguiente intervalo.
    public static void EjecutarAccion(int actionId)
    {
        if (actionId == AccionSuspender)
        {
            // Evita apagar dos veces si la acción llega por el evento y por
            // el arranque en frío a la vez.
            if ((DateTime.Now - _ultimaSuspension).TotalSeconds < 2)
                return;

            _ultimaSuspension = DateTime.Now;
            SuspenderSistema();
        }
        else if (actionId == AccionCancelar)
        {
            CancelarSuspension();
        }
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

        // El descanso visual con "suspender pantalla" usa una notificación
        // interactiva (botones Suspender / Cancelar) en lugar del aviso normal.
        bool esSuspension = string.Equals(tipo, TipoDescanso, StringComparison.OrdinalIgnoreCase)
            && SuspenderPantalla;

        if (!onlyReSchedule && LeDebeAvisar(tipo, intervalo))
        {
            GuardarUltimo(tipo, DateTime.Now);
            try { LocalNotificationCenter.Current.Cancel(id); } catch { }

            if (esSuspension)
                await MostrarSuspensionAsync(id, titulo);
            else
                await MostrarAsync(id, titulo, mensaje);
        }
        else if (ObtenerUltimo(tipo) == DateTime.MinValue)
        {
            // Primera vez: fija el ancla para que tanto el aviso en pantalla
            // como la notificación del sistema salgan exactamente dentro de
            // "intervalo" (y que no se vaya desplazando en cada ciclo).
            GuardarUltimo(tipo, DateTime.Now);
        }

        ProgramarProximaNotificacion(id, tipo, intervalo, titulo, mensaje, esSuspension);
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
            .Count(t => !EstadoTarea.EsCompletado(t.Estado));

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

        ProgramarProximaNotificacion(id, TipoTareas, intervalo, titulo, mensaje, false);
    }

    private static bool LeDebeAvisar(string tipo, TimeSpan intervalo)
    {
        var ultimo = ObtenerUltimo(tipo);
        if (ultimo == DateTime.MinValue)
            return false;

        return DateTime.Now >= ultimo.Add(intervalo);
    }

    private static void ProgramarProximaNotificacion(int id, string tipo, TimeSpan intervalo, string titulo, string mensaje, bool esSuspension)
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

            request.Android.Priority = AndroidPriority.High;
            request.Android.ChannelId = "recordatorios";

            if (esSuspension)
            {
                // Adjunta los botones "Suspender ahora" / "Cancelar" a la
                // notificación que dispara el sistema con la app cerrada.
                request.CategoryType = NotificationCategoryType.Status;
            }

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
                    Description = mensaje,
                    Android =
                    {
                        Priority = AndroidPriority.High,
                        ChannelId = "recordatorios"
                    }
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

    // Notificación de descanso visual con dos botones: "Suspender ahora" apaga
    // la pantalla de inmediato y "Cancelar" la descarta hasta el próximo aviso.
    private static async Task MostrarSuspensionAsync(int id, string titulo)
    {
        await AsegurarPermisoAsync();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
#if WINDOWS
                MostrarSuspensionWindows(id, titulo,
                    "Es hora de descansar la vista. ¿Apagar la pantalla ahora?");
#else
                LocalNotificationCenter.Current.Show(new NotificationRequest
                {
                    NotificationId = id,
                    Title = titulo,
                    Description = "Es hora de descansar la vista. ¿Apagar la pantalla ahora?",
                    CategoryType = NotificationCategoryType.Status,
                    Android =
                    {
                        Priority = AndroidPriority.High,
                        ChannelId = "recordatorios"
                    }
                });
#endif
            }
            catch { }
        });
    }

    // "Cancelar" en la notificación: no se apaga la pantalla ahora. El aviso
    // volverá a dispararse con el siguiente intervalo configurado.
    public static void CancelarSuspension()
    {
        try { LocalNotificationCenter.Current.Cancel(IdsPorTipo[TipoDescanso]); } catch { }
    }

    // Apaga/suspende la pantalla según la plataforma:
    //  - Windows: suspende toda la PC (SetSuspendState).
    //  - Android: apaga y bloquea solo la pantalla (requiere administrador).
    private static void SuspenderSistema()
    {
#if WINDOWS
        try
        {
            HabilitarPrivilegioSuspension();
            SetSuspendState(false, false, false);
        }
        catch { }
#elif ANDROID
        try
        {
            var contexto = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var admin = new Android.Content.ComponentName(contexto,
                Java.Lang.Class.FromType(typeof(Proyecto.ScreenOffAdminReceiver)));
            var dpm = (Android.App.Admin.DevicePolicyManager)contexto
                .GetSystemService(Android.Content.Context.DevicePolicyService);
            if (dpm.IsAdminActive(admin))
                dpm.LockNow();
        }
        catch { }
#endif
    }

    // ¿La app ya puede apagar la pantalla en Android (admin de dispositivo)?
    public static bool AdminDispositivoActivo
    {
        get
        {
#if ANDROID
            try
            {
                var contexto = Microsoft.Maui.ApplicationModel.Platform.AppContext;
                var admin = new Android.Content.ComponentName(contexto,
                    Java.Lang.Class.FromType(typeof(Proyecto.ScreenOffAdminReceiver)));
                var dpm = (Android.App.Admin.DevicePolicyManager)contexto
                    .GetSystemService(Android.Content.Context.DevicePolicyService);
                return dpm.IsAdminActive(admin);
            }
            catch { return false; }
#else
            return true;
#endif
        }
    }

    // Abre el panel de Android para activar la app como administrador del dispositivo.
    public static void SolicitarActivarAdminDispositivo()
    {
#if ANDROID
        try
        {
            // Se usa la Activity activa y no el AppContext para que el panel de
            // Ajustes no se cierre al abrirlo desde un contexto sin ventana.
            var actividad = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
                ?? Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var contexto = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var admin = new Android.Content.ComponentName(contexto,
                Java.Lang.Class.FromType(typeof(Proyecto.ScreenOffAdminReceiver)));
            var intent = new Android.Content.Intent(Android.App.Admin.DevicePolicyManager.ActionAddDeviceAdmin);
            intent.PutExtra(Android.App.Admin.DevicePolicyManager.ExtraDeviceAdmin, admin);
            intent.PutExtra(Android.App.Admin.DevicePolicyManager.ExtraAddExplanation,
                "Permite apagar la pantalla durante tus descansos visuales.");
            if (actividad is Android.App.Activity activity)
                activity.StartActivity(intent);
            else
            {
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                contexto.StartActivity(intent);
            }
        }
        catch { }
#endif
    }

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("powrprof.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool hibernate,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool forceCritical,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool disableWakeEvent);

    // La suspensión necesita el privilegio SeShutdownPrivilege en el token del proceso.
    private static void HabilitarPrivilegioSuspension()
    {
        const uint TokenQuery = 0x0008;
        const uint TokenAdjustPrivileges = 0x0020;
        const uint SePrivilegeEnabled = 0x00000002;
        const string SeShutdown = "SeShutdownPrivilege";

        if (!OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle,
                TokenQuery | TokenAdjustPrivileges, out var token))
            return;

        try
        {
            if (!LookupPrivilegeValue(null, SeShutdown, out var luid))
                return;

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SePrivilegeEnabled
            };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(
        string? lpSystemName,
        string lpName,
        out LUID lpLuid);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }
#endif

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

    // Toast de Windows con los botones "Suspender ahora" y "Cancelar".
    // La pulsación se atiende en Platforms/Windows/App.xaml.cs.
    private static void MostrarSuspensionWindows(int id, string titulo, string mensaje)
    {
        var notificacion = new AppNotificationBuilder()
            .AddArgument("notificationId", id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .AddText(titulo)
            .AddText(mensaje)
            .AddButton(new AppNotificationButton("Suspender ahora")
                .AddArgument("accion", AccionSuspender.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .AddButton(new AppNotificationButton("Cancelar")
                .AddArgument("accion", AccionCancelar.ToString(System.Globalization.CultureInfo.InvariantCulture)))
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