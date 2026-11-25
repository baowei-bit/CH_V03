namespace CH_V03.Core.Mes;

public sealed class JingWangCraftSchedule
{
    private DateTime _lastAttempt;
    private DateTime _lastCollectedTestEnd = DateTime.MinValue;
    private bool _collecting;

    public JingWangCraftSchedule(DateTime startedAt) => _lastAttempt = startedAt;

    public bool TryBegin(DateTime now, DateTime testEnd, int minutes)
    {
        if (_collecting || testEnd <= _lastCollectedTestEnd
            || now - _lastAttempt < TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 1440))) return false;
        _lastAttempt = now;
        _collecting = true;
        return true;
    }

    public void Complete(DateTime testEnd, bool savedLocallyOrWritten)
    {
        if (savedLocallyOrWritten && testEnd > _lastCollectedTestEnd) _lastCollectedTestEnd = testEnd;
        _collecting = false;
    }
}
