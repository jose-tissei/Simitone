# ButtonHoverPatch

Patches `FSO.UI.dll` from the prebuilt `SimitoneWindows.zip` release so that default
button text (eg. the OK button in alerts) turns dark blue (`#00387B`) on hover instead of
white, which is unreadable on light buttons. The same change is in `FSO.UI/UILayer.cs`
for source builds.

    dotnet run -- <original FSO.UI.dll> <patched output dll> <Simitone app dir>

Close the game, keep a backup of the original DLL, then copy the patched DLL over it.
