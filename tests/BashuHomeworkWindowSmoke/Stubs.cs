namespace ClassIsland
{
    public static class App { public static T GetService<T>() => throw new InvalidOperationException("Isolated window test has no classroom services"); }
}
namespace ClassIsland.Core
{
    public static class CommonDirectories { public static string AppDataFolderPath => Path.Combine(Path.GetTempPath(),"BashuHomeworkWindowSmoke-default"); }
}
namespace ClassIsland.Core.Abstractions.Services.Management
{
    public interface IManagementService { ClassIsland.Shared.Models.Management.ManagementSettings Settings {get;} }
}
