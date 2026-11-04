using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Composer;

/// <summary>
/// The WhatsApp side of the reply box, copied from the core's
/// <see cref="WhatsAppView"/>: the line a closed window shows, and the
/// approved templates it offers, one field per variable, the filled preview
/// (the core's, as the service will store it) and Send template, which is
/// turned off as it is pressed, so one press is one billed message.
/// </summary>
public sealed partial class TemplatePickerViewModel : ObservableObject
{
    private PageContext? _context;
    private bool _writing;
    private string? _chosen;

    /// <summary>Whether the reply goes by WhatsApp.</summary>
    [ObservableProperty]
    public partial bool Visible { get; set; }

    /// <summary>"Window closed: WhatsApp only allows a template now", or empty.</summary>
    [ObservableProperty]
    public partial string WindowClosed { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="WindowClosed"/> line.</summary>
    [ObservableProperty]
    public partial bool HasWindowClosed { get; set; }

    /// <summary>"Loading templates", or why there is no template to offer, or empty.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Message"/>.</summary>
    [ObservableProperty]
    public partial bool HasMessage { get; set; }

    /// <summary>Whether the picker shows: approved templates to choose from.</summary>
    [ObservableProperty]
    public partial bool ShowPicker { get; set; }

    /// <summary>The picker's label: "Template".</summary>
    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>The picker's placeholder: "Choose an approved template".</summary>
    [ObservableProperty]
    public partial string Placeholder { get; set; } = string.Empty;

    /// <summary>The approved templates.</summary>
    public ObservableCollection<TemplateChoice> Options { get; } = [];

    /// <summary>The template chosen.</summary>
    [ObservableProperty]
    public partial TemplateChoice? SelectedTemplate { get; set; }

    /// <summary>One field per variable of the chosen template.</summary>
    public ObservableCollection<TemplateVariableField> Variables { get; } = [];

    /// <summary>The preview's label: "Preview".</summary>
    [ObservableProperty]
    public partial string PreviewLabel { get; set; } = string.Empty;

    /// <summary>The template filled with what is typed, or empty before one is chosen.</summary>
    [ObservableProperty]
    public partial string Preview { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Preview"/>.</summary>
    [ObservableProperty]
    public partial bool HasPreview { get; set; }

    /// <summary>The send button: "Send template".</summary>
    [ObservableProperty]
    public partial string SendLabel { get; set; } = string.Empty;

    /// <summary>Whether Send template works now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool CanSend { get; set; }

    internal void Attach(PageContext context) => _context = context;

    /// <summary>Shows <paramref name="view"/>, or hides it when the reply does not go by WhatsApp.</summary>
    internal void Show(WhatsAppView? view)
    {
        Visible = view is not null;
        WindowClosed = view?.WindowClosed ?? string.Empty;
        HasWindowClosed = WindowClosed.Length > 0;
        var picker = view?.Templates;
        _writing = true;
        try
        {
            Message = picker?.Message ?? string.Empty;
            HasMessage = Message.Length > 0;
            ShowPicker = picker is not null && picker.Options.Length > 0;
            Label = picker?.Label ?? string.Empty;
            Placeholder = picker?.Placeholder ?? string.Empty;
            Display.Sync(Options, [.. (picker?.Options ?? []).Select(option => new TemplateChoice(option.Id, option.Label))]);
            _chosen = picker?.Chosen;
            SelectedTemplate = Options.FirstOrDefault(option => option.Id == _chosen);
            ShowVariables(picker?.Variables ?? []);
            PreviewLabel = picker?.PreviewLabel ?? string.Empty;
            Preview = picker?.Preview ?? string.Empty;
            HasPreview = Preview.Length > 0;
            SendLabel = picker?.SendLabel ?? string.Empty;
            CanSend = picker?.CanSend ?? false;
        }
        finally
        {
            _writing = false;
        }
    }

    /// <summary>
    /// The fields for <paramref name="variables"/>: the same fields while the
    /// template is the same (so the one being typed in keeps the keyboard), each
    /// taking the core's value only where it differs from what was typed.
    /// </summary>
    private void ShowVariables(TemplateVariableView[] variables)
    {
        var same = Variables.Count == variables.Length
            && Variables.Zip(variables).All(pair => pair.First.Label == pair.Second.Label);
        if (!same)
        {
            Variables.Clear();
            foreach (var variable in variables)
            {
                Variables.Add(new TemplateVariableField(variable.Index, variable.Label, variable.Value, SendVariable));
            }
        }
        foreach (var (field, variable) in Variables.Zip(variables))
        {
            field.Write(variable.Value);
            field.Required = variable.Required ?? string.Empty;
            field.HasRequired = variable.Required is not null;
        }
    }

    private void SendVariable(uint index, string value) =>
        _context?.Send(new UiEvent.Composer(new ComposerAction.TemplateVariable(index, value)));

    partial void OnSelectedTemplateChanged(TemplateChoice? value)
    {
        if (_writing || value is null || _context is null || value.Id == _chosen)
        {
            return;
        }
        _chosen = value.Id;
        _context.Send(new UiEvent.Composer(new ComposerAction.ChooseTemplate(value.Id)));
    }

    /// <summary>Sends the chosen template, filled.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        if (_context is null || !CanSend)
        {
            return;
        }
        CanSend = false;
        _context.Send(new UiEvent.Composer(new ComposerAction.SendTemplate()));
    }
}

/// <summary>One approved template in the picker.</summary>
/// <param name="Id">Its id, to choose it by.</param>
/// <param name="Label">Its name and language: "appointment_reminder (en_US)".</param>
public sealed record TemplateChoice(string Id, string Label)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>The field for one variable of the chosen template.</summary>
public sealed partial class TemplateVariableField : ObservableObject
{
    private readonly Action<uint, string> _send;
    private readonly TextEcho _echo = new();
    private bool _writing;

    internal TemplateVariableField(uint index, string label, string value, Action<uint, string> send)
    {
        Index = index;
        Label = label;
        _send = send;
        _writing = true;
        Value = value;
        _writing = false;
    }

    /// <summary>Its place, from zero.</summary>
    public uint Index { get; }

    /// <summary>"Variable {{1}}".</summary>
    public string Label { get; }

    /// <summary>What is typed.</summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>"Fill in this variable" while it is empty, else empty.</summary>
    [ObservableProperty]
    public partial string Required { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Required"/> line.</summary>
    [ObservableProperty]
    public partial bool HasRequired { get; set; }

    /// <summary>
    /// Takes the core's <paramref name="value"/> without sending it back, and
    /// only when it is not one this field sent (what was typed since stays).
    /// </summary>
    internal void Write(string value)
    {
        if (!_echo.Write(value, Value))
        {
            return;
        }
        _writing = true;
        try
        {
            Value = value;
        }
        finally
        {
            _writing = false;
        }
    }

    partial void OnValueChanged(string value)
    {
        if (!_writing)
        {
            _echo.Typed(value);
            _send(Index, value);
        }
    }
}
