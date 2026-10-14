namespace PBDMP.Network;

/// <summary>Host-owned generations distinguish same-map restart and reconnect from stale ready/pose packets.</summary>
public sealed class HostTravel
{
    public TravelState State { get; private set; } = new(1, TravelAction.Menu, "", 0, true, false, true, false);

    public void Begin(TravelAction action, string level, int difficulty, bool joining = false)
    {
        var epoch = checked(State.Epoch + 1);
        var next = new TravelState(epoch, action, action == TravelAction.Menu ? "" : level, difficulty,
            action == TravelAction.Menu, false, false, joining);
        Protocol.Validate(next);
        State = next;
    }

    public void SetReady(JoinLoadout? loadout = null)
    {
        var next = State with { HostReady = true, Loadout = loadout };
        Protocol.Validate(next); State = next;
    }

    public void SetPaused(bool paused) => State = State with { Paused = paused };

    public void Acknowledge(ClientReady? ready)
    {
        if (ready?.Epoch == State.Epoch && ready.Ready && State.HostReady &&
            (!State.Joining || State.Action == TravelAction.Menu || State.Loadout != null))
            State = State with { Released = true };
    }

    public void PeerLeft() => State = State with { Released = State.HostReady };
}
