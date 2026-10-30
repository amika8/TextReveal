using System.Runtime.InteropServices;
using System.Windows;

// Every P/Invoke of this assembly is a system DLL: restricting the search path to System32 makes
// DLL planting next to the executable impossible (security baseline for a desktop app).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
