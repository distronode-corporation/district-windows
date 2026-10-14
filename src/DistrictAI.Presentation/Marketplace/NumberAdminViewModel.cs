using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Marketplace;

/// <summary>A carrier account on the carrier tab.</summary>
/// <param name="Carrier">The carrier.</param>
/// <param name="Lines">Its name, state, balance and number count, each when known, one per line.</param>
/// <param name="Refused">Whether its credentials were refused.</param>
public sealed record CarrierAccountItem(string Carrier, string Lines, bool Refused)
{
    internal static CarrierAccountItem From(CarrierAccountView account) =>
        new(
            account.Carrier,
            string.Join(Environment.NewLine, new[] { account.AccountName, account.State, account.Balance, account.NumberCount }.OfType<string>()),
            account.Refused);
}

/// <summary>A SIP trunk on the carrier tab.</summary>
/// <param name="Name">The trunk's name.</param>
/// <param name="Domain">Its domain.</param>
/// <param name="Meta">Its state and how many addresses may use it.</param>
/// <param name="Note">That no carrier resources are recorded for it, or empty.</param>
public sealed record SipTrunkItem(string Name, string Domain, string Meta, string Note)
{
    /// <summary>Whether there is a <see cref="Note"/>.</summary>
    public bool HasNote => Note.Length > 0;

    internal static SipTrunkItem From(SipTrunkView trunk) => new(trunk.Name, trunk.Domain, trunk.Meta, trunk.Unprovisioned ?? string.Empty);
}

/// <summary>
/// Managing the numbers held and the carrier account: the lookup, the
/// confirmation the core asks before a release, a lookup or a registration,
/// and the carrier tab. What is typed in a form goes to the core whole as it
/// changes, and the core says when it can be sent. Every word is the core's.
/// </summary>
public sealed partial class NumberAdminViewModel : ObservableObject
{
    private readonly Dictionary<string, TextEcho> _echoes = [];
    private PageContext? _context;
    private bool _writing;
    private string[] _campaignValues = [];
    private string[] _useCaseValues = [];

    /// <summary>The carrier tab's label.</summary>
    [ObservableProperty]
    public partial string CarrierTab { get; set; } = string.Empty;

    /// <summary>What the last reapply or release said, or empty.</summary>
    [ObservableProperty]
    public partial string NumberNotice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="NumberNotice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNumberNotice { get; set; }

    /// <summary>The lookup's heading.</summary>
    [ObservableProperty]
    public partial string LookupEyebrow { get; set; } = string.Empty;

    /// <summary>The lookup's number field label.</summary>
    [ObservableProperty]
    public partial string LookupLabel { get; set; } = string.Empty;

    /// <summary>The number to look up, as typed.</summary>
    [ObservableProperty]
    public partial string LookupNumber { get; set; } = string.Empty;

    /// <summary>The lookup's button.</summary>
    [ObservableProperty]
    public partial string LookupAction { get; set; } = string.Empty;

    /// <summary>Whether the lookup's button works now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskLookupCommand))]
    public partial bool CanAskLookup { get; set; }

