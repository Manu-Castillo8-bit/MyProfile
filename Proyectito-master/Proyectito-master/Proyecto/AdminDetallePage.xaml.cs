using System.Globalization;
using Proyecto.Services;

namespace Proyecto;

public partial class AdminDetallePage : ContentPage
{
    private enum Categoria { Tareas, Ahorro, Contrasenas, Salud }

    private readonly UsuarioAdmin _usuario;
    private AdminDatosUsuario? _datos;
    private Categoria _categoria = Categoria.Tareas;

    public AdminDetallePage(UsuarioAdmin usuario)
    {
        InitializeComponent();
        _usuario = usuario;
        BindingContext = _usuario;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        LblNombreDet.Text = _usuario.Nombre;
        LblCorreoDet.Text = _usuario.Correo;
        LblInicialesDet.Text = _usuario.Iniciales;
        AplicarRoleEnUI();

        await RecargarAsync();
    }

    private void AplicarRoleEnUI()
    {
        LblRolDet.Text = _usuario.DescripcionRol;
        BordeRol.BackgroundColor = _usuario.BadgeColor;
    }

    private async Task RecargarAsync()
    {
        MostrarIndicador(true);

        try
        {
            _datos = await AdminService.ObtenerDatosUsuarioAsync(_usuario.Id);
            ActualizarChips();
            PintarChatActiva();
            Renderizar();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            MostrarIndicador(false);
        }
    }

    private void ActualizarChips()
    {
        if (_datos is null) return;
        BtnCatTareas.Text = $"Tareas ({_datos.Tareas.Count})";
        BtnCatAhorro.Text = $"Ahorro ({_datos.Movimientos.Count})";
        BtnCatContrasenas.Text = $"Claves ({_datos.Contrasenas.Count})";
        BtnCatSalud.Text = $"Salud ({_datos.Recordatorios.Count})";
    }

    private void MostrarIndicador(bool visible)
    {
        Indicador.IsRunning = visible;
        Indicador.IsVisible = visible;
    }

    // ── NAVEGACIÓN ──

    private async void OnVolverClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    // ── CATEGORÍAS ──

    private void OnCatTareasClicked(object? sender, EventArgs e) { _categoria = Categoria.Tareas; PintarChatActiva(); Renderizar(); }
    private void OnCatAhorroClicked(object? sender, EventArgs e) { _categoria = Categoria.Ahorro; PintarChatActiva(); Renderizar(); }
    private void OnCatContrasenasClicked(object? sender, EventArgs e) { _categoria = Categoria.Contrasenas; PintarChatActiva(); Renderizar(); }
    private void OnCatSaludClicked(object? sender, EventArgs e) { _categoria = Categoria.Salud; PintarChatActiva(); Renderizar(); }

    private (Button boton, string etiqueta)[] _chats = Array.Empty<(Button, string)>();

    private void PintarChatActiva()
    {
        if (_chats.Length == 0)
        {
            _chats = new[]
            {
                (BtnCatTareas, "Tareas"),
                (BtnCatAhorro, "Ahorro"),
                (BtnCatContrasenas, "Claves"),
                (BtnCatSalud, "Salud")
            };
        }

        for (int i = 0; i < _chats.Length; i++)
        {
            bool activa = (int)_categoria == i;
            var boton = _chats[i].boton;
            boton.BackgroundColor = activa ? Color.FromArgb("#4FC3F7") : Color.FromArgb("#172033");
            boton.TextColor = activa ? Color.FromArgb("#0b1120") : Colors.White;
            boton.BorderColor = activa ? Color.FromArgb("#4FC3F7") : Color.FromArgb("#245b78");
            boton.BorderWidth = activa ? 0 : 1;
            boton.FontAttributes = activa ? FontAttributes.Bold : FontAttributes.None;
        }

        BtnAgregar.Text = _categoria switch
        {
            Categoria.Tareas => "＋ Agregar tarea",
            Categoria.Ahorro => "＋ Agregar movimiento",
            Categoria.Contrasenas => "＋ Agregar contraseña",
            _ => "＋ Agregar recordatorio"
        };
    }

    // ── RENDER DE LA CATEGORÍA ACTIVA ──

