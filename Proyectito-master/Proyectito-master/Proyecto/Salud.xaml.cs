using Proyecto.Services;

namespace Proyecto;

public partial class Salud : ContentPage
{
    private const int MetaVasos = 8;
    private const string TipoAgua = RecordatorioScheduler.TipoAgua;
    private const string TipoDescanso = RecordatorioScheduler.TipoDescanso;
    private const string TipoTareas = RecordatorioScheduler.TipoTareas;
    private List<RecordatorioSalud> _recordatorios = new();

	public Salud()
	{
		InitializeComponent();
	}

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarDatosAsync();
    }

    private async Task CargarDatosAsync()
    {
        if (SupabaseService.UsuarioActual is null)
        {
            await DisplayAlert("Aviso", "Debes iniciar sesión primero.", "OK");
            return;
        }

        try
        {
            _recordatorios = await SupabaseService.ObtenerRecordatoriosAsync();

            var agua = _recordatorios.FirstOrDefault(r => r.Tipo == TipoAgua);
            SwAgua.IsToggled = agua?.Activo ?? false;
            ConfigurarFrecuenciaUx(TxtFrecAgua, PckUnidadAgua, agua, "45");

            var descanso = _recordatorios.FirstOrDefault(r => r.Tipo == TipoDescanso);
            SwDescanso.IsToggled = descanso?.Activo ?? false;
            ConfigurarFrecuenciaUx(TxtFrecDescanso, PckUnidadDescanso, descanso, "20");

            var tareas = _recordatorios.FirstOrDefault(r => r.Tipo == TipoTareas);
            SwTareas.IsToggled = tareas?.Activo ?? false;
            ConfigurarFrecuenciaUx(TxtFrecTareas, PckUnidadTareas, tareas, "60");

            RefrescarVista();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudieron cargar los datos: {ex.Message}", "OK");
        }
    }

    // Rellena cantidad y unidad; si el dato es antiguo (solo minutos) elige la unidad más natural.
    private void ConfigurarFrecuenciaUx(Entry txt, Picker pck, RecordatorioSalud? rec, string defecto)
    {
        if (rec is null)
        {
            txt.Text = defecto;
            pck.SelectedIndex = 0;
            return;
        }

        if (rec.FrecuenciaValor > 0)
        {
            txt.Text = rec.FrecuenciaValor.ToString();
            pck.SelectedIndex = IndiceUnidad(rec.FrecuenciaUnidad);
        }
        else
        {
            int m = Math.Max(rec.FrecuenciaMinutos, 1);
            if (m % (24 * 60) == 0)
            {
                txt.Text = (m / (24 * 60)).ToString();
                pck.SelectedIndex = 2;
            }
            else if (m % 60 == 0)
            {
                txt.Text = (m / 60).ToString();
                pck.SelectedIndex = 1;
            }
            else
            {
                txt.Text = m.ToString();
                pck.SelectedIndex = 0;
            }
        }
    }

    private static int IndiceUnidad(string unidad) => unidad.Equals("h", StringComparison.OrdinalIgnoreCase) ? 1
        : (unidad.Equals("d", StringComparison.OrdinalIgnoreCase) ? 2 : 0);

    private static string UnidadDeIndice(int indice) => indice switch
    {
        1 => "h",
        2 => "d",
        _ => "min"
    };

    private static string LeyendaUnidad(int indice, int valor)
    {
        return indice switch
        {
            1 => valor == 1 ? "hora" : "horas",
            2 => valor == 1 ? "día" : "días",
            _ => valor == 1 ? "minuto" : "minutos"
        };
    }

    private static string TextoCadencia(bool activo, Entry txt, Picker pck, string mensajeInactivo)
    {
        if (activo && int.TryParse(txt.Text?.Trim(), out int valor) && valor > 0)
            return $"Cada {valor} {LeyendaUnidad(pck.SelectedIndex, valor)}";

        return mensajeInactivo;
    }

    private void RefrescarVista()
    {
        int vasosHoy = ProgresoSalud.VasosHoy();
        double progreso = Math.Min(1.0, (double)vasosHoy / MetaVasos);
        LblVasos.Text = $"Vasos hoy: {vasosHoy} / {MetaVasos}";
        ProgAgua.Progress = progreso;
        LblPorcentajeAgua.Text = $"{(int)Math.Round(progreso * 100)}% de la meta diaria";

        LblDescansos.Text = $"Descansos registrados hoy: {ProgresoSalud.DescansosHoy()}";

        LblProximo.Text = TextoCadencia(SwDescanso.IsToggled, TxtFrecDescanso, PckUnidadDescanso,
            "Activa el recordatorio para ver el próximo descanso");

        LblEstadoTareas.Text = TextoCadencia(SwTareas.IsToggled, TxtFrecTareas, PckUnidadTareas,
            "Activa el recordatorio para recibir avisos");
    }

    private async void OnGuardarAguaClicked(object sender, EventArgs e)
    {
        await GuardarPreferencia(TipoAgua, TxtFrecAgua, PckUnidadAgua, SwAgua);
    }

    private async void OnGuardarDescansoClicked(object sender, EventArgs e)
    {
        await GuardarPreferencia(TipoDescanso, TxtFrecDescanso, PckUnidadDescanso, SwDescanso);
    }

    private async void OnGuardarTareasClicked(object sender, EventArgs e)
    {
        await GuardarPreferencia(TipoTareas, TxtFrecTareas, PckUnidadTareas, SwTareas);
    }

    private async Task GuardarPreferencia(string tipo, Entry txtFrecuencia, Picker picker, Switch sw)
    {
        if (SupabaseService.UsuarioActual is null)
        {
            await DisplayAlert("Aviso", "Debes iniciar sesión.", "OK");
            return;
        }

        if (!int.TryParse(txtFrecuencia.Text?.Trim(), out int valor) || valor <= 0)
        {
            await DisplayAlert("Error", "La cantidad debe ser un número mayor a 0.", "OK");
            return;
        }

        var unidad = UnidadDeIndice(picker.SelectedIndex);

        try
        {
            await SupabaseService.GuardarRecordatorioAsync(tipo, valor, unidad, sw.IsToggled);

            if (sw.IsToggled)
            {
                await RecordatorioScheduler.AsegurarPermisoAsync();
                RecordatorioScheduler.AsegurarAlarmasExactas();
            }

            await RecordatorioScheduler.RefrescarAsync();
            await DisplayAlert("Listo", "Preferencias guardadas.", "OK");
            await CargarDatosAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudieron guardar: {ex.Message}", "OK");
        }
    }

    private void OnMasVasoClicked(object sender, EventArgs e)
    {
        ProgresoSalud.SumarVaso(1);
        RefrescarVista();
    }

    private void OnMenosVasoClicked(object sender, EventArgs e)
    {
        ProgresoSalud.SumarVaso(-1);
        RefrescarVista();
    }

    private void OnMasDescansoClicked(object sender, EventArgs e)
    {
        ProgresoSalud.SumarDescanso(1);
        RefrescarVista();
    }

    private void OnMenosDescansoClicked(object sender, EventArgs e)
    {
        ProgresoSalud.SumarDescanso(-1);
        RefrescarVista();
    }

    private void OnRecordatorioToggled(object sender, ToggledEventArgs e)
    {
        RefrescarVista();
    }
}