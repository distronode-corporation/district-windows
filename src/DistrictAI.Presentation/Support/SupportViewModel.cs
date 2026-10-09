using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Support;

/// <summary>
/// The support requests, open and resolved, and the form that raises one:
/// what the page shows, copied from the core's <see cref="SupportView"/>, and
/// what its buttons send. The form's boxes hold what was typed, which is what
/// the core keeps; every change sends the whole form, and whether it can be
/// sent is the core's to say.
/// </summary>
public sealed partial class SupportViewModel : ObservableObject
{
    private readonly TextEcho _subjectEcho = new();
    private readonly TextEcho _messageEcho = new();
    private PageContext? _context;
    private bool _writing;
    private SupportKind[] _kinds = [];

    /// <summary>Loading, failure, the empty list and refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The requests still open.</summary>
    public ObservableCollection<SupportRowItem> Open { get; } = [];

    /// <summary>The requests resolved.</summary>
    public ObservableCollection<SupportRowItem> Resolved { get; } = [];

    /// <summary>The kinds the form offers, by their labels, in the core's order.</summary>
    public ObservableCollection<string> KindLabels { get; } = [];

    /// <summary>Whether there are open requests to list.</summary>
    [ObservableProperty]
    public partial bool HasOpen { get; set; }

    /// <summary>Whether there are resolved requests to list.</summary>
    [ObservableProperty]
    public partial bool HasResolved { get; set; }

    /// <summary>A note that the list may be missing older requests, or empty.</summary>
    [ObservableProperty]
    public partial string CappedNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="CappedNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasCappedNote { get; set; }

    /// <summary>The confirmation of a request raised, or empty.</summary>
    [ObservableProperty]
    public partial string Submitted { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Submitted"/> confirmation.</summary>
    [ObservableProperty]
    public partial bool HasSubmitted { get; set; }

    /// <summary>Whether "New request" is offered (no form is open).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRequestCommand))]
    public partial bool CanStart { get; set; }

    /// <summary>Whether the form raising a request is open.</summary>
    [ObservableProperty]
    public partial bool Composing { get; set; }

    /// <summary>The form's heading.</summary>
    [ObservableProperty]
    public partial string ComposeTitle { get; set; } = string.Empty;

    /// <summary>Which of <see cref="KindLabels"/> is chosen.</summary>
    [ObservableProperty]
    public partial int KindIndex { get; set; } = -1;

    /// <summary>The subject, as typed.</summary>
    [ObservableProperty]
    public partial string Subject { get; set; } = string.Empty;

    /// <summary>The message, as typed.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>The longest subject the service takes.</summary>
    [ObservableProperty]
    public partial int SubjectMax { get; set; }

    /// <summary>The longest message the service takes.</summary>
    [ObservableProperty]
    public partial int MessageMax { get; set; }

    /// <summary>What the form needs before it can be sent, or empty.</summary>
    [ObservableProperty]
    public partial string Needs { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Needs"/> line.</summary>
    [ObservableProperty]
    public partial bool HasNeeds { get; set; }

    /// <summary>Whether "Send" works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitRequestCommand))]
    public partial bool CanSubmit { get; set; }

    /// <summary>Whether the request is on its way.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelRequestCommand))]
    public partial bool Submitting { get; set; }

    /// <summary>Whether the form can be changed (not while it is on its way).</summary>
    [ObservableProperty]
    public partial bool CanEdit { get; set; }

    /// <summary>Why the last attempt failed, or empty.</summary>
    [ObservableProperty]
    public partial string ComposeFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ComposeFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasComposeFailure { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(SupportView view)
    {
        var rows = view.Open.Length + view.Resolved.Length;
        Load.Show(view.Status, rows > 0, view.Empty, view.Refreshing, view.RefreshFailure);
        Display.Sync(Open, [.. view.Open.Select(SupportRowItem.From)]);
        Display.Sync(Resolved, [.. view.Resolved.Select(SupportRowItem.From)]);
        HasOpen = view.Open.Length > 0;
        HasResolved = view.Resolved.Length > 0;
        CappedNote = view.CappedNote ?? string.Empty;
        HasCappedNote = view.CappedNote is not null;
        Submitted = view.Submitted ?? string.Empty;
        HasSubmitted = view.Submitted is not null;
        CanStart = view.CanStart;
        ShowCompose(view.Compose);
    }

    private void ShowCompose(SupportComposeView? compose)
    {
        Composing = compose is not null;
        ComposeTitle = compose?.Title ?? string.Empty;
        _kinds = compose is null ? [] : [.. compose.Kinds.Select(kind => kind.Kind)];
        _writing = true;
        try
        {
            string[] labels = compose is null ? [] : [.. compose.Kinds.Select(kind => kind.Label)];
            Display.Sync(KindLabels, labels);
            KindIndex = compose is null ? -1 : Array.IndexOf(_kinds, compose.Kind);
            var subject = compose?.Subject ?? string.Empty;
            if (_subjectEcho.Write(subject, Subject))
            {
                Subject = subject;
            }
            var message = compose?.Message ?? string.Empty;
            if (_messageEcho.Write(message, Message))
            {
                Message = message;
            }
        }
        finally
        {
            _writing = false;
        }
        SubjectMax = (int)Math.Min(compose?.SubjectMax ?? 0, int.MaxValue);
        MessageMax = (int)Math.Min(compose?.MessageMax ?? 0, int.MaxValue);
        Needs = compose?.Needs ?? string.Empty;
        HasNeeds = compose?.Needs is not null;
        CanSubmit = compose is { CanSubmit: true, Submitting: false };
        Submitting = compose?.Submitting ?? false;
        CanEdit = compose is { Submitting: false };
        ComposeFailure = Display.Failure(compose?.Failure);
        HasComposeFailure = compose?.Failure is not null;
    }

    partial void OnKindIndexChanged(int value) => Edited();

    partial void OnSubjectChanged(string value)
    {
        if (!_writing)
        {
            _subjectEcho.Typed(value);
        }
        Edited();
    }

    partial void OnMessageChanged(string value)
    {
        if (!_writing)
        {
            _messageEcho.Typed(value);
        }
        Edited();
    }

    /// <summary>Sends the form as it now reads, when the person changed it.</summary>
    private void Edited()
    {
        if (_writing || !Composing || KindIndex < 0 || KindIndex >= _kinds.Length)
        {
            return;
        }
        Send(new SupportAction.EditRequest(_kinds[KindIndex], Subject, Message));
    }

    internal void OpenRequest(SupportRowItem row) => Send(new SupportAction.OpenRequest(row.Key));

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartRequest() => Send(new SupportAction.StartRequest());

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private void SubmitRequest() => Send(new SupportAction.SubmitRequest());

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void CancelRequest() => Send(new SupportAction.CancelRequest());

    [RelayCommand]
    private void DismissSubmitted() => Send(new SupportAction.DismissSubmitted());

    private bool CanDiscard() => !Submitting;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(SupportAction action) => _context?.Send(new UiEvent.Support(action));
}

/// <summary>A support request in the list.</summary>
/// <param name="Key">What opens it.</param>
/// <param name="Subject">The subject.</param>
/// <param name="Line">Its reference, where it stands and when it last changed.</param>
public sealed record SupportRowItem(string Key, string Subject, string Line)
{
    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Subject + ", " + Line;

    internal static SupportRowItem From(SupportRowView row)
    {
        var line = string.Join(
            " · ",
            new[] { row.Reference, row.Status, Display.When(row.UpdatedAt) }.Where(part => part.Length > 0));
        return new SupportRowItem(row.Key, row.Subject, line);
    }

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
