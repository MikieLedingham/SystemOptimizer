using System.Runtime.InteropServices;
using System.Windows;

// Everything that used to live here - title, description, company, product,
// copyright, trademark, culture, AssemblyVersion, AssemblyFileVersion - moved
// into SystemOptimizer.csproj and is now generated at build time.
// Only the two attributes with no MSBuild equivalent remain.

[assembly: ComVisible(false)]

// Tells WPF not to hunt for per-theme satellite assemblies (SystemOptimizer
// .Aero2.NormalColor.dll and friends) that this app has never shipped, and to
// look in this assembly for the generic dictionary. Dropping it costs a round
// of failed assembly probes on every first control load.
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly)]
