# JinGu Lib

JinGu Lib is a dependency mod for other mods.

Currently provides support for adding custom rebindable actions to the game's Controls menu.

# Usage

Add a reference to `JinGuLib.dll` in your `.csproj`:

```xml
<Reference
  Include="../BepInEx/plugins/nozwock.JinGuLib/*.dll"
  Private="false" />
```

JinGuLib should be added as a BepInEx dependency by putting the following attribute onto your plugin class, below the
BepInAutoPlugin attribute.

```cs
[BepInDependency(JinGuLib.Plugin.Id)]
```

Here's [an example][RebindRegistry-example] making use of `JinGuLib.UI.RebindRegistry`.

[RebindRegistry-example]: https://github.com/nozwock/psychic-kungfu-mods/blob/11682bf0cf6367ba4f2d4fdf468cce2da27ff6e7/VanillaPlus/src/Plugin.cs#L82-L86
