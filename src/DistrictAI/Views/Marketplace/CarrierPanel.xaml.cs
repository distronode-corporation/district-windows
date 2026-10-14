using DistrictAI.ViewModels.Marketplace;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Marketplace;

/// <summary>
/// The phone numbers screen's carrier tab: the carrier accounts, the SIP
/// trunks, the one-time passcode sender, and the A2P 10DLC and toll-free
/// registrations, over the page's <see cref="NumberAdminViewModel"/>.
/// </summary>
public sealed partial class CarrierPanel : UserControl
{
    /// <summary>A panel with nothing shown yet.</summary>
    public CarrierPanel()
    {
        InitializeComponent();
    }

    /// <summary>What the panel shows, and its actions: the page's.</summary>
    public NumberAdminViewModel Admin { get; private set; } = new();

    /// <summary>Shows <paramref name="admin"/>, the page's.</summary>
    internal void Attach(NumberAdminViewModel admin)
    {
        Admin = admin;
        AccountsStatus.Attach(admin.AccountsLoad);
        SipStatus.Attach(admin.SipLoad);
        VerifyStatus.Attach(admin.VerifyLoad);
        Bindings.Update();
    }
}
