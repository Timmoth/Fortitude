namespace Fortitude.Client;

public abstract class FortitudeHandlerBase
{
    public abstract bool Matches(FortitudeRequest request);

    public abstract Task<FortitudeResponse> BuildResponse(FortitudeRequest req);
}