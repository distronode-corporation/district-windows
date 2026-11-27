// SPDX-FileCopyrightText: Distronode Corporation
//
// SPDX-License-Identifier: Apache-2.0

using DistrictAI.ViewModels.AccountSecurity;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DistrictAI.Views.AccountSecurity;

/// <summary>
/// Confirming it is you, on the two-step and deletion pages: the providers,
/// "Use my password", then the password and, once the service asks, the code.
/// The password and the code live in their boxes only until Continue sends
/// them; the boxes are emptied then, when the step goes and when the page is
/// left (<see cref="ReauthViewModel.FieldsCleared"/>).
/// </summary>
public sealed partial class ReauthPanel : UserControl
{
    /// <summary>The panel over <paramref name="model"/>, its page's.</summary>
    public ReauthPanel(ReauthViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
        ViewModel.FieldsCleared += OnFieldsCleared;
    }

    /// <summary>What the panel shows, and its actions.</summary>
    public ReauthViewModel ViewModel { get; }

    private void OnChoiceClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ReauthChoiceItem choice)
        {
            ViewModel.Choose(choice.Provider);
        }
    }

    private void OnContinueClick(object sender, RoutedEventArgs e) => Submit();

    private void OnEnterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            Submit();
            e.Handled = true;
        }
    }

    private void Submit() => _ = ViewModel.Submit(PasswordInput.Password, CodeInput.Text);

    private void OnFieldsCleared(object? sender, EventArgs e)
    {
        PasswordInput.Password = string.Empty;
        CodeInput.Text = string.Empty;
    }
}
