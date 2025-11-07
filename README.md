# Strong Name Remover

## Description
Removes strong names off of all patched .NET assemblies in a folder. Also removes transitive dependendants, such that given the patched assembly `C` and the dependency graph `A -> B -> [C]`, the whole chain
gets their strong-names removed, as well as assembly and `InternalsVisibleToAttribute` references updated.

## Usage
Patched assemblies are detected by checking for file name suffix `.Patched` (such as `Fortender.StrongNameRemover.Patched.dll`)
```
StrongNameRemover.exe <src dir> <dst dir>
```

## Motivation
Sometimes you want to apply patches to [strong-named](https://learn.microsoft.com/en-us/dotnet/standard/assembly/strong-named) .NET assemblies.
Those patches will cause the assembly's signature to become invalid which requires you to resign the assembly with your own key-pair or remove the strong name entirely.
When the strong name changes, though, dependant assemblies won't load the patched assembly if references are strong-named, too. This requires you to update all references
as well, which in turn requires you to remove strong name of dependant assemblies as well, and so on and so forth...
