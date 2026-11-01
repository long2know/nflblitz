using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal static class SourcePreGameSoundPrograms
    {
        internal static void Emit(SourcePreGameSoundService owner, SourcePreGameMenuWake wake, SourcePreGameMenuSound sound)
        {
            const string select = "lib/select/select.c", cap = "cap.c";
            string path = cap, function;
            int line, id, wait = 0;
            uint volume, priority;
            switch (sound)
            {
                case SourcePreGameMenuSound.NameCursor:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.Name, SourcePreGameMenuScreen.Pin);
                    SourceDigitalDirections direction = wake.ClosedDirections(wake.StationId);
                    line = (direction & SourceDigitalDirections.Up) != 0 ? 2840
                        : (direction & SourceDigitalDirections.Down) != 0 ? 2849
                        : (direction & SourceDigitalDirections.Left) != 0 ? 2863 : 2877;
                    path = select; function = "player_cursor"; id = 87; volume = 235; priority = 15; break;
                case SourcePreGameMenuSound.NameSelect:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.Name, SourcePreGameMenuScreen.Pin);
                    path = select; function = "player_cursor"; line = 2739;
                    id = 27; volume = 205; priority = 10; wait = 9; break;
                case SourcePreGameMenuSound.YesNoCursor:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.YesNo, SourcePreGameMenuScreen.OptionsConfirm);
                    path = select; id = 17; volume = 255; priority = 15;
                    function = wake.Screen == SourcePreGameMenuScreen.OptionsConfirm ? "plyr_select_option_proc" : "player_cursor";
                    line = wake.Screen == SourcePreGameMenuScreen.OptionsConfirm ? 2224
                        : (wake.ClosedDirections(wake.StationId) & SourceDigitalDirections.Up) != 0 ? 2658 : 2665;
                    break;
                case SourcePreGameMenuSound.RecordFound:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.RecordLookup, SourcePreGameMenuScreen.RecordStatus);
                    bool normal = wake.Process.Value.Function == "player_cursor";
                    path = normal ? select : cap; function = normal ? "player_cursor" : "show_record_status_msg";
                    line = normal ? 2932 : 1552; id = 80; volume = 205; priority = 5; break;
                case SourcePreGameMenuSound.CapCursor:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.CapActions);
                    bool up = (wake.ClosedDirections(wake.StationId) & SourceDigitalDirections.Up) != 0;
                    function = up ? "lcb_move_up" : "lcb_move_down"; line = up ? 1180 : 1191;
                    id = 86; volume = 235; priority = 15; break;
                case SourcePreGameMenuSound.CapSelect:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.CapActions);
                    function = "create_play_proc"; line = 1130; id = 30; volume = 160; priority = 10; wait = 21; break;
                case SourcePreGameMenuSound.FormationCursor:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.CapFormation);
                    function = "lcb_formation"; line = 2595; id = 87; volume = 235; priority = 15; break;
                case SourcePreGameMenuSound.FormationSelect:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.CapFormation);
                    function = "lcb_formation"; line = 2581; id = 91; volume = 205; priority = 10; break;
                case SourcePreGameMenuSound.RoutePoint:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.CapRoute);
                    function = "lcb_edit_route"; line = 2918; id = 89; volume = 235; priority = 15; break;
                case SourcePreGameMenuSound.RouteMenu:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.CapRouteMenu);
                    function = "er_menu"; line = 3530; id = 91; volume = 235; priority = 15; break;
                case SourcePreGameMenuSound.CapLetterCursor:
                case SourcePreGameMenuSound.CapLetterSelect:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.CapName, SourcePreGameMenuScreen.CapPin,
                        SourcePreGameMenuScreen.CapPlayName);
                    bool choose = sound == SourcePreGameMenuSound.CapLetterSelect;
                    function = wake.Screen == SourcePreGameMenuScreen.CapName ? "get_name"
                        : wake.Screen == SourcePreGameMenuScreen.CapPin ? "get_pin" : "lcb_name_play";
                    line = wake.Screen == SourcePreGameMenuScreen.CapName ? choose ? 1473 : 1506
                        : wake.Screen == SourcePreGameMenuScreen.CapPin ? choose ? 1623 : 1656 : choose ? 1378 : 1320;
                    id = choose ? 97 : 85; volume = 160; priority = 15; wait = choose ? 9 : 0; break;
                case SourcePreGameMenuSound.OptionCursor:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.Options);
                    path = select; function = "plyr_select_option_proc"; line = 2214;
                    id = 43; volume = 255; priority = 15; break;
                case SourcePreGameMenuSound.OptionSelect:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.Options, SourcePreGameMenuScreen.OptionsConfirm);
                    if (owner.CurrentOptionIndex() != 0)
                        throw new InvalidOperationException("The original option SELECT_SND1 belongs only to CREATE confirmation.");
                    path = select; function = "plyr_select_option_proc"; line = 2236;
                    id = 27; volume = 255; priority = 15; break;
                case SourcePreGameMenuSound.PlayCursor:
                case SourcePreGameMenuSound.PlaySelect:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.CapSlots, SourcePreGameMenuScreen.CapAudibles);
                    bool play = sound == SourcePreGameMenuSound.PlaySelect;
                    function = wake.Screen == SourcePreGameMenuScreen.CapAudibles ? "lcb_choose_audibles" : "lcb_create_edit";
                    line = wake.Screen == SourcePreGameMenuScreen.CapAudibles ? play ? 2049 : 2079 : play ? 1786 : 1811;
                    id = play ? 95 : 83; volume = 160; priority = 15; break;
                case SourcePreGameMenuSound.RouteMenuCursor:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.CapRouteMenu);
                    function = "er_menu"; line = (wake.ClosedDirections(wake.StationId) & SourceDigitalDirections.Up) != 0 ? 3536 : 3545;
                    id = 87; volume = 235; priority = 15; break;
                case SourcePreGameMenuSound.CapYesNoCursor:
                case SourcePreGameMenuSound.CapYesNoSelect:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.CapConfirm, SourcePreGameMenuScreen.CapAudibles);
                    bool yesno = sound == SourcePreGameMenuSound.CapYesNoSelect;
                    function = wake.Screen == SourcePreGameMenuScreen.CapAudibles ? "prompt_off_def" : "prompt_yn";
                    line = wake.Screen == SourcePreGameMenuScreen.CapAudibles ? yesno ? 3813 : 3822
                        : yesno ? 3911 : (wake.ClosedDirections(wake.StationId) & SourceDigitalDirections.Up) != 0 ? 3922 : 3931;
                    id = yesno ? 96 : 17; volume = yesno ? 205u : 235u; priority = 10; break;
                case SourcePreGameMenuSound.YesNoSelect:
                    owner.RequireScreen(wake, SourcePreGameMenuScreen.YesNo);
                    path = select; function = "flash_yes_no"; line = owner.CurrentYesAnswer(wake.StationId) ? 4042 : 4051;
                    id = 28; volume = 235; priority = 10; wait = 33; break;
                default: throw new ArgumentOutOfRangeException(nameof(sound));
            }
            if (path == cap && wake.Process.Value.Function != "create_play_proc"
                || path == select && function == "plyr_select_option_proc" && wake.Process.Value.Function != function
                || path == select && (function == "player_cursor" || function == "flash_yes_no")
                    && wake.Process.Value.Function != "player_cursor")
                throw new InvalidOperationException("The original menu callee requires its actual source process, not another screen's event.");
            owner.OriginalMenuCall(wake, sound, path, function, line, id, volume, priority, wait);
        }
    }
}