    private void Renderizar()
    {
        var stack = Contenido;
        stack.Children.Clear();

        if (_datos is null) return;

        switch (_categoria)
        {
            case Categoria.Tareas:
                if (_datos.Tareas.Count == 0) { stack.Children.Add(Vacio("Sin tareas para este usuario.")); return; }
                foreach (var t in _datos.Tareas)
                    stack.Children.Add(CrearFila(
                        t.Titulo,
                        $"{t.Estado} · {(t.FechaVencimiento.HasValue ? t.FechaVencimiento.Value.ToLocalTime().ToString("dd/MM/yyyy") : "sin fecha")}",
                        t.Descripcion,
                        () => EditarTareaAsync(t),
                        () => EliminarAsync(() => AdminService.EliminarTareaAsync(_usuario.Id, t.Id), $"la tarea \"{t.Titulo}\"", "Tarea eliminada.")));
                break;

            case Categoria.Ahorro:
                if (_datos.Movimientos.Count == 0) { stack.Children.Add(Vacio("Sin movimientos de ahorro.")); return; }
                foreach (var m in _datos.Movimientos)
                {
                    string signo = m.Tipo == "ingreso" ? "+" : "-";
                    stack.Children.Add(CrearFila(
                        $"{signo}${m.Monto.ToString("N2", CultureInfo.InvariantCulture)} · {TipoMovimiento(m.Tipo)}",
                        m.Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                        m.Descripcion,
                        () => EditarMovimientoAsync(m),
                        () => EliminarAsync(() => AdminService.EliminarMovimientoAsync(_usuario.Id, m.Id), $"el movimiento de ${m.Monto}", "Movimiento eliminado.")));
                }
                break;

            case Categoria.Contrasenas:
                if (_datos.Contrasenas.Count == 0) { stack.Children.Add(Vacio("Sin contraseñas guardadas.")); return; }
                foreach (var c in _datos.Contrasenas)
                    stack.Children.Add(CrearFila(
                        c.Sitio,
                        $"Usuario: {c.UsuarioCuenta}",
                        "Clave cifrada",
                        () => EditarContrasenaAsync(c),
                        () => EliminarAsync(() => AdminService.EliminarContrasenaAsync(_usuario.Id, c.Id), $"la contraseña de \"{c.Sitio}\"", "Contraseña eliminada.")));
                break;

            case Categoria.Salud:
                if (_datos.Recordatorios.Count == 0) { stack.Children.Add(Vacio("Sin recordatorios.")); return; }
                foreach (var r in _datos.Recordatorios)
                    stack.Children.Add(CrearFila(
                        DescripcionTipoRec(r.Tipo),
                        r.Activo ? $"Cada {r.FrecuenciaMinutos} min · Activo" : $"Cada {r.FrecuenciaMinutos} min · Inactivo",
                        "",
                        () => EditarRecordatorioAsync(r),
                        () => EliminarAsync(() => AdminService.EliminarRecordatorioAsync(_usuario.Id, r.Id), $"el recordatorio de {DescripcionTipoRec(r.Tipo)}", "Recordatorio eliminado.")));
                break;
        }
    }

    private static Label Vacio(string texto) => new()
    {
        Text = texto,
        TextColor = Color.FromArgb("#8E8E93"),
        FontSize = 13,
        HorizontalOptions = LayoutOptions.Center,
        Margin = new Thickness(0, 24, 0, 0)
    };

    private static View CrearFila(string principal, string sub, string detalle, Func<Task> onEditar, Func<Task> onEliminar)
    {
        var frame = new Frame
        {
            BackgroundColor = Color.FromArgb("#172033"),
            BorderColor = Color.FromArgb("#245b78"),
            CornerRadius = 12,
            Padding = new Thickness(14),
            HasShadow = false
        };

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };

        grid.Add(new Label { Text = principal, TextColor = Colors.White, FontSize = 15, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation }, 0, 0);
        grid.Add(new Label { Text = sub, TextColor = Color.FromArgb("#8E8E93"), FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation }, 0, 1);
        if (!string.IsNullOrEmpty(detalle))
            grid.Add(new Label { Text = detalle, TextColor = Color.FromArgb("#5F9EA0"), FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation }, 0, 2);

        var btnEditar = CrearBotonIcono("✎", Color.FromArgb("#1E3A5F"), Colors.White);
        btnEditar.Clicked += async (_, _) => await onEditar();
        var btnEliminar = CrearBotonIcono("🗑", Color.FromArgb("#3A1C1C"), Color.FromArgb("#FF6B5E"));
        btnEliminar.Clicked += async (_, _) => await onEliminar();

        var botones = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        botones.Children.Add(btnEditar);
        botones.Children.Add(btnEliminar);

        grid.Add(botones, 1, 0);
        Grid.SetRowSpan(botones, 3);

