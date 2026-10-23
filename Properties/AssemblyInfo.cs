using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("A2G-Scaler")]
[assembly: AssemblyDescription("Skaliert Anstoss 2 Gold (640x480) auf beliebige Fenster- und Bildschirmgrößen")]
[assembly: AssemblyProduct("A2G-Scaler")]
[assembly: AssemblyCopyright("transfairs")]
[assembly: ComVisible(false)]

// Lets A2G-Scaler.Tests reach the internal classes (Settings, Recovery, HotkeyText, ...).
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("A2G-Scaler.Tests")]
