using System.Reflection;
using System.Runtime.Versioning;
using ClassIsland;

#if NIX
[assembly: AssemblyVersion("0.0.0.0")]
[assembly: AssemblyInformationalVersion("NIXBUILD+NIXBUILD_LONG_HASH")]
#else
[assembly: AssemblyVersion("2.2.18.0")]
[assembly: AssemblyFileVersion("2.2.18.0")]
[assembly: AssemblyInformationalVersion($"2.2.18+{GitInfo.CommitHash}")]
#endif

[assembly: AssemblyTitle("ClassIsland")]
[assembly: AssemblyProduct("ClassIsland")]
#if NETCOREAPP
// [assembly: SupportedOSPlatform("Windows")]
#endif
#if Platforms_MacOs
[assembly:SupportedOSPlatform("macos")]
#endif
 
