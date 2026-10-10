/*
 * HUScroll - Scroll-Capture fuer Greenshot
 * Copyright (C) 2026 ChiliApple
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 *
 * Name and HU logo: no trademark rights are granted (GPLv3 section 7 e), see Assets/LICENSE-LOGO.txt
 */

using System;
using System.Linq;
using System.Windows.Forms;
using Greenshot.Base.Core;
using Greenshot.Base.Interfaces;
using Greenshot.Base.Interfaces.Plugin;
using HUScroll.Core;
using HUScroll.Core.UI;

[assembly: AssemblyPluginIdentifier("HUScroll")]

namespace Greenshot.Plugin.HUScroll
{
    /// <summary>
    /// HUScroll for Greenshot 1.3: tray menu entry "Scroll-Capture", the result opens in the Greenshot editor.
    /// </summary>
    public sealed class HUScrollPlugin : IGreenshotPlugin
    {
        private const string EditorDesignation = "Editor";
        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(typeof(HUScrollPlugin));

        private ToolStripMenuItem _automaticItem;
        private ToolStripMenuItem _manualItem;
        private bool _running;

        public string Name => "HUScroll";

        public bool IsConfigurable => false;

        public bool Initialize()
        {
            _automaticItem = CreateMenuItem("Scroll-Capture automatisch (HUScroll)",
                "Fenster oder Bereich wählen – HUScroll scrollt selbst und setzt alles zu einem Bild zusammen", ScrollCaptureMode.Automatic);
            _manualItem = CreateMenuItem("Scroll-Capture manuell (HUScroll)",
                "Rahmen wählen – du scrollst selbst, HUScroll nimmt laufend auf (Esc = fertig)", ScrollCaptureMode.Manual);
            // AddToContextMenu inserts at the same position, so add in reverse order
            PluginUtils.AddToContextMenu(_manualItem);
            PluginUtils.AddToContextMenu(_automaticItem);
            Log.InfoFormat("HUScroll {0} initialized", typeof(HUScrollPlugin).Assembly.GetName().Version);
            return true;
        }

        public void Shutdown()
        {
            RemoveMenuItem(ref _automaticItem);
            RemoveMenuItem(ref _manualItem);
        }

        private ToolStripMenuItem CreateMenuItem(string text, string toolTip, ScrollCaptureMode mode)
        {
            var item = new ToolStripMenuItem
            {
                Text = text,
                Image = HUScrollIcon.Create(16),
                ToolTipText = toolTip,
                Tag = mode
            };
            item.Click += OnMenuClick;
            return item;
        }

        private void RemoveMenuItem(ref ToolStripMenuItem item)
        {
            if (item == null) return;
            item.Click -= OnMenuClick;
            item.Owner?.Items.Remove(item);
            item.Image?.Dispose();
            item.Dispose();
            item = null;
        }

        public void Configure()
        {
            // Nothing to configure yet
        }

        public void Dispose()
        {
            Shutdown();
        }

        private async void OnMenuClick(object sender, EventArgs e)
        {
            if (_running) return;
            _running = true;
            try
            {
                var mode = (sender as ToolStripItem)?.Tag is ScrollCaptureMode tagged ? tagged : ScrollCaptureMode.Automatic;
                var session = new ScrollCaptureSession(log: message => Log.Info(message));
                var result = await session.RunAsync(mode);
                if (result?.Image == null) return;

                string summary = $"{result.Image.Width} × {result.Image.Height} px aus {result.Frames} Bildern – {result.StopReason}";
                Log.Info("Scroll capture: " + summary);

                var surface = SimpleServiceProvider.Current.GetInstance<Func<ISurface>>().Invoke();
                surface.Image = result.Image;
                var captureDetails = new CaptureDetails
                {
                    Title = $"HUScroll {DateTime.Now:yyyy-MM-dd HH-mm-ss}",
                    DateTime = DateTime.Now,
                    CaptureMode = CaptureMode.Region
                };
                surface.CaptureDetails = captureDetails;
                surface.Modified = true;

                var editor = DestinationHelper.GetDestination(EditorDesignation);
                var exportInformation = editor != null && editor.IsActive
                    ? DestinationHelper.ExportCapture(true, EditorDesignation, surface, captureDetails)
                    : null;
                if (exportInformation == null || !exportInformation.ExportMade)
                {
                    // Don't lose the capture: clipboard instead
                    Log.Warn("The Greenshot editor did not open the scroll capture, copying it to the clipboard");
                    Clipboard.SetImage(result.Image);
                    surface.Dispose();
                    Notify("Der Greenshot-Editor ist nicht verfügbar – das Bild liegt in der Zwischenablage. " + summary);
                    return;
                }

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
                // Several services can be registered (tray balloon, Windows toast), GetInstance would throw then
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
