namespace Pulse1x.App.Services.GameHub;

/// <summary>Two presses must target the same card in the same input zone.</summary>
public sealed class GameActivationGesture
{
    private string? _gameId;
    private HubZone _zone;
    private long _pressedAt;

    public bool Press(string gameId, HubZone zone, long nowMilliseconds, int intervalMilliseconds)
    {
        bool activate = _gameId == gameId && _zone == zone &&
                        nowMilliseconds >= _pressedAt &&
                        nowMilliseconds - _pressedAt <= intervalMilliseconds;
        if (activate) Reset();
        else
        {
            _gameId = gameId;
            _zone = zone;
            _pressedAt = nowMilliseconds;
        }
        return activate;
    }

    public void Reset() => _gameId = null;
}
