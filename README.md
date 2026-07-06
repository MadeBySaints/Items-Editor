# Items-Editor

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

DevM Items Editor is a tool that can make server development easier.

Offer tools to update and create missing items on your XML by pushing it from client 12 assets and Tibia WIKI.

This item editor was created to work along with Canary and OpenTibiaBR repository but can work on another servers.

> On different server bases and versions may demand adaptation to work as intended.

> **This fork:** patched to load and save modern Canary (protocol 15.x) items.xml files without crashing or
> silently dropping/corrupting attributes. See [Fork changes](#fork-changes) below.

> **License:** the upstream [marcosvf132/Items-Editor](https://github.com/marcosvf132/Items-Editor) has no
> license of its own (all rights reserved by default). This fork's own changes and additions are released
> under the [MIT license](LICENSE) in this repo; that does not retroactively relicense marcosvf132's
> original, unmodified code.

# How to use
 - Compile the program with Visual studio or download the compiled release.
 - Open the executable file and click on 'Load XML'.
 - Choose the type of your XML file (ServerID or ClientID).
   - **ServerID** expects a matching `items.otb`. Modern Canary (client 12+/appearances.dat, no `items.otb`)
     doesn't have one — use **ClientID** instead, since Canary's ids are already the client/appearance ids.
 - Edit any item you want, or click on 'Load assets' to reload and create missing items following the data on your client 12 assets.
 - Update your items data from tibia WIKI. I highly recommend you load the client 12 assets first.
 - If the log window pops up with warnings/errors, it's also dumped to `items_editor_log.txt` next to your
   user profile folder for easier review.

# Need help?
 - Feel free to message me on Discord. Check the 'about' label below.

# Compiling
 Open the project on Visual Studio and just hit Build, or run `dotnet build "Devm items editor.csproj" -c Release`
 from a terminal. Packages used:
  - [Newtonsoft JSON v13.0.1](https://www.newtonsoft.com/json)
  - [MaterialDesignThemes.MahApps v0.1.9](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)
  - [MahApps.Metro v2.4.9](https://github.com/MahApps/MahApps.Metro)
  - [Costura.Fody v5.7](https://github.com/Fody/Costura)
  - [Google.Protobuf v3.15.3](https://github.com/protocolbuffers/protobuf)

 Targets `netcoreapp3.1`. Building requires the .NET SDK (the .NET 8 SDK can build this target fine); running a
 prebuilt release requires the **.NET Core 3.1 Desktop Runtime** to be installed alongside whatever newer
 runtime you may already have (`winget install Microsoft.DotNet.DesktopRuntime.3_1`).

# Fork changes
 The original upstream build (as of the version this was forked from) would crash outright when loading a
 real, current Canary `items.xml`, and silently dropped a large number of attributes on save that Canary has
 added since this tool was last updated. Fixed in this fork:

 - **Crash: nested `BeginInit` on retry.** `EndInit()` was never called if loading threw partway through, so
   after any load failure the item list was left permanently unusable until the app was restarted. Moved
   `EndInit()` into a `finally` block.
 - **Crash: one bad item aborted the entire load.** A single item with an attribute the parser didn't
   recognize (e.g. an unknown imbuement type) would throw and stop loading the rest of the file. Per-item
   parsing is now wrapped so a bad item is logged and skipped instead of losing everything else.
 - **Added read/write support** (previously silently dropped on save) for: `primarytype`, `script`
   (`moveevent`/`moveevent;weapon` and its nested `action`/`level`/`vocation`/`slot`/`unproperly`/`weaponType`/
   `wandType`/`mana`/`toDamage`/`fromDamage`/`breakChance`/`armor`/`chain`), `bedpart`, `bedpartof`,
   `transformonuse`, `elementalbond`, `usedbyhouseguests`, `reflectdamage`, `cleavepercent`,
   `perfectshotdamage`/`perfectshotrange`, `magicshieldcapacityflat`/`percent`, `mantra`, the per-element
   `*magiclevelpoints` attributes, the `augments` weapon-proficiency perk system, the `paralysis deflection`/
   `vibrancy`/`skillboost fist` imbuements, and the `type="dummy"` nested `rate` attribute.
 - **Fixed silent data corruption:** `lifeleechamount`/`lifeleechchance`/`manaleechamount`/`manaleechchance`
   were being read and written under made-up key names (`skilllifeamount` etc.) that Canary doesn't recognize
   at all, so round-tripping a file through this tool silently deleted those attributes.
 - **Fixed silent data corruption:** `absorbpercentearth` was aliased onto the *same* internal field as
   `absorbpercentpoison` (read, write, and the UI field), so an item with earth resistance would have it
   silently renamed to poison resistance after a save. Split into its own field.
 - **Fixed write-back key bug:** the tool wrote `moveable` on save, which Canary does not recognize (only
   `movable` is a real key) — this would have silently dropped the not-movable restriction from any item that
   went through a save.
 - Multi-line `description` values now get their embedded line breaks normalized to a single space on read,
   matching standard XML attribute-value normalization, instead of coming back out as a literal newline.
 - The on-screen log window is now also written to `items_editor_log.txt` for easier review of large logs.

 Verified via a full semantic diff of a load→save round trip against a real, current 15.x `items.xml`
 (37,509 items): zero remaining differences that aren't pre-existing duplicate-attribute definitions already
 present in the source file itself (which Canary's own parser resolves identically either way).

 New features added on top of the original tool:

 - **Overview tab.** A dashboard shown on load: total loaded entries/ids, distinct type count, last load
   warning count, and full breakdowns by `type` and `primarytype`. Refreshes automatically after Load XML,
   Load assets, and Load Wiki.
 - **Find free IDs button.** Scans the loaded appearance catalog for ids that have real client sprite data
   but no real `items.xml` entry (or only a blank/reserved placeholder) — i.e. ids that are safe to repurpose
   for a new custom item. Writes `free_ids_report.txt`.
 - **Data quality report button.** Re-scans the raw loaded XML for duplicate attribute keys within the same
   item, empty names, and named items missing `primarytype`. Writes `data_quality_report.txt`.
 - **Sprite preview.** The Index tab now renders the selected item's actual sprite, decoded directly from the
   client's `sprites-*.bmp.lzma` sheets (see [`SpriteRenderer.cs`](SpriteRenderer.cs) for the reverse-engineered
   file format). "Load assets" also loads `catalog-content.json` from the same folder to enable this — point
   it at your **client's** `things/<version>/` folder (not just a lone copy of `appearances.dat`) for previews
   to work. Uses a vendored copy of the classic public-domain LZMA SDK (`Lzma/`, Igor Pavlov,
   [jljusten/LZMA-SDK](https://github.com/jljusten/LZMA-SDK)) for the raw LZMA1 decompression.

# About
 Tool created by Marcosvf132. You can message me on discord if you have any doubts or wan't to contribute somehow:
  > Discord: Marcosvf132#8947
