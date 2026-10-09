namespace DistrictAI.ViewModels.Calls;

/// <summary>
/// Text a person types into a box the core also writes to (the dialler's
/// number): which of the core's values to put into the box.
/// </summary>
/// <remarks>
/// Every change the person makes is sent as an event and comes back later as
/// the core's value, after the person may have typed more. Putting that value
/// back into the box would take away what they typed since, and move the
/// cursor. So a value this box sent is only acknowledged, and the box is
/// written only when the core's value is one the box did not send: the number
/// cleared after a call, or a number filled in from a call back. The same rule
/// as District AI for Linux's <c>Echo</c>.
/// </remarks>
internal sealed class TextEcho
{
    /// <summary>What the box sent that the core has not given back yet, oldest first.</summary>
    private readonly List<string> _pending = [];

    /// <summary>The core's value when the box was last drawn.</summary>
    private string _acknowledged = string.Empty;

    /// <summary>The box now holds <paramref name="text"/>, which is being sent to the core.</summary>
    public void Typed(string text) => _pending.Add(text);

    /// <summary>Whether to write the core's <paramref name="value"/> into a box holding <paramref name="shown"/>.</summary>
    public bool Write(string value, string shown)
    {
        if (value == _acknowledged && _pending.Count > 0)
        {
            // Nothing new from the core: what the box holds is newer.
            return false;
        }
        _acknowledged = value;
        var sent = _pending.IndexOf(value);
        if (sent >= 0)
        {
            _pending.RemoveRange(0, sent + 1);
            return false;
        }
        _pending.Clear();
        return value != shown;
    }
}
