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
using System.Threading;
using System.Threading.Tasks;
using Greenshot.Base.Core;
using Greenshot.Base.Interfaces;
using Greenshot.Base.Recipes;
using Greenshot.Base.Recipes.Contracts;
using Greenshot.Base.Recipes.Pipeline;
using HUScroll.Core;

namespace Greenshot.Plugin.HUScroll.Recipes
{
    /// <summary>
    /// Recipe step (Greenshot 1.4): runs a scroll capture and hands the stitched image to the flow as the capture,
    /// so it can be combined with every other step (editor, save, clipboard, upload ...) and started by any trigger, e.g. a hotkey.
    /// </summary>
    [StepInfo(StepType, "Scroll-Capture (HUScroll)",
        "Selects a window or an area, scrolls it (automatically or by the user) and stitches everything into one tall image.", "Acquisition")]
    [StepPayload(RawCapture = PayloadRequirement.Created)]
    [StepParameter("Mode", ContractDataType.Enum, DefaultValue = "Automatic", SupportsExpressions = false,
        Description = "Automatic: HUScroll scrolls by itself; Manual: the user scrolls, Esc ends", AllowedValues = new[] { "Automatic", "Manual" })]
    [StepOutputVariable("HUScroll.Frames", ContractDataType.Integer, "Number of stitched frames")]
    [StepOutputVariable("HUScroll.StopReason", ContractDataType.String, "Why the capture ended")]
    public sealed class HUScrollStep : ICaptureStep
    {
        public const string StepType = "HUScroll";

        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(typeof(HUScrollStep));

        public HUScrollStep(RecipeNodeConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Name = config.Name ?? "HUScrollStep";
        }

        public string Name { get; }

        public RecipeNodeConfig Config { get; }

        public async Task ExecuteAsync(CaptureFlowContext context, CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            string modeText = Config.GetParameter("Mode", "Automatic");
            var mode = string.Equals(modeText, "Manual", StringComparison.OrdinalIgnoreCase) ? ScrollCaptureMode.Manual : ScrollCaptureMode.Automatic;
            context.LogStep($"HUScroll: scroll capture ({mode})");

            // A held hotkey repeats: Greenshot then starts the recipe a second time while the first capture is still running
            if (ScrollCaptureSession.IsActive)
            {
                context.Abort("HUScroll: a scroll capture is already running");
                return;
            }

            // The selection overlay and the status windows are WinForms windows: run the whole session on the UI thread
            var session = new ScrollCaptureSession(log: message => Log.Info(message));
            var result = await context.Ui.InvokeAsync(() => session.RunAsync(mode), cancellationToken).ConfigureAwait(false);
            if (result?.Image == null)
            {
                context.Abort("HUScroll: selection cancelled");
                return;
            }

            var capture = new Capture(result.Image)
            {
                CaptureDetails = new CaptureDetails
                {
                    Title = $"HUScroll {DateTime.Now:yyyy-MM-dd HH-mm-ss}",
                    DateTime = DateTime.Now,
                    CaptureMode = CaptureMode.Region
                }
            };
            // Greenshot puts a full screen source in front of every hotkey recipe without a "Source" step (the type name is fixed),
            // that capture is replaced here and has to be released
            var previousPayload = context.Payload;
            context.Payload = new CapturePayload(capture);
            previousPayload?.Dispose();
            context.Properties["HUScroll.Frames"] = result.Frames;
            context.Properties["HUScroll.StopReason"] = result.StopReason;

            string summary = $"{result.Image.Width} x {result.Image.Height} px from {result.Frames} frames - {result.StopReason}";
            context.LogStep("HUScroll: " + summary);
            Log.Info("Scroll capture (recipe): " + summary);
        }
    }
}
