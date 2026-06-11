# Game assemblies (not included)

The build references Escape from Duckov's own managed assemblies (and a few
Unity modules). These are **proprietary** and are **not** redistributed here.

To build from source, populate this folder with the DLLs the project references
(see `../../src/DuckovController/DuckovController.csproj` for the exact list).
They are found in your own copy of the game at:

    <Escape from Duckov>/Duckov_Data/Managed/

Copy (or symlink) the referenced `*.dll` files from there into this folder.
The only redistributable dependency, `0Harmony.dll` (Harmony, MIT), is fetched
separately — see the CI workflow or drop your own copy under
`reference/HarmonyLib_workshop/0Harmony.dll`.

`*.dll` in this folder is gitignored on purpose.
