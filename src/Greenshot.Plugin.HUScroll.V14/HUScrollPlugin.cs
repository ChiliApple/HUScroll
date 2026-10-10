using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Greenshot.Base.Core;
using Greenshot.Base.Interfaces;
using Greenshot.Base.Interfaces.Plugin;
using Greenshot.Base.Threading;
using HUScroll.Core;
using HUScroll.Core.UI;

[assembly: AssemblyPluginIdentifier("HUScroll")]

namespace Greenshot.Plugin.HUScroll
{
    /// <summary>
    /// HUScroll for Greenshot 1.4: tray menu entry "Scroll-Capture", the result opens in the Greenshot editor.
    /// 1.4 plugins register in ConfigureServices (no I/O) and add their menu entries in StartAsync on the UI thread.
    /// </summary>
    public sealed class HUScrollPlugin : IGreenshotPlugin
    {
        private const string EditorDesignation = "Editor";
        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(typeof(HUScrollPlugin));

        private TrayMenuEntry _automaticEntry;
        private TrayMenuEntry _manualEntry;
        private bool _running;

        public string Name => "HUScroll";

        public void ConfigureServices(IPluginServices services)
        {
            // Nothing to register yet (no settings, no destinations)
        }

        public Task StartAsync(IServiceProvider services, CancellationToken cancellationToken) =>
            services.GetRequiredService<IUiDispatcher>().RunOnUiAsync(Start, cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) =>
            UiDispatcher.Current.RunOnUiAsync(() =>
            {
                RemoveEntry(ref _automaticEntry);
                RemoveEntry(ref _manualEntry);
            }, cancellationToken);

        public ValueTask DisposeAsync()
        {
            // The menu entry is removed in StopAsync
            return default;
        }

        private void Start()
        {
            // 1.4 shows the entries in the order they were added
            _automaticEntry = CreateEntry("Scroll-Capture automatisch (HUScroll)", ScrollCaptureMode.Automatic);
            _manualEntry = CreateEntry("Scroll-Capture manuell (HUScroll)", ScrollCaptureMode.Manual);
            Log.InfoFormat("HUScroll {0} started", typeof(HUScrollPlugin).Assembly.GetName().Version);
        }

        private TrayMenuEntry CreateEntry(string text, ScrollCaptureMode mode)
        {
            var entry = new TrayMenuEntry
            {
                Text = text,
                Image = HUScrollIcon.Create(16)
            };
            entry.Click += (sender, e) => OnMenuClick(mode);
            PluginUtils.AddToContextMenu(entry);
            return entry;
        }

        private static void RemoveEntry(ref TrayMenuEntry entry)
        {
            if (entry == null) return;
            PluginUtils.RemoveFromContextMenu(entry);
            entry.Dispose();
            entry = null;
        }

        private async void OnMenuClick(ScrollCaptureMode mode)
        {
            if (_running) return;
            _running = true;
            try
            {
                var session = new ScrollCaptureSession(log: message => Log.Info(message));
                var result = await session.RunAsync(mode);
                if (result?.Image == null) return;

                string summary = $"{result.Image.Width} × {result.Image.Height} px aus {result.Frames} Bildern – {result.StopReason}";
                Log.Info("Scroll capture: " + summary);

                var surface = SimpleServiceProvider.Current.GetInstance<Func<ISurface>>().Invoke();
                surface.Image = result.Image;
                surface.CaptureDetails = new CaptureDetails
                {
                    Title = $"HUScroll {DateTime.Now:yyyy-MM-dd HH-mm-ss}",
                    DateTime = DateTime.Now,
                    CaptureMode = CaptureMode.Region
                };
                surface.Modified = true;

                // Opens the editor in the background (1.4 exports asynchronously)
                DestinationHelper.StartExport(EditorDesignation, surface, true);
                if (!result.IsNormalEnd)
                {
                    // Only tell the user when something unexpected happened (Windows plays a sound with every notification)
                    Notify(summary);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Scroll capture failed", ex);
                MessageBox.Show("HUScroll: " + ex.Message, "HUScroll", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _running = false;
            }
        }

        private static void Notify(string message)
        {
            try
            {
                var notificationService = SimpleServiceProvider.Current.GetAllInstances<INotificationService>().LastOrDefault();
                notificationService?.ShowInfoMessage("HUScroll: " + message, TimeSpan.FromSeconds(6));
            }
            catch (Exception ex)
            {
                Log.Warn("Notification failed", ex);
            }
        }
    }
}
