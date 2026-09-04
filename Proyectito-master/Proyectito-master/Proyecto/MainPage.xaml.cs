using Proyecto.Services;

namespace Proyecto
{
    public partial class MainPage : ContentPage
    {
        private List<Tarea> _todasLasTareas = new();
        private bool _mostrandoCompletadas = false;
        private int? _editandoId = null;

        public MainPage()
        {
            InitializeComponent();
            LblDepuracion.Text = "Depuración: página construida";
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            LblDepuracion.Text = "Depuración: OnAppearing (sesión=" + (SupabaseService.UsuarioActual?.Id.ToString() ?? "null") + ")";
            await CargarTareasAsync();
        }

        private async Task CargarTareasAsync()
        {
            if (SupabaseService.UsuarioActual is null)
            {
                LblDepuracion.Text = "Depuración: SIN SESIÓN (UsuarioActual es null)";
                await DisplayAlert("Aviso", "Debes iniciar sesión primero.", "OK");
                MostrarTareas(new List<Tarea>()); // Limpia la vista si no hay usuario
                return;
            }

            try
            {
                LblDepuracion.Text = "Depuración: consultando tareas...";
                _todasLasTareas = await SupabaseService.ObtenerTareasAsync();
                LblDepuracion.Text = "Depuración: tareas cargadas = " + _todasLasTareas.Count;
                RefrescarVista();
            }
            catch (Exception ex)
            {
                LblDepuracion.Text = "Depuración: ERROR " + ex.Message;
                await DisplayAlert("Error", $"No se pudieron cargar las tareas: {ex.Message}", "OK");
            }
        }

        private void RefrescarVista()
        {
            var estadoFiltro = _mostrandoCompletadas ? "completada" : "pendiente";
            var tareasFiltradas = _todasLasTareas
                .Where(t => string.Equals(t.Estado, estadoFiltro, StringComparison.OrdinalIgnoreCase))
                .ToList();

            MostrarTareas(tareasFiltradas);
        }

        private void MostrarTareas(List<Tarea> lista)
        {
            ListaTareas.Children.Clear();

            if (lista == null || lista.Count == 0)
            {
                ListaTareas.Children.Add(new Label
                {
                    Text = "No hay tareas.",
                    TextColor = Color.FromArgb("#8E8E93"),
                    FontSize = 14,
                    HorizontalTextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 40, 0, 0)
                });
                return;
            }

