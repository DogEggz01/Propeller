# Source baseline and build

The repository previously contained 1.1.0 source while the installed release was 1.2.1. This source tree restores the installed 1.2.1 assembly using ILSpy before applying the two panel changes for 1.2.2. The expanded file layout reflects recovered classes, not a rewrite of the mod.

The recovered baseline built successfully. Decompiling that rebuild produced exact matches for 39 of 41 class files. The remaining two files (`PumpState` and `PumpWater`) differed only in equivalent expressions and temporary variable names. This is a source comparison, not a proof of binary identity.

Baseline installed DLL SHA-256: `776A22F7CADC46E650218B0D89FC7A540D953F12B6591BD49E50565176BAB9A8`.

Unchanged asset bundle SHA-256: `81A795B90D38A68F1F7F8BBAA89A02007D01E62C1119F1387BA54E3F98936F5D`.

The only gameplay changes after recovery are in `PumpHammerMovingMount.Prefix` and `PumpMount.Tick`. Version metadata is now 1.2.2. Save keys, save schema, assets, pairing, physics, and vanilla ownership handling remain unchanged.

## Build

Requires .NET SDK 10 and a local Sailwind installation with BepInEx. Game assemblies are referenced locally and are not included in this repository.

```powershell
dotnet build Propeller.csproj -c Release '-p:GameDir=C:/path/to/Sailwind/'
./release.ps1 -GameDir 'C:/path/to/Sailwind' -OutDir './release'
```

The release script packages the compiled DLL and the existing installed Propeller asset bundle. It does not install the mod or publish a release.
