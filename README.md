# Pico Recipes

A **"Just Enough Items" (JEI)** style item and recipe browser for **Vintage Story 1.22.x**, built with .NET 10.

If you know JEI from Minecraft, you already know how to use this mod:

- A **searchable item list** appears next to your inventory, listing every item and block in the game (including those added by other mods).
- **Hover** an item to see a compact recipe preview pop up next to it.
- **Left click** an item (or hover it and press **R**) to see **how it is made** in the full browser.
- **Right click** an item (or hover it and press **U**) to see **what it is used for**.
- Click any ingredient or output inside the recipe view to drill down into *its* recipes, with a **Back** button to retrace your steps.
- In **creative mode**, **shift-click** an item in the list to give yourself one (JEI "cheat mode").
- Press **Ctrl+O** at any time to turn the whole overlay on or off.

## Features

| JEI feature | Pico Recipes |
|---|---|
| Item list panel on container screens | Semi-transparent panel that appears beside your inventory (and chests, crafting, etc.) in lockstep, and disappears when you close them. |
| Enable/disable overlay | **Ctrl+O** toggles the whole overlay on or off; the choice is remembered between sessions. |
| Search bar | Docked at the bottom center of the screen (JEI style). Only captures typing once you click it. Live filtering by item name or code; `@modid` filters by mod, e.g. `@game sword`. |
| Hover preview | Hover an item in the list and a compact recipe pops up next to it — no click needed. |
| Pagination | `<` / `>` buttons or **mouse wheel** over the grid. |
| Recipe view (R / left click) | Grid crafting (with variant slideshows, exactly like the vanilla handbook), smithing, clay forming, knapping, barrel mixing/aging, smelting/cooking, grinding and crushing. Drawn above the inventory so it is never hidden behind it. |
| Usage view (U / right click) | Everything the item participates in as an ingredient or process input. |
| Recipe drill-down + history | Click any stack in the recipe browser; Back button pops the history. |
| Minimap | Hidden automatically while the overlay is up, restored when it closes. |
| Cheat mode | Shift-click gives the item — creative mode only, validated server side. |
| Tooltips | Standard Vintage Story item tooltips everywhere. |

The recipe/usage views are rendered with the same engine components the vanilla handbook uses
(`SlideshowGridRecipeTextComponent` and friends), so wood-typed and wildcard recipes cycle through
their variants and always show the correct matching output.

## Requirements

- Vintage Story **1.22.0 or newer** (developed against 1.22.3)
- Only needed on the **client**. Installing it on a server as well enables creative-mode
  shift-click item spawning; everything else works purely client side.

## Installing

Drop the release zip (`picorecipes_x.y.z.zip`) into your `Mods` folder
(`%appdata%/VintagestoryData/Mods` on Windows, `~/.config/VintagestoryData/Mods` on Linux).

## Building from source

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Build:

   ```sh
   dotnet build PicoRecipes/PicoRecipes.csproj -c Release
   ```

The build looks for your game installation (the folder containing `VintagestoryAPI.dll`)
in the usual places automatically: `%AppData%\Vintagestory`, `Desktop\Vintagestory`,
`%LocalAppData%\Vintagestory`, `C:\Program Files\Vintagestory` and `~/.local/share/vintagestory`.
If yours lives somewhere else, set the `VINTAGE_STORY` environment variable to that folder, e.g.

```powershell
setx VINTAGE_STORY "D:\Games\Vintagestory"   # then open a new terminal
```

The freely downloadable [dedicated server package](https://account.vintagestory.at/downloads)
also works as an assembly source.

The ready-to-use mod folder lands in `PicoRecipes/bin/Release/Mods/picorecipes`; zip its
*contents* (so `modinfo.json` is at the zip root) to get an installable mod. The GitHub Actions
workflow in this repo does all of the above automatically and uploads the zip as a build artifact.

## How it works

- **Item list** (`GuiDialogItemList` + `GuiElementClickableSlotGrid`): a display-only item slot
  grid backed by a `DummyInventory`. Clicks never move items; they open the browser instead.
  A lightweight game-tick watcher auto-opens/closes the panel alongside inventory-like dialogs.
- **Recipe browser** (`GuiDialogRecipeBrowser`): a scrollable richtext page with Recipes/Usages
  tabs, composed from the same text components the vanilla handbook uses (`RecipeComponentBuilder`,
  shared with the hover preview). It draws above the inventory so it is never hidden behind it.
- **Hover preview** (`GuiDialogRecipeHover`): a passive popup that renders a compact recipe for the
  item under the cursor after a short dwell. It is a regular dialog with a high `DrawOrder` (0.5),
  not a HUD element — HUD elements render in an earlier pass and would end up under the inventory
  and its scrollbar. It sizes itself to the content with a single composer (measure, then
  `ReCompose`), and skips the expensive process scan so hovering stays smooth.
- **Voxel recipes**: clay forming, knapping and smithing are shape-based rather than grid-based, so
  their `Pattern` is rendered as a small pixel grid (`VoxelPatternComponent`, a custom drawn
  richtext component) next to the input → output line.
- **Localization** (`Loc`): all UI strings have a built-in English fallback, so the interface stays
  readable even if the mod's lang file fails to load.
- **Minimap handling**: while the overlay is up, the vanilla minimap HUD is located at runtime by
  type name and closed, then reopened afterwards (best effort — no dependency on the map mod).
- **Recipe index** (`RecipeIndex`): grid recipes come straight from `IWorldAccessor.GridRecipes`.
  Smithing, knapping, clay forming and barrel recipes are read from the survival mod's
  `RecipeRegistrySystem` via reflection — no compile-time dependency on `VSSurvivalMod.dll` —
  and then handled through the engine's `RecipeBase` abstraction. Smelting, grinding and
  crushing are discovered from collectible properties.
- **Cheat mode**: a small network channel; the server only honors requests from players who are
  actually in creative mode.

## Limitations / roadmap

- Cooking-pot meal recipes and metal alloy ratios are not shown yet.
- Usage pages of extremely common ingredients (e.g. sticks) are capped at 50 recipe groups to
  keep the UI responsive; use search/drill-down for the rest.
- Bookmarks and recipe transfer (+) buttons are future work.
