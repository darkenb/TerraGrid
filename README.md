# BUG_TerraGrid

Lay foundation over a whole rectangle with the game's own foundation tool. Pick two corners and the area is painted for you, block by block, using the foundation, colour and texture you picked.

## How to use

1. Open the foundation tool and pick a foundation (or the restore brush).
2. Press **Ctrl+D** to turn area mode on.
3. Click the first corner, move the cursor and click the second corner.

The cursor shows the size of the area (columns × rows and the tile count) before you click.

| Key | Action |
|---|---|
| Ctrl+D | Area mode on or off (off also closes the foundation tool) |
| Ctrl+P | Go the short way or the long way around the planet between the two corners |
| Ctrl+Shift+P | Pause or resume the painting in progress |
| Ctrl+Shift+D | Cancel the painting in progress |

All keys can be changed in the config file (`bug.dsp.terragrid.cfg`).

## How painting works

- Before anything is painted, the whole area is checked: enough foundation items and soil, and no Dark Fog base pit inside it. If something is missing, nothing is painted and you are told what is missing. The corners stay picked, so you can fetch what you need and click again.
- Tiles that already have foundation are only re-coloured or re-textured, which costs no items, as in the game.
- Foundation items are taken from your inventory and from the mecha's logistic slots. With DeliverySlotsTweaks the logistic slots are used the way that mod sets them.
- Painting goes on after you close the tool or switch items. The nearest block in build range is painted first. Blocks out of range are marked with blue squares and are painted when you get close.
- A new area picked while painting joins the work in progress with its own foundation and colour. Where the two overlap, the newer one wins.
- Painting stops if you leave the planet or run out of items or soil.

## Settings

| Setting | Default | |
|---|---|---|
| `BlockSize` | 12 | Areas are painted in blocks of this many tiles per side, one block per frame. Larger is faster, smaller keeps each frame lighter. Restore uses at most 10. |
| `ShowPendingTiles` | true | Mark tiles still to be painted with blue squares. |

## Compatibility

- Works on planets of every size, also with Galactic Scale. The tiles are the same ones the game's own brush picks.
- Galactic Scale's brush size buttons keep working; the size you pick is used again when area mode is off.
- Cannot be used together with Foundation Painter, which changes the same tool. If both are installed, this mod does not load.

## Licence

MIT. Written from scratch on the game's own planet grid; it contains no code of Foundation Painter or any other mod.

## Building from source

You need a current .NET SDK and the .NET Framework 4.7.2 targeting pack. The game, Unity and BepInEx assemblies are referenced from your own installation and are not part of this repository.

```text
dotnet build -c Release "-p:DSPGamePath=D:\Games\Dyson Sphere Program" "-p:BepInExCorePath=C:\path\to\BepInEx\core"
```

The output is `bin/Release/net472/BUG_TerraGrid.dll`. Copy it to `BepInEx/plugins/BUG_TerraGrid/`.
