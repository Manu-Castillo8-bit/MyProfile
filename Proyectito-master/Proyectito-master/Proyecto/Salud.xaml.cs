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
            TxtFrecAgua.Text = (agua?.FrecuenciaMinutos ?? 45).ToString();

            var descanso = _recordatorios.FirstOrDefault(r => r.Tipo == TipoDescanso);
            SwDescanso.IsToggled = descanso?.Activo ?? false;
            TxtFrecDescanso.Text = (descanso?.FrecuenciaMinutos ?? 20).ToString();

            var tareas = _recordatorios.FirstOrDefault(r => r.Tipo == TipoTareas);
            SwTareas.IsToggled = tareas?.Activo ?? false;
            TxtFrecTareas.Text = (tareas?.FrecuenciaMinutos ?? 60).ToString();

            RefrescarVista();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudieron cargar los datos: {ex.Message}", "OK");
        }
    }

    private void RefrescarVista()
    {
        int vasosHoy = ProgresoSalud.VasosHoy();
        double progreso = Math.Min(1.0, (double)vasosHoy / MetaVasos);
        LblVasos.Text = $"Vasos hoy: {vasosHoy} / {MetaVasos}";
        ProgAgua.Progress = progreso;
        LblPorcentajeAgua.Text = $"{(int)Math.Round(progreso * 100)}% de la meta diaria";

        LblDescansos.Text = $"Descansos registrados hoy: {ProgresoSalud.DescansosHoy()}";

        if (SwDescanso.IsToggled && int.TryParse(TxtFrecDescanso.Text, out int frecuencia) && frecuencia > 0)
            LblProximo.Text = $"Próximo descanso en {frecuencia} min";
        else
            LblProximo.Text = "Activa el recordatorio para ver el próximo descanso";

        if (SwTareas.IsToggled && int.TryParse(TxtFrecTareas.Text, out int frecuenciaTareas) && frecuenciaTareas > 0)
            LblEstadoTareas.Text = $"Aviso de pendientes cada {frecuenciaTareas} min";
        else
            LblEstadoTareas.Text = "Activa el recordatorio para recibir avisos";
    }

    private async void OnGuardarAguaClicked(object sender, EventArgs e)
    {
        await GuardarPreferencia(TipoAgua, TxtFrecAgua, SwAgua);
    }

    private async void OnGuardarDescansoClicked(object sender, EventArgs e)
    {
        await GuardarPreferencia(TipoDescanso, TxtFrecDescanso, SwDescanso);
    }

    private async void OnGuardarTareasClicked(object sender, EventArgs e)
    {
        await GuardarPreferencia(TipoTareas, TxtFrecTareas, SwTareas);
    }

    private async Task GuardarPreferencia(string tipo, Entry txtFrecuencia, Switch sw)
    {
        if (SupabaseService.UsuarioActual is null)
        {
            await DisplayAlert("Aviso", "Debes iniciar sesión.", "OK");
            return;
        }

        if (!int.TryParse(txtFrecuencia.Text?.Trim(), out int frecuencia) || frecuencia <= 0)
        {
            await DisplayAlert("Error", "La frecuencia debe ser un número mayor a 0 (en minutos).", "OK");
            return;
        }

        try
        {
            await SupabaseService.GuardarRecordatorioAsync(tipo, frecuencia, sw.IsToggled);

            if (sw.IsToggled)
                await RecordatorioScheduler.AsegurarPermisoAsync();

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