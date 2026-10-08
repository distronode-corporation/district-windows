using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Calls;

/// <summary>How a <see cref="FailureView"/> reads on a calls screen.</summary>
internal static class FailureText
{
    /// <summary>
    /// The failure's message, with the regions line under it when the core
    /// gives one; empty for no failure.
    /// </summary>
    public static string Of(FailureView? failure) => failure switch
    {
        null => string.Empty,
        { RegionsLine: { Length: > 0 } regions } => failure.Message + Environment.NewLine + regions,
        _ => failure.Message,
    };
}
