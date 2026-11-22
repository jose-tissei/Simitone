# ScriptDump

Reads your installed The Sims 1 (`GameData/Global/Global.far` and `GameData/Objects/Objects.far`) and writes every
SimAntics script (BHAV), string table, constants table (BCON) and interaction table (TTAB) as readable text, with
`expression` and `relationship` operands decoded and calls resolved to their names.

```
dotnet build Tools/ScriptDump -c Release -o Tools/ScriptDump/out
copy <app>\Monogame\Windows\MonoGame.Framework.dll Tools\ScriptDump\out\
Tools\ScriptDump\out\dump.exe "C:\Program Files (x86)\Maxis\The Sims" dump.txt
```

It references the released `FSO.Files.dll` / `FSO.Common.dll` in the patched game folder (`AppDir` in `dump.csproj`,
defaults to `%USERPROFILE%\.t3\projects\the-sims-run\app`), so it is x86 .NET Framework. The output is a few MB and
is not committed. `docs/architecture/RELATIONSHIPS.md` is the analysis built from it.

How to read the output: a call target >= 256 is a global script, >= 4096 a script in the same object or its semi-global.
`Tuning[n]` means constants table `(n >> 7)` (+4096 for the object's own table, +8192 semi-global, +256 global),
entry `n & 127`.