            foreach (var t in lista)
            {
                var frame = new Frame
                {
                    BackgroundColor = Color.FromArgb("#1E1E1E"),
                    BorderColor = Color.FromArgb("#2C2C2C"),
                    CornerRadius = 14,
                    Padding = new Thickness(16),
                    HasShadow = false
                };

                // Solución al alto de las filas usando GridLength.Auto
                var grid = new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = GridLength.Auto }
                    },
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    ColumnSpacing = 10
                };

                var tituloLabel = new Label
                {
                    Text = t.Titulo,
                    TextColor = Colors.White,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold
                };

                var estadoString = _mostrandoCompletadas ? "Completada" : "Pendiente";

                var descripcionLabel = new Label
                {
                    Text = string.IsNullOrWhiteSpace(t.Descripcion) ? estadoString : t.Descripcion,
                    TextColor = Color.FromArgb("#8E8E93"),
                    FontSize = 13
                };

                var fechaLabel = new Label
                {
                    Text = t.FechaVencimiento.HasValue
                        ? $"Vence: {t.FechaVencimiento.Value.ToShortDateString()}"
                        : "Sin fecha de vencimiento",
                    TextColor = Color.FromArgb("#555555"),
                    FontSize = 12
                };

                var btnCompletar = new Button
                {
                    Text = _mostrandoCompletadas ? "Reabrir" : "Completar",
                    BackgroundColor = Color.FromArgb("#2C4A2C"),
                    TextColor = Colors.White,
                    FontSize = 12,
                    CornerRadius = 8,
                    HeightRequest = 34,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                btnCompletar.Clicked += async (s, e) => await OnCompletarClicked(t);

                var btnEliminar = new Button
                {
                    Text = "Eliminar",
                    BackgroundColor = Color.FromArgb("#3A1C1C"),
                    TextColor = Color.FromArgb("#FF3B30"),
                    FontSize = 12,
                    CornerRadius = 8,
                    HeightRequest = 34,
                    Padding = new Thickness(0)
                };
                btnEliminar.Clicked += async (s, e) => await OnEliminarClicked(t);

                var botonesStack = new VerticalStackLayout
                {
                    Children = { btnCompletar, btnEliminar },
                    VerticalOptions = LayoutOptions.Center
                };

                Grid.SetRow(tituloLabel, 0);
                Grid.SetColumn(tituloLabel, 0);

                Grid.SetRow(descripcionLabel, 1);
                Grid.SetColumn(descripcionLabel, 0);

                Grid.SetRow(fechaLabel, 2);
                Grid.SetColumn(fechaLabel, 0);

                Grid.SetRow(botonesStack, 0);
                Grid.SetColumn(botonesStack, 1);
                Grid.SetRowSpan(botonesStack, 3);

                grid.Children.Add(tituloLabel);
                grid.Children.Add(descripcionLabel);
                grid.Children.Add(fechaLabel);
                grid.Children.Add(botonesStack);

                frame.Content = grid;
                ListaTareas.Children.Add(frame);
            }
        }

        private async Task OnCompletarClicked(Tarea tarea)
        {
            try
            {
                var nuevoEstado = _mostrandoCompletadas ? "pendiente" : "completada";
                await SupabaseService.CambiarEstadoTareaAsync(tarea.IdTarea, nuevoEstado);
                await CargarTareasAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async Task OnEliminarClicked(Tarea tarea)
        {
            var confirmar = await DisplayAlert("Confirmar", $"Eliminar la tarea \"{tarea.Titulo}\"?", "Sí", "No");
            if (!confirmar) return;

            try
            {
                await SupabaseService.EliminarTareaAsync(tarea.IdTarea);
                await DisplayAlert("Listo", "Tarea eliminada.", "OK");
                await CargarTareasAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private void OnPendientesClicked(object sender, EventArgs e)
        {
            _mostrandoCompletadas = false;
            BtnPendientes.BackgroundColor = Colors.White;
            BtnPendientes.TextColor = Colors.Black;
            BtnCompletadas.BackgroundColor = Color.FromArgb("#1E1E1E");
            BtnCompletadas.TextColor = Color.FromArgb("#8E8E93");

            RefrescarVista();
        }

        private void OnCompletadasClicked(object sender, EventArgs e)
        {
            _mostrandoCompletadas = true;
            BtnCompletadas.BackgroundColor = Colors.White;
            BtnCompletadas.TextColor = Colors.Black;
            BtnPendientes.BackgroundColor = Color.FromArgb("#1E1E1E");
            BtnPendientes.TextColor = Color.FromArgb("#8E8E93");

            RefrescarVista();
        }

        private void OnAgregarClicked(object sender, EventArgs e)
        {
            _editandoId = null;
            LblFormTitulo.Text = "Nueva tarea";
            TxtTitulo.Text = "";
            TxtDescripcion.Text = "";
            FchVencimiento.Date = DateTime.Today;
            FormOverlay.IsVisible = true;
        }

        private void OnCancelarClicked(object sender, EventArgs e)
        {
            FormOverlay.IsVisible = false;
            _editandoId = null;
        }

        private async void OnGuardarClicked(object sender, EventArgs e)
        {
            var titulo = TxtTitulo.Text?.Trim() ?? "";
            var descripcion = TxtDescripcion.Text?.Trim() ?? "";
            var fecha = FchVencimiento.Date;

            if (string.IsNullOrWhiteSpace(titulo))
            {
                await DisplayAlert("Error", "El título es obligatorio.", "OK");
                return;
            }

            try
            {
                if (_editandoId.HasValue)
                {
                    await SupabaseService.ActualizarTareaAsync(_editandoId.Value, titulo, descripcion, fecha, _mostrandoCompletadas ? "completada" : "pendiente");
                    await DisplayAlert("Listo", "Tarea actualizada.", "OK");
                }
                else
                {
                    await SupabaseService.CrearTareaAsync(titulo, descripcion, fecha, _mostrandoCompletadas ? "completada" : "pendiente");
                    await DisplayAlert("Listo", "Tarea creada.", "OK");
                }

                FormOverlay.IsVisible = false;
                _editandoId = null;
                await CargarTareasAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }
    }
}