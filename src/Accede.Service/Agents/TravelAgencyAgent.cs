using Orleans.Journaling;

namespace Accede.Service.Agents;

internal interface ITravelAgencyAgent : IGrainWithStringKey
{
}

internal sealed class TravelAgencyAgent : DurableGrain
{
}
