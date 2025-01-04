using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using GagSpeak.Utils;
using GagSpeak.WebAPI;
using GagSpeak.WebAPI.Utils;
using GagspeakAPI.Data.Interfaces;
using GagspeakAPI.Enums;
using GagspeakAPI.Extensions;
using ImGuiNET;
using OtterGui.Text;
using System.Numerics;

namespace GagSpeak.UI.Permissions;

/// <summary>
/// Contains functions relative to the paired users permissions for the client user.
/// 
/// Yes its messy, yet it's long, but i functionalized it best i could for the insane 
/// amount of logic being performed without adding too much overhead.
/// </summary>
public partial class PairStickyUI
{
    public void DrawGagLayerSelection(float comboWidth, string pairAliasUID)
    {
        using (var gagLayerGroup = ImRaii.Group())
        {
            ImGui.SetNextItemWidth(comboWidth);
            ImGui.Combo("##GagLayerSelection", ref _gagLayer, new string[] { "Layer 1", "Layer 2", "Layer 3" }, 3);
            UiSharedService.AttachToolTip("Select the layer to apply a Gag to.");
        }
    }

    private void DrawGagActions()
    {
        if(StickyPair.LastAppearanceData is null) 
            return;

        DrawGagLayerSelection(ImGui.GetContentRegionAvail().X, StickyPair.UserData.UID);

        var gagSlot = StickyPair.LastAppearanceData.GagSlots[_gagLayer];

        bool disableLocking = gagSlot.GagType.ToGagType() is GagType.None;
        bool disableUnlocking = gagSlot.Padlock.ToPadlock() is Padlocks.None;
        bool disableRemoving = !disableUnlocking;
        bool disableApplying = !disableUnlocking || !PairPerms.GagFeatures;

        ////////// APPLY GAG //////////
        string DisplayGagText = !disableLocking ? "A " + gagSlot.GagType + " is applied." : "Apply a Gag to " + PairNickOrAliasOrUID;
        if (_uiShared.IconTextButton(FontAwesomeIcon.CommentDots, DisplayGagText, WindowMenuWidth, true, disableApplying))
            Opened = Opened == InteractionType.ApplyGag ? InteractionType.None : InteractionType.ApplyGag;
        UiSharedService.AttachToolTip("Apply a Gag to " + PairUID + ". Click to view options.");

        if (Opened is InteractionType.ApplyGag)
        {
            using (ImRaii.Child("###GagApplyChild", new Vector2(WindowMenuWidth, ImGui.GetFrameHeight()), false))
                _pairGagComboButton[_gagLayer].DrawCombo("##ApplyGagCombo-" + _gagLayer, "Select a Gag to apply", WindowMenuWidth, 1.15f, ImGui.GetTextLineHeightWithSpacing());
            ImGui.Separator();
        }

        ////////// LOCK GAG //////////
        string DisplayText = disableUnlocking ? "Lock "+PairNickOrAliasOrUID+"'s Gag" : "Locked with a " + gagSlot.Padlock;
        string tooltipText = disableUnlocking ? "Locks the Gag on " + PairNickOrAliasOrUID+ ". Click to view options." : "This Gag is locked with a " + gagSlot.Padlock;
        using (ImRaii.PushColor(ImGuiCol.Text, disableUnlocking ? ImGuiColors.DalamudWhite : ImGuiColors.DalamudYellow))
            if (_uiShared.IconTextButton(FontAwesomeIcon.Lock, DisplayText, WindowMenuWidth, true, disableLocking || (!disableLocking && !disableUnlocking) || !PairPerms.GagFeatures))
                Opened = Opened == InteractionType.LockGag ? InteractionType.None : InteractionType.LockGag;
        UiSharedService.AttachToolTip(tooltipText + ((!disableUnlocking && GenericHelpers.TimerPadlocks.Contains(gagSlot.Padlock))
            ? "--SEP----COL--" + UiSharedService.TimeLeftFancy(gagSlot.Timer) : ""), color : ImGuiColors.ParsedPink);

        if (Opened is InteractionType.LockGag)
        {

            Padlocks selected = _permActions.GetSelectedItem<Padlocks>("LockGagForPairPermCombo", StickyPair.UserData.UID);
            float height = _permActions.ExpandLockHeightCheck(selected) ? ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.Y : ImGui.GetFrameHeight();

            using (var actionChild = ImRaii.Child("GagLockChild", new Vector2(WindowMenuWidth, height), false))
            {
                if (!actionChild) return;

                bool disabled = selected == Padlocks.None || !PairPerms.GagFeatures;
                // Draw combo
                _permActions.DrawGenericComboButton(StickyPair.UserData.UID, "LockGagForPairPermCombo", "Lock",
                WindowMenuWidth, GenericHelpers.NoMimicPadlockList, (padlock) => padlock.ToName(), false, disabled, true, Padlocks.None,
                FontAwesomeIcon.Lock, ImGuiComboFlags.None, (selected) => { _logger.LogDebug("Selected Padlock: " + selected); },
                (onButtonPress) =>
                {
                    var newAppearance = StickyPair.LastAppearanceData.DeepClone();
                    if (newAppearance == null) throw new Exception("Appearance data is null or lock is invalid., not sending");
                    
                    _logger.LogDebug("Verifying lock for padlock: " + onButtonPress.ToName(), LoggerType.PadlockHandling);
                    var res = _permActions.PadlockVerifyLock<IPadlockable>(newAppearance.GagSlots[_permActions.GagLayer], onButtonPress,
                                    PairPerms.ExtendedLockTimes, PairPerms.OwnerLocks, PairPerms.DevotionalLocks, PairPerms.MaxLockTime);
                    if (res.Item1)
                    {
                        try
                        {
                            newAppearance.GagSlots[_permActions.GagLayer].Padlock = onButtonPress.ToName();
                            newAppearance.GagSlots[_permActions.GagLayer].Password = _permActions.Password;
                            newAppearance.GagSlots[_permActions.GagLayer].Timer = UiSharedService.GetEndTimeUTC(_permActions.Timer);
                            newAppearance.GagSlots[_permActions.GagLayer].Assigner = MainHub.UID;
                            _ = _apiHubMain.UserPushPairDataAppearanceUpdate(new(StickyPair.UserData, MainHub.PlayerUserData, newAppearance, (GagLayer)_permActions.GagLayer, GagUpdateType.GagLocked, Padlocks.None, UpdateDir.Other));
                            _logger.LogDebug("Locking Gag with GagPadlock "+onButtonPress.ToName()+" to "+PairNickOrAliasOrUID, LoggerType.Permissions);
                            Opened = InteractionType.None;
                        }
                        catch (Exception e) { _logger.LogError("Failed to push updated appearance data: " + e.Message); }
                    }
                    else
                    {
                        // Fire and forget, additional triggers replace previous.
                        _ = DisplayError(res.Item2);
                    }
                    _permActions.ResetInputs();
                });
                // draw password field combos.
                _permActions.DisplayPadlockFields(selected);
            }
            ImGui.Separator();
        }

        ////////// UNLOCK GAG //////////
        if (_uiShared.IconTextButton(FontAwesomeIcon.Unlock, ("Unlock " + PairNickOrAliasOrUID + "'s Gag"), WindowMenuWidth, true, disableUnlocking || !PairPerms.GagFeatures))
        {
            Opened = Opened == InteractionType.UnlockGag ? InteractionType.None : InteractionType.UnlockGag;
        }
        UiSharedService.AttachToolTip("Unlock " + PairUID + "'s Gag. Click to view options.");
        if (Opened is InteractionType.UnlockGag)
        {
            Padlocks selected = StickyPair.LastAppearanceData.GagSlots[_permActions.GagLayer].Padlock.ToPadlock();
            float height = _permActions.ExpandLockHeightCheck(selected) ? ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.Y : ImGui.GetFrameHeight();
            using (var actionChild = ImRaii.Child("GagUnlockChild", new Vector2(WindowMenuWidth, height), false))
            {
                if (!actionChild) return;

                bool disabled = selected == Padlocks.None || !PairPerms.GagFeatures;
                // Draw combo
                float width = WindowMenuWidth - ImGui.GetStyle().ItemInnerSpacing.X - _uiShared.GetIconTextButtonSize(FontAwesomeIcon.Unlock, "Unlock");
                ImGui.SetNextItemWidth(width);

                using (ImRaii.Disabled(true))
                {
                    if (ImGui.BeginCombo("##DummyComboDisplayLockedSet", StickyPair.LastAppearanceData.GagSlots[_permActions.GagLayer].Padlock ?? "Not Lock Active")) { ImGui.EndCombo(); }
                }

                ImUtf8.SameLineInner();
                if (_uiShared.IconTextButton(FontAwesomeIcon.Unlock, "Unlock", ImGui.GetContentRegionAvail().X, false, disabled))
                {
                    try
                    {
                        var newAppearance = StickyPair.LastAppearanceData.DeepClone();
                        if (newAppearance == null) throw new Exception("Appearance data is null or unlock is invalid. not sending");

                        _logger.LogDebug("Verifying password for padlock: " + selected.ToName() + "with password " + _permActions.Password, LoggerType.PadlockHandling);
                        var res = _permActions.PadlockVerifyUnlock<IPadlockable>(newAppearance.GagSlots[_permActions.GagLayer], selected, PairPerms.OwnerLocks, PairPerms.DevotionalLocks);
                        
                        if(res.Item1)
                        {
                            var prevLock = newAppearance.GagSlots[_permActions.GagLayer].Padlock.ToPadlock();
                            newAppearance.GagSlots[_permActions.GagLayer].Padlock = selected.ToName();
                            newAppearance.GagSlots[_permActions.GagLayer].Password = _permActions.Password;
                            newAppearance.GagSlots[_permActions.GagLayer].Timer = DateTimeOffset.UtcNow;
                            newAppearance.GagSlots[_permActions.GagLayer].Assigner = MainHub.UID;
                            _ = _apiHubMain.UserPushPairDataAppearanceUpdate(new(StickyPair.UserData, MainHub.PlayerUserData, newAppearance, (GagLayer)_permActions.GagLayer, GagUpdateType.GagUnlocked, prevLock, UpdateDir.Other));
                            _logger.LogDebug("Unlocking Gag with GagPadlock "+selected.ToName()+" to "+PairNickOrAliasOrUID, LoggerType.Permissions);
                            Opened = InteractionType.None;
                        }
                        else
                        {
                            // Fire and forget, additional triggers replace previous.
                            _ = DisplayError(res.Item2);
                        }
                        _permActions.ResetInputs();
                    }
                    catch (Exception e) { _logger.LogError("Failed to push updated appearance data: " + e.Message); }
                }
                // draw password field combos.
                _permActions.DisplayPadlockFields(selected, true);
            }
            ImGui.Separator();
        }

        ////////// REMOVE GAG //////////
        if (_uiShared.IconTextButton(FontAwesomeIcon.TimesCircle, ("Remove " + PairNickOrAliasOrUID + "'s Gag"), WindowMenuWidth, true,
        (disableRemoving || !PairPerms.GagFeatures || disableLocking)))
        {
            Opened = Opened == InteractionType.RemoveGag ? InteractionType.None : InteractionType.RemoveGag;
        }
        UiSharedService.AttachToolTip("Remove " + PairNickOrAliasOrUID + "'s Gag. Click to view options.");
        if (Opened is InteractionType.RemoveGag)
        {
            using (var actionChild = ImRaii.Child("GagRemoveChild", new Vector2(WindowMenuWidth, ImGui.GetFrameHeight()), false))
            {
                if (!actionChild) return;

                if (ImGui.Button("Remove Gag", ImGui.GetContentRegionAvail()))
                {
                    try
                    {
                        var newAppearance = StickyPair.LastAppearanceData.DeepClone();
                        if (newAppearance == null) throw new Exception("Appearance data is null, not sending");
                        newAppearance.GagSlots[_permActions.GagLayer].GagType = GagType.None.GagName();
                        _ = _apiHubMain.UserPushPairDataAppearanceUpdate(new(StickyPair.UserData, MainHub.PlayerUserData, newAppearance, (GagLayer)_permActions.GagLayer, GagUpdateType.GagRemoved, Padlocks.None, UpdateDir.Other));
                        _logger.LogDebug("Removing Gag from "+PairNickOrAliasOrUID, LoggerType.Permissions);
                        Opened = InteractionType.None;
                    }
                    catch (Exception e) { _logger.LogError("Failed to push updated appearance data: " + e.Message); }
                }
            }
        }
        ImGui.Separator();
    }
}
