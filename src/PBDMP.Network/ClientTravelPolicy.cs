namespace PBDMP.Network;

public static class ClientTravelPolicy
{
    // Admission advances the epoch without changing the map being loaded.
    // A second native LoadLevel here would overlap scene teardown/creation.
    public static bool ReusePendingLoad(TravelState? previous,TravelState next,bool nativeReady) =>
        !nativeReady && previous is not null && next.Epoch>previous.Epoch && next.Joining &&
        previous.Action==TravelAction.Load && next.Action==TravelAction.Load &&
        previous.Level==next.Level && previous.Difficulty==next.Difficulty &&
        previous.StoryVideo==next.StoryVideo;
}
