using Android.App;
using Android.Runtime;

namespace LifeHub.Mobile;

[Application(AllowBackup = false)]   // the data key lives in the Keystore, which backups can't restore
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
