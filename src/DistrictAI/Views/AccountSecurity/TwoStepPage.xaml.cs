// SPDX-FileCopyrightText: Distronode Corporation
//
// SPDX-License-Identifier: Apache-2.0

using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.AccountSecurity;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.System;

namespace DistrictAI.Views.AccountSecurity;

/// <summary>
/// Two-step verification for the account, below the account page: the status
/// and the change it offers, confirming it is you (<see cref="ReauthPanel"/>),
/// the new secret as a QR code and a key with Copy key and Open in an
/// authenticator app, the first code, then the recovery codes once with Copy
/// all. The key, the picture, the codes and the typed code leave the page
/// whenever it is not showing (<see cref="Leave"/>); a copy leaves the
/// clipboard after two minutes unless something else was copied since
/// (<see cref="SecretCopies"/>).
/// </summary>
public sealed partial class TwoStepPage : UserControl
{
    private byte[]? _drawn;

    /// <summary>A page with nothing shown yet.</summary>
    public TwoStepPage()
    {
        ViewModel = new TwoStepViewModel(new SecretCopies(new WindowsSecretClipboard(), TimeProvider.System));
        InitializeComponent();
        ReauthHost.Content = new ReauthPanel(ViewModel.Reauth);
        ViewModel.SecretsCleared += OnSecretsCleared;
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public TwoStepViewModel ViewModel { get; }

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(TwoStepView view)
    {
        ViewModel.Show(view);
        DrawQr();
    }

    /// <summary>The page is no longer showing: what it showed once, and what was typed, goes.</summary>
    internal void Leave() => ViewModel.Leave();

    /// <summary>Draws the QR code the view model holds, once per picture.</summary>
    private async void DrawQr()
    {
        var png = ViewModel.QrPng;
        if (ReferenceEquals(png, _drawn))
        {
            return;
        }
        _drawn = png;
        if (png is null)
        {
            QrImage.Source = null;
            return;
        }
        var bitmap = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        try
        {
            await bitmap.SetSourceAsync(stream);
        }
        catch (Exception error) when (error is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // Not a picture Windows can read: no picture, and the page says
            // to enter the key, which still works.
            QrImage.Source = null;
            ViewModel.QrUnreadableHere(png);
            return;
        }
        // Still the picture to show, once it is decoded.
        if (ReferenceEquals(ViewModel.QrPng, png))
        {
            QrImage.Source = bitmap;
        }
    }

    private void OnSecretsCleared(object? sender, EventArgs e)
    {
        CodeInput.Text = string.Empty;
        QrImage.Source = null;
        _drawn = null;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => Confirm();

    private void OnCodeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            Confirm();
            e.Handled = true;
        }
    }

    private void Confirm()
    {
        if (ViewModel.Confirm(CodeInput.Text))
        {
            CodeInput.Text = string.Empty;
        }
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e) => await ViewModel.OpenAuthenticatorAsync();

    private void OnNoticesClosed(InfoBar sender, object args) => ViewModel.DismissNoticesCommand.Execute(null);
}
