using Fortitude.Client;

namespace Fortitude.Server;

public class HandlerSet(ILogger<HandlerSet> logger, List<FortitudeHandler> handlers)
{
    public async Task<FortitudeResponse?> HandleIncomingAsync(FortitudeRequest request)
    {
        for (var i = handlers.Count - 1; i >= 0; i--)
        {
            var handler = handlers[i];
            if (!handler.Matches(request)) continue;

            var response = await handler.HandleRequestAsync(request);

            logger.LogInformation("[Incoming]: {RequestId}", request.ToString());
            logger.LogInformation("[Handled] {response}", response);

            return response;
        }
        
        return null;
    }

    public void ReloadYamlFiles()
    {
        var configDir = Path.Combine(AppContext.BaseDirectory, "config"); 
     
        handlers.Clear();
        foreach (var handler in FortitudeYamlLoader.LoadHandlers(configDir))
        {
            handlers.AddRange(handler);
        }
    }
}