        frame.Content = grid;
        return frame;
    }

    private static Button CrearBotonIcono(string texto, Color fondo, Color color)
    {
        return new Button
        {
            Text = texto,
            BackgroundColor = fondo,
            TextColor = color,
            FontSize = 13,
            CornerRadius = 8,
            HeightRequest = 34,
            WidthRequest = 42,
            Padding = new Thickness(0),
            Margin = new Thickness(0)
        };
    }

    private static string TipoMovimiento(string tipo)
        => string.Equals(tipo, "ingreso", StringComparison.OrdinalIgnoreCase) ? "Ingreso" : "Gasto";

    private static string DescripcionTipoRec(string tipo)
    {
        return tipo switch
        {
            "agua" => "💧 Hidratación",
            "descanso" => "👁️ Descanso visual",
            "tareas" => "📝 Recordatorio de tareas",
            _ => tipo
        };
    }

    // ── AGREGAR NUEVO ──

    private async void OnAgregarClicked(object? sender, EventArgs e)
    {
        if (_datos is null) return;

        switch (_categoria)
        {
            case Categoria.Tareas: await AgregarTareaAsync(); break;
            case Categoria.Ahorro: await AgregarMovimientoAsync(); break;
            case Categoria.Contrasenas: await AgregarContrasenaAsync(); break;
            case Categoria.Salud: await AgregarRecordatorioAsync(); break;
        }
    }

    private async Task AgregarTareaAsync()
    {
        var titulo = await PedirTextoAsync("Título de la tarea", "");
        if (string.IsNullOrEmpty(titulo)) return;
        var descripcion = await PedirTextoAsync("Descripción", "");
        if (descripcion is null) return;
        var fecha = await PedirFechaAsync("Fecha límite (dd/mm/aaaa · vacío = sin fecha)", null);
        if (fecha is null) return;

        await EjecutarCambioAsync(
            () => AdminService.InsertarTareaAsync(_usuario.Id, titulo, descripcion, fecha, "Pendiente"),
            "Tarea creada.");
    }

    private async Task AgregarMovimientoAsync()
    {
        var monto = await PedirMontoAsync("Monto (por ejemplo 150.50)");
        if (monto is null) return;
        var tipo = await PedirTipoMovimientoAsync(null);
        if (tipo is null) return;
        var descripcion = await PedirTextoAsync("Descripción", "");
        if (descripcion is null) return;
        var fecha = await PedirFechaAsync("Fecha (dd/mm/aaaa hh:mm)", DateTime.Now);
        if (fecha is null) return;

        await EjecutarCambioAsync(
            () => AdminService.InsertarMovimientoAsync(_usuario.Id, monto.Value, tipo, descripcion, fecha),
            "Movimiento creado.");
    }

    private async Task AgregarContrasenaAsync()
    {
        var sitio = await PedirTextoAsync("Sitio o servicio", "");
        if (string.IsNullOrEmpty(sitio)) return;
        var usuarioCuenta = await PedirTextoAsync("Usuario de la cuenta", "");
        if (usuarioCuenta is null) return;
        var clave = await PedirTextoAsync("Clave", "");
        if (clave is null) return;

        await EjecutarCambioAsync(
            () => AdminService.InsertarContrasenaAsync(_usuario.Id, sitio, usuarioCuenta, clave),
            "Contraseña creada.");
    }

    private async Task AgregarRecordatorioAsync()
    {
        var tipo = await PedirTipoRecordatorioAsync(null);
        if (tipo is null) return;
        var frecuencia = await PedirFrecuenciaAsync("45");
        if (frecuencia is null) return;

        await EjecutarCambioAsync(
            () => AdminService.InsertarRecordatorioAsync(_usuario.Id, tipo, frecuencia.Value, true),
            "Recordatorio creado.");
    }

    // ── EDITAR EXISTENTE ──

    private async Task EditarTareaAsync(AdminTarea t)
    {
        var titulo = await PedirTextoAsync("Título", t.Titulo);
        if (titulo is null) return;
        var descripcion = await PedirTextoAsync("Descripción", t.Descripcion);
        if (descripcion is null) return;
        var estado = await DisplayActionSheet("Estado", "Cancelar", null, "Pendiente", "Completado");
        if (estado is null or "Cancelar") return;
        var fecha = await PedirFechaAsync("Fecha límite (dd/mm/aaaa · vacío = sin fecha)", t.FechaVencimiento?.ToLocalTime());
        if (fecha is null) return;

        await EjecutarCambioAsync(
            () => AdminService.ActualizarTareaAsync(_usuario.Id, t.Id, titulo, descripcion, fecha, estado),
            "Tarea actualizada.");
    }

    private async Task EditarMovimientoAsync(AdminMovimiento m)
    {
        var monto = await PedirMontoAsync(m.Monto.ToString("N2", CultureInfo.InvariantCulture));
        if (monto is null) return;
        var tipo = await PedirTipoMovimientoAsync(m.Tipo);
        if (tipo is null) return;
        var descripcion = await PedirTextoAsync("Descripción", m.Descripcion);
        if (descripcion is null) return;
        var fecha = await PedirFechaAsync("Fecha (dd/mm/aaaa hh:mm)", m.Fecha.ToLocalTime());
        if (fecha is null) return;

        await EjecutarCambioAsync(
            () => AdminService.ActualizarMovimientoAsync(_usuario.Id, m.Id, monto, tipo, descripcion, fecha),
            "Movimiento actualizado.");
    }

    private async Task EditarContrasenaAsync(AdminContrasena c)
    {
        var sitio = await PedirTextoAsync("Sitio o servicio", c.Sitio);
        if (sitio is null) return;
        var usuarioCuenta = await PedirTextoAsync("Usuario de la cuenta", c.UsuarioCuenta);
        if (usuarioCuenta is null) return;
        var clave = await PedirTextoAsync("Nueva clave (vacío = no cambiar)", "", teclado: null);
        if (clave is null) return;

        await EjecutarCambioAsync(
            () => AdminService.ActualizarContrasenaAsync(_usuario.Id, c.Id, sitio, usuarioCuenta, string.IsNullOrEmpty(clave) ? null : clave),
            "Contraseña actualizada.");
    }

    private async Task EditarRecordatorioAsync(AdminRecordatorio r)
    {
        var tipo = await PedirTipoRecordatorioAsync(r.Tipo);
        if (tipo is null) return;
        var frecuencia = await PedirFrecuenciaAsync(r.FrecuenciaMinutos.ToString());
        if (frecuencia is null) return;
        var activoRes = await DisplayActionSheet("Estado", "Cancelar", null, "Activo", "Inactivo");
        if (activoRes is null or "Cancelar") return;

        await EjecutarCambioAsync(
            () => AdminService.ActualizarRecordatorioAsync(_usuario.Id, r.Id, tipo, frecuencia, activoRes == "Activo"),
            "Recordatorio actualizado.");
    }

    // ── ACCIONES DE LA CUENTA ──

    private async void OnEditarNombreClicked(object? sender, EventArgs e)
    {
        var nombre = await PedirTextoAsync("Nombre de la cuenta", _usuario.Nombre);
        if (string.IsNullOrEmpty(nombre)) return;

        await EjecutarCambioAsyncNoRecarga(async () =>
        {
            await AdminService.ActualizarUsuarioAsync(_usuario.Id, nombre, null);
            _usuario.Nombre = nombre;
            LblNombreDet.Text = nombre;
            LblInicialesDet.Text = _usuario.Iniciales;
        }, "Nombre actualizado.");
    }

    private async void OnCambiarRolClicked(object? sender, EventArgs e)
    {
        string objetivo = _usuario.EsAdmin
            ? await DisplayActionSheet("Cambiar a rol", "Cancelar", null, new[] { "Usuario" })
            : await DisplayActionSheet("Cambiar a rol", "Cancelar", null, new[] { "Administrador" });

        if (string.IsNullOrEmpty(objetivo) || objetivo == "Cancelar") return;

        string nuevoRol = objetivo == "Administrador" ? "admin" : "usuario";
        if (nuevoRol == _usuario.Rol) return;

        bool confirmar = await DisplayAlert("Cambiar rol",
            _usuario.EsAdmin
                ? "¿Quitar el rol de administrador a esta cuenta?"
                : "¿Otorgar el rol de administrador a esta cuenta?",
            nuevoRol == "admin" ? "Hacer admin" : "Quitar admin", "Cancelar");

        if (!confirmar) return;

        await EjecutarCambioAsyncNoRecarga(async () =>
        {
            await AdminService.ActualizarUsuarioAsync(_usuario.Id, null, nuevoRol);
            _usuario.Rol = nuevoRol;
            AplicarRoleEnUI();
        }, "Rol actualizado.");
    }

    private async void OnEliminarCuentaClicked(object? sender, EventArgs e)
    {
        if (!await ConfirmarDobleAsync($"a TODA la cuenta de \"{_usuario.Nombre}\" (tareas, ahorro, contraseñas, salud y su acceso)"))
            return;

        try
        {
            MostrarIndicador(true);
            await AdminService.EliminarUsuarioAsync(_usuario.Id);
            MostrarIndicador(false);
            await DisplayAlert("Listo", "Cuenta eliminada.", "OK");
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            MostrarIndicador(false);
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    // ── ELIMINAR UN REGISTRO ──

    private async Task EliminarAsync(Func<Task> accion, string descripcion, string mensajeOK)
    {
        if (!await ConfirmarDobleAsync(descripcion)) return;
        await EjecutarCambioAsync(accion, mensajeOK);
    }

    // ── AUXILIARES ──

    private async Task<string?> PedirTextoAsync(string titulo, string actual, Keyboard? teclado = null)
        => await DisplayPromptAsync(titulo, "", "Aceptar", "Cancelar", null, 250, teclado, actual);

    private async Task<decimal?> PedirMontoAsync(string actual)
    {
        var r = await PedirTextoAsync("Monto", actual, Keyboard.Numeric);
        if (r is null) return null;
        if (decimal.TryParse(r.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ||
            decimal.TryParse(r.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out v))
            return v;
        await DisplayAlert("Dato inválido", "Ingresa un monto válido, por ejemplo 150.50", "OK");
        return null;
    }

    private async Task<int?> PedirFrecuenciaAsync(string actual)
    {
        var r = await PedirTextoAsync("Cada cuántos minutos", actual, Keyboard.Numeric);
        if (r is null) return null;
        if (int.TryParse(r.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ||
            int.TryParse(r.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out v))
            return v > 0 ? v : 1;
        await DisplayAlert("Dato inválido", "Ingresa un número entero en minutos.", "OK");
        return null;
    }

    private async Task<DateTime?> PedirFechaAsync(string titulo, DateTime? principal)
    {
        var r = await PedirTextoAsync(titulo, principal?.ToString("dd/MM/yyyy") ?? "sin fecha");
        if (r is null) return null;

        var texto = r.Trim();
        if (texto.Length == 0 || string.Equals(texto, "sin fecha", StringComparison.OrdinalIgnoreCase))
            return null;

        DateTime resultado;
        string[] formatos = { "dd/MM/yyyy HH:mm", "dd/MM/yyyy", "yyyy-MM-dd HH:mm", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(texto, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out resultado) ||
            DateTime.TryParseExact(texto, formatos, CultureInfo.CurrentCulture, DateTimeStyles.None, out resultado))
            return resultado;

        if (DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out resultado) ||
            DateTime.TryParse(texto, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out resultado))
            return resultado;

        await DisplayAlert("Dato inválido", "Usa formato dd/mm/aaaa o dd/mm/aaaa hh:mm, o déjalo vacío.", "OK");
        return null;
    }

    private async Task<string?> PedirTipoMovimientoAsync(string? actual)
    {
        var r = await DisplayActionSheet("Tipo de movimiento", "Cancelar", null, "Ingreso", "Gasto");
        if (r is null or "Cancelar") return null;
        return r == "Ingreso" ? "ingreso" : "gasto";
    }

    private async Task<string?> PedirTipoRecordatorioAsync(string? actual)
    {
        var r = await DisplayActionSheet("Tipo de recordatorio", "Cancelar", null, "Hidratación", "Descanso visual", "Tareas");
        if (r is null or "Cancelar") return null;
        return r switch
        {
            "Hidratación" => "agua",
            "Descanso visual" => "descanso",
            "Tareas" => "tareas",
            _ => actual
        };
    }

    private async Task<bool> ConfirmarDobleAsync(string entidad)
    {
        bool primera = await DisplayAlert("Eliminar", $"¿Eliminar {entidad}?", "Sí", "No");
        if (!primera)
            return false;

        return await DisplayAlert("Confirmación final",
            $"Esta acción NO se puede deshacer.\n\n¿Eliminar definitivamente {entidad}?",
            "Eliminar", "Cancelar");
    }

    private async Task EjecutarCambioAsync(Func<Task> accion, string mensajeOK)
    {
        try
        {
            await accion();
            await RecargarAsync();
            await DisplayAlert("Listo", mensajeOK, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async Task EjecutarCambioAsyncNoRecarga(Func<Task> accion, string mensajeOK)
    {
        try
        {
            MostrarIndicador(true);
            await accion();
            MostrarIndicador(false);
            await DisplayAlert("Listo", mensajeOK, "OK");
        }
        catch (Exception ex)
        {
            MostrarIndicador(false);
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }
}