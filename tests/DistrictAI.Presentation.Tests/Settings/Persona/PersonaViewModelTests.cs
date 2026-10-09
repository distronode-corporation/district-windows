using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Persona;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Persona;

public sealed class PersonaViewModelTests
{
    private static PickerView Picker(string selected, params (string Value, string Label)[] choices) =>
        new([.. choices.Select(choice => new ChoiceView(choice.Value, choice.Label))], selected,
            choices.FirstOrDefault(choice => choice.Value == selected).Label ?? (selected.Length > 0 ? selected : "Not chosen"));

    private static PersonaEngineView Engine(string language = "en-US") =>
        new(
            "Deepgram Pipeline",
            "The voice is changed in Voice Studio.",
            "Open Voice Studio",
            Picker(language, ("en-US", "English (US)"), ("fr-CA", "French (Canada)")),
            Picker("concise", ("concise", "Concise"), ("detailed", "Detailed")));

    private static AuditionView Audition(
        string? state = null,
        bool waiting = false,
        FailureView? failure = null,
        bool start = true,
        bool canStart = true,
        string? cooling = null) =>
        new("Try this receptionist", "It is billed like any call.", state, waiting, failure, start, canStart, !start, cooling, "Start", "Stop");

    private static PersonaView View(
        SectionStatus? status = null,
        string name = "Ada",
        bool textEditable = true,
        PersonaEngineView? engine = null,
        string? engineNote = null,
        bool canRetryOptions = false,
        string? refit = null,
        SaveNoticeView? notice = null,
        bool saving = false,
        bool canSave = false,
        bool canPreview = true,
        bool previewEnabled = true,
        AuditionView? audition = null) =>
        new(
            "Persona",
            status ?? new SectionStatus.Ready(),
            "What it says",
            name,
            "Hello",
            "Warm",
            textEditable,
            "Clearing a box saves it as empty.",
            "Language and answers",
            engine,
            engine is not null,
            engineNote,
            canRetryOptions,
            refit,
            notice,
            saving,
            canSave,
            "Save",
            canPreview,
            previewEnabled,
            "Try this receptionist",
            audition);

    [Fact]
    public void TheFormShowsTheCoreValues()
    {
        var model = new PersonaViewModel();
        var view = View(engine: Engine(), refit: "Checking.");
        model.Show(view);

        Assert.Same(view, model.View);
        Assert.Equal("Persona", model.Title);
        Assert.True(model.IsReady);
        Assert.False(model.IsLoading || model.HasStatus);
        Assert.Equal(("Ada", "Hello", "Warm"), (model.Name, model.Greeting, model.Personality));
        Assert.True(model.TextEditable && model.HasEngine && model.EngineEditable);
        Assert.Equal("What it says", model.TextsHeading);
        Assert.Equal("Clearing a box saves it as empty.", model.ClearHint);
        Assert.Equal("Language and answers", model.EngineHeading);
        Assert.Equal("Deepgram Pipeline", model.EngineLabel);
        Assert.Equal("Open Voice Studio", model.StudioLabel);
        Assert.StartsWith("The voice", model.StudioHint, StringComparison.Ordinal);
        Assert.Equal("en-US", model.SelectedLanguage?.Value);
        Assert.Equal("English (US)", model.SelectedLanguage?.ToString());
        Assert.Equal("concise", model.SelectedResponseLength?.Value);
        Assert.Equal(2, model.Languages.Count);
        Assert.True(model.HasRefitLine);
        Assert.Equal("Save", model.SaveLabel);
        Assert.Equal("Try this receptionist", model.PreviewLabel);
        Assert.True(model.CanPreview && model.PreviewEnabled);
        Assert.False(model.AuditionOpen);
    }

    [Fact]
    public void LoadingAFailedReadAndASaveNotReadBack()
    {
        var model = new PersonaViewModel();
        model.Show(View(status: new SectionStatus.Loading()));
        Assert.True(model.IsLoading);
        Assert.False(model.IsReady || model.HasStatus);

        model.Show(View(status: new SectionStatus.Failed("Could not load", V.Failure("Offline.", retryable: true))));
        Assert.True(model.HasStatus && model.HasStatusAction);
        Assert.Equal(("Could not load", "Offline.", "Try again"), (model.StatusTitle, model.StatusBody, model.StatusAction));

        model.Show(View(status: new SectionStatus.Failed("Could not load", V.Failure("Refused."))));
        Assert.False(model.HasStatusAction);

        model.Show(View(status: new SectionStatus.Stale("Saved", "Read them again first.", "Read them again")));
        Assert.Equal(("Saved", "Read them again"), (model.StatusTitle, model.StatusAction));
        Assert.True(model.HasStatusAction);
    }

    [Fact]
    public void TypingSendsEachTextAndTheCoreDoesNotOverwriteIt()
    {
        var model = new PersonaViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View());

        model.Name = "Gr";
        model.Name = "Grace";
        model.Greeting = "Hi";
        model.Personality = "Kind";
        Assert.Equal(
            [
                new UiEvent.Persona(new PersonaAction.EditText(PersonaField.Name, "Gr")),
                new UiEvent.Persona(new PersonaAction.EditText(PersonaField.Name, "Grace")),
                new UiEvent.Persona(new PersonaAction.EditText(PersonaField.Greeting, "Hi")),
                new UiEvent.Persona(new PersonaAction.EditText(PersonaField.Personality, "Kind")),
            ],
            sink.Sent);

