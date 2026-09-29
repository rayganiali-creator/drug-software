namespace MedSmarter.IntegrationTests;

/// <summary>Runs only when MEDSMARTER_IT=1 and the real stack (docker compose) is reachable.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MEDSMARTER_IT") != "1")
        {
            Skip = "Set MEDSMARTER_IT=1 with the compose stack running (see README).";
        }
    }
}
