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

using System.Collections.Generic;
using Greenshot.Base.Recipes;
using Greenshot.Base.Recipes.Triggers;

namespace Greenshot.Plugin.HUScroll.Recipes
{
    /// <summary>
    /// The built-in recipes of HUScroll (Greenshot 1.4): scroll capture -> editor, started by a hotkey.
    /// The hotkeys can be changed (or more steps added) in Greenshot's recipe editor.
    /// </summary>
    public sealed class HUScrollRecipes : IBuiltInRecipeProvider
    {
        public const string RecipeIdAutomatic = "recipe_huscroll_automatic";
        public const string RecipeIdManual = "recipe_huscroll_manual";
        public const string HotkeyAutomatic = "Ctrl + Alt + PrintScreen";
        public const string HotkeyManual = "Ctrl + Alt + Shift + PrintScreen";

        public IEnumerable<CaptureRecipe> CreateRecipes()
        {
            yield return Create(RecipeIdAutomatic, "Scroll-Capture automatisch (HUScroll)",
                "HUScroll scrollt selbst und setzt alles zu einem Bild zusammen, Ergebnis im Editor", "Automatic", HotkeyAutomatic);
            yield return Create(RecipeIdManual, "Scroll-Capture manuell (HUScroll)",
                "Rahmen wählen, selbst scrollen, Esc = fertig, Ergebnis im Editor", "Manual", HotkeyManual);
        }

        private static CaptureRecipe Create(string id, string name, string description, string mode, string hotkey)
        {
            var recipe = new CaptureRecipe(id, name, description)
                .AddNode(new RecipeNodeConfig("acquire", HUScrollStep.StepType, "Scroll-Capture").Set("Mode", mode))
                .AddNode(RecipeStepConfig.CreateEditor("editor"))
                .AddTrigger(TriggerConfig.CreateHotkey(hotkey, name));
            recipe.Flow = new RecipeFlowConfig("acquire")
                .AddTransition("acquire", "editor");
            return recipe;
        }
    }
}
