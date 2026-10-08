using DistrictAI.Core;
using Microsoft.UI.Dispatching;

namespace DistrictAI.Platform;

/// <summary><see cref="IUiDispatcher"/> over the window's <see cref="DispatcherQueue"/>.</summary>
internal sealed class QueueDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool TryEnqueue(Action work) => queue.TryEnqueue(new DispatcherQueueHandler(work));
}
