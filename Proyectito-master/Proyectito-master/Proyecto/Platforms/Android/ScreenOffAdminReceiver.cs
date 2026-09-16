using Android.App;
using Android.App.Admin;
using Android.Content;
using Android.Runtime;

namespace Proyecto;

[Android.Content.BroadcastReceiver(Exported = true, Permission = "android.permission.BIND_DEVICE_ADMIN")]
[Android.App.IntentFilter(new[] { "android.app.action.DEVICE_ADMIN_ENABLED" })]
[Android.App.MetaData("android.app.device_admin", Resource = "@xml/device_admin")]
public class ScreenOffAdminReceiver : DeviceAdminReceiver
{
}