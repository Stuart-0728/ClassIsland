namespace ClassIsland;
public static class App { public static T GetService<T>() => (T)(object)Microsoft.Extensions.Logging.Abstractions.NullLogger<ClassIsland.Services.Management.BashuPlatformConnection>.Instance; }
