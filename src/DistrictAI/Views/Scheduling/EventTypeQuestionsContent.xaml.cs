using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// An event type's booking questions: the list with Edit and Delete (asked
/// once more), Add question, and one question's form in place of the list.
/// </summary>
public sealed partial class EventTypeQuestionsContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public EventTypeQuestionsContent(EventTypeQuestionsViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public EventTypeQuestionsViewModel ViewModel { get; }

    // An ItemsRepeater whose template uses x:Bind gives what it makes no
    // DataContext, so each row's button carries its question's id, or its
    // option, in Tag.
    private QuestionEntry? QuestionOf(object sender) =>
        (sender as FrameworkElement)?.Tag is string id ? ViewModel.Questions.FirstOrDefault(question => question.Id == id) : null;

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (QuestionOf(sender) is { } question)
        {
            ViewModel.Edit(question);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (QuestionOf(sender) is { } question)
        {
            ViewModel.AskDelete(question);
        }
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickKind(KindChooser.SelectedIndex);

    private void OnRemoveOptionClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string value && ViewModel.Options.FirstOrDefault(entry => entry.Option == value) is { } option)
        {
            ViewModel.RemoveOption(option);
        }
    }

    /// <summary>Enter in the new choice's box adds it, as Add option does.</summary>
    private void OnNewOptionKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.AddOptionCommand.Execute(null);
            e.Handled = true;
        }
    }
}