        // The core catching up with the first keystroke does not undo the second.
        model.Show(View(name: "Gr"));
        Assert.Equal("Grace", model.Name);
        // A value the box never sent (a save read back, a refresh) is written.
        model.Show(View(name: "Stored"));
        Assert.Equal("Stored", model.Name);
        Assert.Equal(4, sink.Sent.Count);
    }

    [Fact]
    public void ChoosingSendsTheValueAndTheCoreChoiceSendsNothing()
    {
        var model = new PersonaViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View(engine: Engine()));
        Assert.Empty(sink.Sent);

        model.SelectedLanguage = model.Languages[1];
        model.SelectedResponseLength = model.ResponseLengths[1];
        model.SelectedLanguage = null;
        Assert.Equal(
            [
                new UiEvent.Persona(new PersonaAction.ChooseLanguage("fr-CA")),
                new UiEvent.Persona(new PersonaAction.ChooseResponseLength("detailed")),
            ],
            sink.Sent);
    }

    [Fact]
    public void AStoredValueTheChoicesDoNotListShowsAsStored()
    {
        var model = new PersonaViewModel();
        model.Show(View(engine: Engine(language: "xx-OLD")));
        Assert.Equal(3, model.Languages.Count);
        Assert.Equal(new ChoiceItem("xx-OLD", "xx-OLD"), model.SelectedLanguage);

        model.Show(View(engineNote: "Reading the languages.", canRetryOptions: true));
        Assert.False(model.HasEngine);
        Assert.Empty(model.Languages);
        Assert.Null(model.SelectedLanguage);
        Assert.True(model.HasEngineNote && model.CanRetryOptions);
    }

    [Fact]
    public void SaveTheNoticeAndTheButtons()
    {
        var model = new PersonaViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View(canSave: false));
        Assert.False(model.SaveCommand.CanExecute(null));

        model.Show(View(canSave: true));
        model.SaveCommand.Execute(null);
        model.Show(View(saving: true, textEditable: false));
        Assert.True(model.Saving);
        Assert.False(model.TextEditable);

        model.Show(View(notice: new SaveNoticeView("Saved.", true)));
        Assert.True(model.HasNotice && model.NoticeSaved);
        model.Show(View(notice: new SaveNoticeView("Offline.", false)));
        Assert.False(model.NoticeSaved);
        Assert.Equal("Offline.", model.NoticeText);

        model.DismissNoticeCommand.Execute(null);
        model.OpenStudioCommand.Execute(null);
        model.RetryCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.Persona(new PersonaAction.Save()),
                new UiEvent.Persona(new PersonaAction.DismissSaveNotice()),
                new UiEvent.Persona(new PersonaAction.OpenVoiceStudio()),
                new UiEvent.Refresh(),
            ],
            sink.Sent);
    }

    [Fact]
    public void TheAuditionSaysItIsBilledAndStartsOnlyFromStart()
    {
        var model = new PersonaViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View());
        model.OpenPreviewCommand.Execute(null);
        Assert.Equal([new UiEvent.Persona(new PersonaAction.OpenPreview())], sink.Sent);

        model.Show(View(previewEnabled: false, audition: Audition()));
        Assert.True(model.AuditionOpen);
        Assert.False(model.OpenPreviewCommand.CanExecute(null));
        Assert.Equal("Try this receptionist", model.AuditionTitle);
        Assert.Contains("billed", model.BilledNote, StringComparison.Ordinal);
        Assert.True(model.ShowStart && model.StartPreviewCommand.CanExecute(null));
        Assert.False(model.ShowStop);
        Assert.Equal(("Start", "Stop"), (model.StartLabel, model.StopLabel));
        model.StartPreviewCommand.Execute(null);

        model.Show(View(audition: Audition(state: "Connecting to your receptionist.", waiting: true, start: false, canStart: false)));
        Assert.True(model.HasAuditionState && model.AuditionWaiting && model.ShowStop);
        Assert.False(model.StartPreviewCommand.CanExecute(null));
        model.StopPreviewCommand.Execute(null);

        model.Show(View(audition: Audition(failure: V.Failure("Not available."), canStart: false, cooling: "In a few seconds.")));
        Assert.True(model.HasAuditionFailure && model.HasCooling);
        Assert.Equal("Not available.", model.AuditionFailure);
        model.ClosePreview();

        model.Show(View());
        Assert.False(model.AuditionOpen);
        Assert.Equal(
            [
                new UiEvent.Persona(new PersonaAction.OpenPreview()),
                new UiEvent.Persona(new PersonaAction.StartPreview()),
                new UiEvent.Persona(new PersonaAction.StopPreview()),
                new UiEvent.Persona(new PersonaAction.ClosePreview()),
            ],
            sink.Sent);
    }

    [Fact]
    public void WithNoCoreNothingIsSent()
    {
        var model = new PersonaViewModel();
        model.Send(new PersonaAction.Open());
        model.Show(View());
        model.Name = "Grace";
        model.RetryCommand.Execute(null);
        Assert.Equal("Grace", model.Name);
    }
}
