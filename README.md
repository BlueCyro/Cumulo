# ⚡🌪️ Cumulo: A whirlwind of weaving!
A simple patcher that uses Mono.Cecil to weave the Resonite Headless for use in .NET 8

- All-in-one solution to enable running the headless server on .NET 8
- Patches several key dlls to prevent crashes and allow greater compatibility with the new runtime
- Bundled with [Nimbus](https://github.com/RileyGuy/Nimbus) to provide auxiliary patches in order to properly function
- Bundled with [Harmony](https://github.com/pardeike/Harmony) built for .NET 8 to support [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mods

# Requirements
- Access to the Resonite Headless server (this repo ain't a bargain bin)
- ResoniteModLoader version 2.6.0 or greater

# Command Line Usage
Cumulo is run from the command line/terminal and can either be run standalone or as part of a script - such as your own server startup script for example.

- Linux usage: `mono Cumulo.exe /path/to/headless/here`
- Windows usage: `Cumulo.exe C:\path\to\your\headless\here`

When running Cumulo, you will be prompted before any changes are made to your Headless server with this message:

```
--------
You are about to apply Cumulo patches to "yourheadlesspath".
This operation is NOT reversible and will make your
headless server incompatible with Mono/.NET Framework
in order to support .NET 8.
--------
```

The changes made are:
- System.Security.Permissions.dll for .NET 8 is added to the root of the Headless folder as a new dependency
- Resonite.runtimeconfig.json is added to the root of the Headless folder to designate .NET 8 as the desired runtime.
- Nimbus.dll is copied to the rml_mods folder to enable compatibility with normal Resonite clients.
- 0Harmony.dll is copied to the Libraries folder as a replacement for the one provided by ResoniteModLoader
- FrooxEngine.dll and FrooxEngine.Weavers.dll are patched in several key locations to enable .NET 8 compatibility

You may also pass `--noconfirm` as an optional flag to run Cumulo without user confirmation. **Cumulo will not prompt you before making changes when using this flag**.

E.g. `mono Cumulo.exe /headless/path/here --noconfirm`

# How do I run with .NET 8?
Simply replace your startup command with `dotnet Resonite.exe -YourExistingLaunchFlagsHere -Etc.`

E.g. if you're running linux and using mono to run the headless, the command goes from:
- `mono Resonite.exe`

to
- `dotnet Resonite.exe`

<br/><br/>
<br/><br/>

<div style="text-align: right">Small shoutout to <a href="https://github.com/Nihlus/Crystite">Crystite</a> for inspiring implementations for some of the trickier hoops I had to jump through.</div>

