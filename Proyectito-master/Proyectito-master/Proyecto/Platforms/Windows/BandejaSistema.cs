using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Window = Microsoft.UI.Xaml.Window;
using MenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;
using MenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;
using MenuFlyoutSeparator = Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator;

namespace Proyecto;

// Mantiene la app viva en la bandeja del sistema cuando se cierra la ventana,
// para que los toasts de Windows (recordatorios) sigan saliendo con la ventana
// cerrada. Ademas registra el arranque automatico al iniciar sesion en Windows.
public static class BandejaSistema
{
    private const string ClaveAutoarranque =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NombreAutoarranque = "Proyecto";
    private const string ArgumentoSegundoPlano = "--background";

    private const string NombreMutex = "Proyecto.InstanciaUnica";
    private const string NombreEventoMostrar = "Proyecto.MostrarVentana";

    private static TaskbarIcon? _icono;
    private static Window? _ventana;
    private static AppWindow? _appWindow;
    private static bool _saliendo;
    private static bool _avisoMostrado;
    private static Mutex? _mutex;
    private static EventWaitHandle? _eventoMostrar;

    public static void Configurar(Window ventana)
    {
        _ventana = ventana;

        // Si ya hay otra instancia viva (por ejemplo, oculta en la bandeja),
        // se le pide que muestre su ventana y esta segunda se cierra.
        if (!EsPrimeraInstancia())
        {
            Salir();
            return;
        }

        // La ventana WinUI y su AppWindow: AppWindow.Closing permite cancelar el
        // cierre de forma fiable (la "X" oculta en vez de cerrar de verdad) y
        // AppWindow.Hide/Show gestiona la visibilidad sin romper el renderizado.
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(ventana);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            _appWindow.Closing += (_, e) =>
            {
                if (_saliendo)
                    return;

                e.Cancel = true;
                Ocultar();
                MostrarAviso();
            };
        }
        catch
        {
            // Respaldo si AppWindow no está disponible: interceptar "Closed".
            ventana.Closed += (_, e) =>
            {
                if (_saliendo)
                    return;

                e.Handled = true;
                Ocultar();
                MostrarAviso();
            };
        }

        CrearIcono();
        RegistrarAutoarranque();

        // Si el arranque fue automatico, empieza directamente oculta.
        if (EsArranqueAutomatico())
            Ocultar();
    }

    // Cierra la app de verdad (se usa desde el menu "Salir" de la bandeja).
    public static void Salir()
    {
        _saliendo = true;
        try { _icono?.Dispose(); } catch { }
        try { Microsoft.UI.Xaml.Application.Current.Exit(); } catch { }
    }

    private static void Mostrar()
    {
        if (_ventana is null)
            return;

        try
        {
            _appWindow?.Show();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_ventana);
            SetForegroundWindow(hwnd);
            _ventana.Activate();
        }
        catch { }
    }

    private static void Ocultar()
    {
        if (_ventana is null)
            return;

        try
        {
            _appWindow?.Hide();
        }
        catch { }
    }

    private static void CrearIcono()
    {
        try
        {
            var menu = new MenuFlyout();

            var abrir = new MenuFlyoutItem { Text = "Abrir Proyecto" };
            abrir.Click += (_, _) => Mostrar();
            menu.Items.Add(abrir);

            menu.Items.Add(new MenuFlyoutSeparator());

            var salir = new MenuFlyoutItem { Text = "Salir" };
            salir.Click += (_, _) => Salir();
            menu.Items.Add(salir);

            _icono = new TaskbarIcon
            {
                ToolTipText = "Proyecto - recordatorios activos",
                ContextFlyout = menu,
                Icon = ObtenerIcono(),
                LeftClickCommand = new ComandoAccion(Mostrar),
                DoubleClickCommand = new ComandoAccion(Mostrar)
            };
            // enablesEfficiencyMode=false: el "Efficiency Mode" que activa la
            // bandeja por defecto limita la CPU del proceso y deja la app
            // lenta/trabada (incluso no despierta los hilos rapidamente).
            _icono.ForceCreate(false);
        }
        catch
        {
            // Sin bandeja la app sigue funcionando mientras la ventana este abierta.
        }
    }

    private static System.Drawing.Icon? ObtenerIcono()
    {
        try
        {
            var ruta = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(ruta))
                return System.Drawing.Icon.ExtractAssociatedIcon(ruta);
        }
        catch { }

        return null;
    }

    private static void MostrarAviso()
    {
        if (_avisoMostrado || _icono is null)
            return;

        _avisoMostrado = true;

        try
        {
            _icono.ShowNotification(
                "Proyecto sigue activo",
                "Los recordatorios seguiran avisandote. Abre la app desde el icono de la bandeja.");
        }
        catch { }
    }

    // Registra la app en "Inicio" de Windows (HKCU) con el argumento que
    // indica que debe arrancar oculta en la bandeja.
    private static void RegistrarAutoarranque()
    {
        try
        {
            var ruta = Environment.ProcessPath;
            if (string.IsNullOrEmpty(ruta))
                return;

            using var clave = Registry.CurrentUser.OpenSubKey(ClaveAutoarranque, writable: true);
            if (clave is null)
                return;

            var valor = $"\"{ruta}\" {ArgumentoSegundoPlano}";
            if (clave.GetValue(NombreAutoarranque) as string != valor)
                clave.SetValue(NombreAutoarranque, valor);
        }
        catch { }
    }

    private static bool EsArranqueAutomatico()
        => Environment.GetCommandLineArgs()
            .Any(a => string.Equals(a, ArgumentoSegundoPlano, StringComparison.OrdinalIgnoreCase));

    // Detecta si esta es la primera instancia. Si ya habia una, avisa a esa
    // instancia (por un evento con nombre) para que muestre su ventana.
    private static bool EsPrimeraInstancia()
    {
        try
        {
            _mutex = new Mutex(true, NombreMutex, out bool primera);
            if (!primera)
            {
                try { EventWaitHandle.OpenExisting(NombreEventoMostrar).Set(); } catch { }
                return false;
            }

            _eventoMostrar = new EventWaitHandle(false, EventResetMode.AutoReset, NombreEventoMostrar);

            var hilo = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_eventoMostrar.WaitOne())
                            _ventana?.DispatcherQueue.TryEnqueue(Mostrar);
                    }
                    catch
                    {
                        return;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "Proyecto.MostrarVentana"
            };
            hilo.Start();

            return true;
        }
        catch
        {
            // Ante la duda, se comporta como instancia única.
            return true;
        }
    }

    private sealed class ComandoAccion : System.Windows.Input.ICommand
    {
        private readonly Action _accion;

        public ComandoAccion(Action accion) => _accion = accion;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _accion();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}