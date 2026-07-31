using System.Reflection;
using System.Runtime.Loader;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArrWatch.Services;

public sealed class StartupTask : IScheduledTask
{
    private static readonly Guid TransformationId =
        Guid.Parse("9fb59ac5-e4dc-4f8c-a257-80dc9ee89996");
    private readonly ILogger<StartupTask> _logger;

    public StartupTask(ILogger<StartupTask> logger)
    {
        _logger = logger;
    }

    public string Name => "Arr Watch Startup";
    public string Key => "Jellyfin.Plugin.ArrWatch.Startup";
    public string Description => "Registers Arr Watch with Jellyfin Web.";
    public string Category => "Startup Services";

    public Task ExecuteAsync(
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var assembly = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(candidate =>
                candidate.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) == true);
        var pluginType = assembly?.GetType(
            "Jellyfin.Plugin.FileTransformation.FileTransformationPlugin");
        var writeServiceType = assembly?.GetType(
            "Jellyfin.Plugin.FileTransformation.Library.IWebFileTransformationWriteService");
        var delegateType = assembly?.GetType(
            "Jellyfin.Plugin.FileTransformation.Library.TransformFile");
        var pluginInstance = pluginType?
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null);
        var serviceProvider = pluginInstance?
            .GetType()
            .GetProperty("ServiceProvider", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(pluginInstance) as IServiceProvider;

        if (writeServiceType is null || delegateType is null || serviceProvider is null)
        {
            _logger.LogWarning(
                "File Transformation was not found. Arr Watch cannot be injected.");
            return Task.CompletedTask;
        }

        var writeService = serviceProvider.GetService(writeServiceType);
        var updateMethod = writeServiceType.GetMethod("UpdateTransformation");
        var transformMethod = typeof(WebInjection).GetMethod(
            nameof(WebInjection.TransformIndex),
            BindingFlags.Public | BindingFlags.Static);
        if (writeService is null || updateMethod is null || transformMethod is null)
        {
            _logger.LogWarning("File Transformation API is unavailable.");
            return Task.CompletedTask;
        }

        var callback = Delegate.CreateDelegate(delegateType, transformMethod);
        updateMethod.Invoke(writeService, [TransformationId, "index.html", callback]);
        _logger.LogInformation("Arr Watch registered its Jellyfin Web transformation.");
        progress.Report(100);
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger }];
    }
}