    /// <summary>The last lookup's answer, a line each, or why it failed.</summary>
    [ObservableProperty]
    public partial string LookupResult { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="LookupResult"/>.</summary>
    [ObservableProperty]
    public partial bool HasLookupResult { get; set; }

    /// <summary>The confirmation the core is asking, while it asks.</summary>
    [ObservableProperty]
    public partial NumberConfirmationView? Confirmation { get; set; }

    /// <summary>Whether the carrier tab is read and shows.</summary>
    [ObservableProperty]
    public partial bool OnCarrier { get; set; }

    /// <summary>The carrier tab, as the core has it.</summary>
    [ObservableProperty]
    public partial CarrierTabView? Carrier { get; set; }

    /// <summary>Loading, failure and none connected, for the carrier accounts.</summary>
    public LoadStateViewModel AccountsLoad { get; } = new();

    /// <summary>Loading, failure and none yet, for the SIP trunks.</summary>
    public LoadStateViewModel SipLoad { get; } = new();

    /// <summary>Loading and failure, for the passcode sender.</summary>
    public LoadStateViewModel VerifyLoad { get; } = new();

    /// <summary>The carrier accounts.</summary>
    public ObservableCollection<CarrierAccountItem> Accounts { get; } = [];

    /// <summary>The SIP trunks.</summary>
    public ObservableCollection<SipTrunkItem> Trunks { get; } = [];

    /// <summary>The A2P campaign types, by their labels.</summary>
    public ObservableCollection<string> CampaignTypes { get; } = [];

    /// <summary>The toll-free use cases, by their labels.</summary>
    public ObservableCollection<string> UseCases { get; } = [];

    /// <summary>What the last carrier write said, and why it is not offered again, or empty.</summary>
    [ObservableProperty]
    public partial string CarrierNotice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="CarrierNotice"/>.</summary>
    [ObservableProperty]
    public partial bool HasCarrierNotice { get; set; }

    /// <summary>The SIP trunk name, as typed.</summary>
    [ObservableProperty]
    public partial string SipName { get; set; } = string.Empty;

    /// <summary>The SIP domain label, as typed.</summary>
    [ObservableProperty]
    public partial string SipDomain { get; set; } = string.Empty;

    /// <summary>The SIP trunk's allowed addresses, one per line, as typed.</summary>
    [ObservableProperty]
    public partial string SipAddresses { get; set; } = string.Empty;

    /// <summary>The A2P business name, as typed.</summary>
    [ObservableProperty]
    public partial string A2pBusiness { get; set; } = string.Empty;

    /// <summary>The A2P tax or company number, as typed.</summary>
    [ObservableProperty]
    public partial string A2pEin { get; set; } = string.Empty;

    /// <summary>The A2P website, as typed.</summary>
    [ObservableProperty]
    public partial string A2pWebsite { get; set; } = string.Empty;

    /// <summary>The A2P industry, as typed.</summary>
    [ObservableProperty]
    public partial string A2pVertical { get; set; } = string.Empty;

    /// <summary>Which of <see cref="CampaignTypes"/> is chosen.</summary>
    [ObservableProperty]
    public partial int CampaignIndex { get; set; } = -1;

    /// <summary>What the A2P campaign will send, as typed.</summary>
    [ObservableProperty]
    public partial string A2pDescription { get; set; } = string.Empty;

    /// <summary>The first A2P example message, as typed.</summary>
    [ObservableProperty]
    public partial string A2pSampleOne { get; set; } = string.Empty;

    /// <summary>The second A2P example message, as typed.</summary>
    [ObservableProperty]
    public partial string A2pSampleTwo { get; set; } = string.Empty;

    /// <summary>The toll-free number, as typed.</summary>
    [ObservableProperty]
    public partial string TollFreeNumber { get; set; } = string.Empty;

    /// <summary>The toll-free business name, as typed.</summary>
    [ObservableProperty]
    public partial string TollFreeBusiness { get; set; } = string.Empty;

    /// <summary>The toll-free opt-in link, as typed.</summary>
    [ObservableProperty]
    public partial string TollFreeOptIn { get; set; } = string.Empty;

    /// <summary>The toll-free example message, as typed.</summary>
    [ObservableProperty]
    public partial string TollFreeMessage { get; set; } = string.Empty;

    /// <summary>Which of <see cref="UseCases"/> is chosen.</summary>
    [ObservableProperty]
    public partial int UseCaseIndex { get; set; } = -1;

    internal void Attach(PageContext context)
    {
        _context = context;
        AccountsLoad.Attach(context);
        SipLoad.Attach(context);
        VerifyLoad.Attach(context);
    }

    internal void Show(NumberAdminView view)
    {
        CarrierTab = view.CarrierTab;
        NumberNotice = Join(view.NumberNotice, view.NumberUnrepeatable);
        HasNumberNotice = NumberNotice.Length > 0;
        var lookup = view.Lookup;
        LookupEyebrow = lookup.Eyebrow;
        LookupLabel = lookup.NumberLabel;
        LookupAction = lookup.Action;
        CanAskLookup = lookup.CanAsk;
        LookupResult = lookup.Notice is { } notice
            ? Join(notice, lookup.Unrepeatable)
            : string.Join(Environment.NewLine, lookup.Result);
        HasLookupResult = LookupResult.Length > 0;
        Confirmation = view.Confirmation;
        Carrier = view.Carrier;
        OnCarrier = view.Carrier is not null;
        _writing = true;
        try
        {
            Fill(nameof(LookupNumber), lookup.Number, LookupNumber, value => LookupNumber = value);
            if (view.Carrier is { } carrier)
            {
                ShowCarrier(carrier);
            }
        }
        finally
        {
            _writing = false;
        }
    }

    private void ShowCarrier(CarrierTabView carrier)
    {
        AccountsLoad.Show(carrier.AccountsStatus, carrier.Accounts.Length > 0, carrier.AccountsEmpty, false, null);
        SipLoad.Show(carrier.Sip.Status, carrier.Sip.Trunks.Length > 0, carrier.Sip.Empty, false, null);
        VerifyLoad.Show(carrier.Verify.Status, true, null, false, null);
        Display.Sync(Accounts, [.. carrier.Accounts.Select(CarrierAccountItem.From)]);
        Display.Sync(Trunks, [.. carrier.Sip.Trunks.Select(SipTrunkItem.From)]);
        CarrierNotice = Join(carrier.Notice, carrier.Unrepeatable);
        HasCarrierNotice = CarrierNotice.Length > 0;

        var sip = carrier.Sip.Form;
        Fill(nameof(SipName), sip.Name, SipName, value => SipName = value);
        Fill(nameof(SipDomain), sip.Domain, SipDomain, value => SipDomain = value);
        Fill(nameof(SipAddresses), sip.IpAddresses, SipAddresses, value => SipAddresses = value);

        var a2p = carrier.A2p;
        _campaignValues = [.. a2p.CampaignTypes.Select(choice => choice.Value)];
        Display.Sync(CampaignTypes, [.. a2p.CampaignTypes.Select(choice => choice.Label)]);
        CampaignIndex = Array.IndexOf(_campaignValues, a2p.Form.CampaignType);
        Fill(nameof(A2pBusiness), a2p.Form.BusinessName, A2pBusiness, value => A2pBusiness = value);
        Fill(nameof(A2pEin), a2p.Form.Ein, A2pEin, value => A2pEin = value);
        Fill(nameof(A2pWebsite), a2p.Form.Website, A2pWebsite, value => A2pWebsite = value);
        Fill(nameof(A2pVertical), a2p.Form.Vertical, A2pVertical, value => A2pVertical = value);
        Fill(nameof(A2pDescription), a2p.Form.CampaignDescription, A2pDescription, value => A2pDescription = value);
        Fill(nameof(A2pSampleOne), a2p.Form.SampleOne, A2pSampleOne, value => A2pSampleOne = value);
        Fill(nameof(A2pSampleTwo), a2p.Form.SampleTwo, A2pSampleTwo, value => A2pSampleTwo = value);

        var tollFree = carrier.TollFree;
        _useCaseValues = [.. tollFree.UseCases.Select(choice => choice.Value)];
        Display.Sync(UseCases, [.. tollFree.UseCases.Select(choice => choice.Label)]);
        UseCaseIndex = Array.IndexOf(_useCaseValues, tollFree.Form.UseCase);
        Fill(nameof(TollFreeNumber), tollFree.Form.PhoneNumber, TollFreeNumber, value => TollFreeNumber = value);
        Fill(nameof(TollFreeBusiness), tollFree.Form.BusinessName, TollFreeBusiness, value => TollFreeBusiness = value);
        Fill(nameof(TollFreeOptIn), tollFree.Form.OptInImageUrl, TollFreeOptIn, value => TollFreeOptIn = value);
        Fill(nameof(TollFreeMessage), tollFree.Form.MessageContent, TollFreeMessage, value => TollFreeMessage = value);
    }

    private static string Join(string? first, string? second) =>
        string.Join(" ", new[] { first, second }.OfType<string>().Where(part => part.Length > 0));

    private TextEcho Echo(string field)
    {
        if (!_echoes.TryGetValue(field, out var echo))
        {
            echo = new TextEcho();
            _echoes[field] = echo;
        }
        return echo;
    }

    /// <summary>Writes the core's <paramref name="value"/> into a field showing <paramref name="shown"/>, unless what was typed is newer.</summary>
    private void Fill(string field, string value, string shown, Action<string> write)
    {
        if (Echo(field).Write(value, shown))
        {
            write(value);
        }
    }

    /// <summary>A field was typed in: remembers it, and sends <paramref name="action"/> unless the core is writing.</summary>
    private void Typed(string field, string value, Func<NumberAdminAction> action)
    {
        if (_writing)
        {
            return;
        }
        Echo(field).Typed(value);
        Send(action());
    }

    partial void OnLookupNumberChanged(string value) =>
        Typed(nameof(LookupNumber), value, () => new NumberAdminAction.EditLookup(value));

    partial void OnSipNameChanged(string value) => Typed(nameof(SipName), value, SipForm);

    partial void OnSipDomainChanged(string value) => Typed(nameof(SipDomain), value, SipForm);

    partial void OnSipAddressesChanged(string value) => Typed(nameof(SipAddresses), value, SipForm);

    partial void OnA2pBusinessChanged(string value) => Typed(nameof(A2pBusiness), value, A2pForm);

    partial void OnA2pEinChanged(string value) => Typed(nameof(A2pEin), value, A2pForm);

    partial void OnA2pWebsiteChanged(string value) => Typed(nameof(A2pWebsite), value, A2pForm);

    partial void OnA2pVerticalChanged(string value) => Typed(nameof(A2pVertical), value, A2pForm);

    partial void OnA2pDescriptionChanged(string value) => Typed(nameof(A2pDescription), value, A2pForm);

    partial void OnA2pSampleOneChanged(string value) => Typed(nameof(A2pSampleOne), value, A2pForm);

    partial void OnA2pSampleTwoChanged(string value) => Typed(nameof(A2pSampleTwo), value, A2pForm);

    partial void OnCampaignIndexChanged(int value)
    {
        if (!_writing && value >= 0)
        {
            Send(A2pForm());
        }
    }

    partial void OnTollFreeNumberChanged(string value) => Typed(nameof(TollFreeNumber), value, TollFreeForm);

    partial void OnTollFreeBusinessChanged(string value) => Typed(nameof(TollFreeBusiness), value, TollFreeForm);

    partial void OnTollFreeOptInChanged(string value) => Typed(nameof(TollFreeOptIn), value, TollFreeForm);

    partial void OnTollFreeMessageChanged(string value) => Typed(nameof(TollFreeMessage), value, TollFreeForm);

    partial void OnUseCaseIndexChanged(int value)
    {
        if (!_writing && value >= 0)
        {
            Send(TollFreeForm());
        }
    }

    private static string Pick(string[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] : string.Empty;

    private NumberAdminAction.EditSipTrunk SipForm() =>
        new NumberAdminAction.EditSipTrunk(new SipTrunkFormInput(SipName, SipDomain, SipAddresses));

    private NumberAdminAction.EditA2p A2pForm() =>
        new NumberAdminAction.EditA2p(new A2pFormInput(
            A2pBusiness, A2pEin, A2pWebsite, A2pVertical, Pick(_campaignValues, CampaignIndex), A2pDescription, A2pSampleOne, A2pSampleTwo));

    private NumberAdminAction.EditTollFree TollFreeForm() =>
        new NumberAdminAction.EditTollFree(new TollFreeFormInput(
            TollFreeNumber, TollFreeBusiness, TollFreeOptIn, TollFreeMessage, Pick(_useCaseValues, UseCaseIndex)));

    /// <summary>"Reapply call routing" on a held number.</summary>
    internal void Configure(NumberRowItem row)
    {
        if (row.CanConfigure)
        {
            Send(new NumberAdminAction.Configure(row.PhoneNumber));
        }
    }

    /// <summary>"Release this number": the core asks first.</summary>
    internal void AskRelease(NumberRowItem row)
    {
        if (row.CanRelease)
        {
            Send(new NumberAdminAction.AskRelease(row.PhoneNumber));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAskLookup))]
    private void AskLookup() => Send(new NumberAdminAction.AskLookup());

    [RelayCommand]
    private void CreateSipTrunk()
    {
        if (Carrier?.Sip.CanCreate == true)
        {
            Send(new NumberAdminAction.CreateSipTrunk());
        }
    }

    [RelayCommand]
    private void ToggleVerify()
    {
        if (Carrier?.Verify is { CanToggle: true, Enabled: { } enabled })
        {
            Send(new NumberAdminAction.SetVerify(!enabled));
        }
    }

    [RelayCommand]
    private void AskA2p()
    {
        if (Carrier?.A2p.CanSubmit == true)
        {
            Send(new NumberAdminAction.AskA2p());
        }
    }

    [RelayCommand]
    private void AskTollFree()
    {
        if (Carrier?.TollFree.CanSubmit == true)
        {
            Send(new NumberAdminAction.AskTollFree());
        }
    }

    /// <summary>Go ahead with the confirmation asked.</summary>
    [RelayCommand]
    private void Confirm()
    {
        if (Confirmation is { CanConfirm: true })
        {
            Send(new NumberAdminAction.Confirm());
        }
    }

    /// <summary>Close the confirmation. The core refuses while its write is on its way.</summary>
    [RelayCommand]
    private void CancelConfirmation() => Send(new NumberAdminAction.CancelConfirmation());

    private void Send(NumberAdminAction action) => _context?.Send(new UiEvent.Marketplace(new MarketplaceAction.Numbers(action)));
}
