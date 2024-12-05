using Dalamud.Interface;
using Dalamud.Interface.Colors;
using GagSpeak.Services.Mediator;
using GagSpeak.UI.Handlers;
using ImGuiNET;

namespace GagSpeak.UI.MainWindow;

/// <summary> 
/// Sub-class of the main UI window. Handles drawing the whitelist/contacts tab of the main UI.
/// </summary>
public class MainUiWhitelist : DisposableMediatorSubscriberBase
{
    private readonly UserPairListHandler _userPairListHandler;

    public MainUiWhitelist(ILogger<MainUiWhitelist> logger, GagspeakMediator mediator,
        UserPairListHandler userPairListHandler) : base(logger, mediator)
    {
        _userPairListHandler = userPairListHandler;
        _userPairListHandler.UpdateDrawFoldersAndUserPairDraws();

        Mediator.Subscribe<RefreshUiMessage>(this, (msg) =>
        {
            _userPairListHandler.UpdateDrawFoldersAndUserPairDraws(); // Update Pair List
            _userPairListHandler.UpdateKinksterRequests(); // Update Kinkster Requests
        });
    }

    /// <summary>
    /// Main Draw function for the Whitelist/Contacts tab of the main UI
    /// </summary>
    public void DrawWhitelistSection()
    {
        try
        {
            _userPairListHandler.DrawSearchFilter(true, true);
            ImGui.Separator();
            _userPairListHandler.DrawPairs();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error drawing whitelist section");
        }
    }
}
