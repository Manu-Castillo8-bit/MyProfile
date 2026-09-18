using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AndroidOption;
using Plugin.LocalNotification.Core.Models.AppleOption;
using Proyecto.Services;

namespace Proyecto
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                })
                .UseLocalNotification(config =>
                {
                    config.AddAndroid(android =>
                    {
                        android.AddChannel(new AndroidNotificationChannelRequest
                        {
                            Id = "recordatorios",
                            Name = "Recordatorios",
                            Description = "Avisos de tareas, hidratación y descanso visual",
                            Importance = AndroidImportance.High
                        });
                    });

                    // Categoría con los dos botones de la notificación de
                    // "suspender pantalla": suspender ahora o cancelar.
                    config.AddCategory(new NotificationCategory(NotificationCategoryType.Status)
                    {
                        ActionList = new HashSet<NotificationAction>
                        {
                            new NotificationAction(RecordatorioScheduler.AccionSuspender)
                            {
                                Title = "Suspender ahora",
                                // true hace que el botón funcione aunque la app
                                // esté cerrada: Android la abre para ejecutarlo.
                                Android = { LaunchAppWhenTapped = true },
                                Apple = { Action = AppleActionType.Foreground }
                            },
                            new NotificationAction(RecordatorioScheduler.AccionCancelar)
                            {
                                Title = "Cancelar",
                                Android = { LaunchAppWhenTapped = true },
                                Apple = { Action = AppleActionType.Foreground }
                            }
                        }
                    });
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            var app = builder.Build();

            // Se suscribe aquí (y no en una página) para que el botón de la
            // notificación se atienda aunque la app se inicie desde cero.
            try
            {
                LocalNotificationCenter.Current.NotificationActionTapped +=
                    RecordatorioScheduler.OnAccionNotificacion;

                // Si la app acaba de iniciarse al pulsar un botón de la
                // notificación, la acción llega por aquí.
                var inicio = LocalNotificationCenter.LaunchNotificationDetails;
                if (inicio?.DidNotificationLaunchApp == true && inicio.ActionId is int accion && accion != 0)
                    RecordatorioScheduler.EjecutarAccion(accion);
            }
            catch
            {
                // Si la plataforma no soporta acciones, se ignora.
            }

            return app;
        }
    }
}
