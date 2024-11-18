using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AmongUs.GameOptions;
using Assets.CoreScripts;
using EHR.AddOns.Common;
using EHR.Crewmate;
using EHR.Impostor;
using EHR.Modules;
using EHR.Neutral;
using HarmonyLib;
using Hazel;
using UnityEngine;
using static EHR.Translator;

// ReSharper disable InconsistentNaming


namespace EHR
{
    internal class Command(string[] commandForms, string arguments, string description, Command.UsageLevels usageLevel, Command.UsageTimes usageTime, Action<ChatController, PlayerControl, string, string[]> action, bool isCanceled, string[] argsDescriptions = null)
    {
        public enum UsageLevels
        {
            Everyone,
            Modded,
            Host,
            HostOrModerator
        }

        public enum UsageTimes
        {
            Always,
            InLobby,
            InGame,
            InMeeting,
            AfterDeath,
            AfterDeathOrLobby
        }

        public string[] CommandForms => commandForms;
        public string Arguments => arguments;
        public string Description => description;
        public string[] ArgsDescriptions => argsDescriptions ?? [];
        private UsageLevels UsageLevel => usageLevel;
        private UsageTimes UsageTime => usageTime;
        public Action<ChatController, PlayerControl, string, string[]> Action => action;
        public bool IsCanceled => isCanceled;

        public bool IsThisCommand(string text)
        {
            if (!text.StartsWith('/')) return false;

            text = text.ToLower().Trim().TrimStart('/');
            return CommandForms.Any(text.Split(' ')[0].Equals);
        }

        public bool CanUseCommand(PlayerControl pc, bool checkTime = true)
        {
            if (UsageLevel == UsageLevels.Everyone && UsageTime == UsageTimes.Always && !Lovers.PrivateChat.GetBool()) return true;

            if (Lovers.PrivateChat.GetBool() && GameStates.IsInTask && pc.IsAlive()) return false;

            switch (UsageLevel)
            {
                case UsageLevels.Host when !pc.IsHost():
                case UsageLevels.Modded when !pc.IsModClient():
                case UsageLevels.HostOrModerator when !pc.IsHost() && !ChatCommands.IsPlayerModerator(pc.FriendCode):
                    return false;
            }

            if (!checkTime) return true;

            switch (UsageTime)
            {
                case UsageTimes.InLobby when !GameStates.IsLobby:
                case UsageTimes.InGame when !GameStates.InGame:
                case UsageTimes.InMeeting when !GameStates.IsMeeting:
                case UsageTimes.AfterDeath when pc.IsAlive():
                case UsageTimes.AfterDeathOrLobby when pc.IsAlive() && !GameStates.IsLobby:
                    return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    internal static class ChatCommands
    {
        public static readonly List<string> ChatHistory = [];
        public static readonly Dictionary<byte, long> LastSentCommand = [];
        public static HashSet<Command> AllCommands = [];

        private static readonly Dictionary<char, int> PollVotes = [];
        private static readonly Dictionary<char, string> PollAnswers = [];
        private static readonly List<byte> PollVoted = [];
        private static float PollTimer = 60f;

        public static readonly Dictionary<byte, (long MuteTimeStamp, int Duration)> MutedPlayers = [];

        public static Dictionary<byte, List<CustomRoles>> DraftRoles = [];
        public static Dictionary<byte, CustomRoles> DraftResult = [];

        private static HashSet<byte> ReadyPlayers = [];

        public static void LoadCommands()
        {
            AllCommands =
            [
                new(["lt", "лт"], "", GetString("CommandDescription.LT"), Command.UsageLevels.Everyone, Command.UsageTimes.InLobby, LTCommand, false),
                new(["dump", "дамп", "лог"], "", GetString("CommandDescription.Dump"), Command.UsageLevels.Modded, Command.UsageTimes.Always, DumpCommand, false),
                new(["v", "version", "в", "версия"], "", GetString("CommandDescription.Version"), Command.UsageLevels.Modded, Command.UsageTimes.Always, VersionCommand, false),
                new(["cs", "changesetting", "измнастр"], "{name} {?} [?]", GetString("CommandDescription.ChangeSetting"), Command.UsageLevels.Host, Command.UsageTimes.InLobby, ChangeSettingCommand, true, [GetString("CommandArgs.ChangeSetting.Name"), GetString("CommandArgs.ChangeSetting.UnknownValue"), GetString("CommandArgs.ChangeSetting.UnknownValue")]),
                new(["w", "win", "winner", "победители"], "", GetString("CommandDescription.Winner"), Command.UsageLevels.Everyone, Command.UsageTimes.InLobby, WinnerCommand, true),
                new(["l", "lastresult", "л"], "", GetString("CommandDescription.LastResult"), Command.UsageLevels.Everyone, Command.UsageTimes.InLobby, LastResultCommand, true),
                new(["rn", "rename", "рн", "ренейм", "переименовать"], "{name}", GetString("CommandDescription.Rename"), Command.UsageLevels.Everyone, Command.UsageTimes.InLobby, RenameCommand, true, [GetString("CommandArgs.Rename.Name")]),
                new(["hn", "hidename", "хн", "спрник"], "", GetString("CommandDescription.HideName"), Command.UsageLevels.Host, Command.UsageTimes.InLobby, HideNameCommand, true),
                new(["level", "лвл", "уровень"], "{level}", GetString("CommandDescription.Level"), Command.UsageLevels.Host, Command.UsageTimes.InLobby, LevelCommand, true, [GetString("CommandArgs.Level.Level")]),
                new(["n", "now", "н"], "", GetString("CommandDescription.Now"), Command.UsageLevels.Everyone, Command.UsageTimes.Always, NowCommand, true),
                new(["dis", "disconnect", "дис"], "{team}", GetString("CommandDescription.Disconnect"), Command.UsageLevels.Host, Command.UsageTimes.InGame, DisconnectCommand, true, [GetString("CommandArgs.Disconnect.Team")]),
                new(["r", "р"], "[role]", GetString("CommandDescription.R"), Command.UsageLevels.Everyone, Command.UsageTimes.Always, RCommand, true, [GetString("CommandArgs.R.Role")]),
                new(["up"], "{role}", GetString("CommandDescription.Up"), Command.UsageLevels.Host, Command.UsageTimes.InLobby, UpCommand, true, [GetString("CommandArgs.Up.Role")]),
                new(["setrole", "сетроль"], "{id} {role}", GetString("CommandDescription.SetRole"), Command.UsageLevels.Host, Command.UsageTimes.InLobby, SetRoleCommand, true, [GetString("CommandArgs.SetRole.Id"), GetString("CommandArgs.SetRole.Role")]),
                new(["h", "help", "хэлп", "хелп", "помощь"], "", GetString("CommandDescription.Help"), Command.UsageLevels.Everyone, Command.UsageTimes.Always, HelpCommand, true),
                new(["gamestate", "gstate", "gs", "kcount", "kc", "кубийц", "гс", "статигры"], "", GetString("CommandDescription.KCount"), Command.UsageLevels.Everyone, Command.UsageTimes.InGame, KCountCommand, true),
                new(["addmod", "добмодера"], "{id}", GetString("CommandDescription.AddMod"), Command.UsageLevels.Host, Command.UsageTimes.Always, AddModCommand, true, [GetString("CommandArgs.AddMod.Id")]),
                new(["deletemod", "убрмодера", "удмодера", "убратьмодера", "удалитьмодера"], "{id}", GetString("CommandDescription.DeleteMod"), Command.UsageLevels.Host, Command.UsageTimes.Always, DeleteModCommand, true, [GetString("CommandArgs.DeleteMod.Id")]),
                new(["combo", "комбо"], "{mode} {role} {addon} [all]", GetString("CommandDescription.Combo"), Command.UsageLevels.Host, Command.UsageTimes.Always, ComboCommand, true, [GetString("CommandArgs.Combo.Mode"), GetString("CommandArgs.Combo.Role"), GetString("CommandArgs.Combo.Addon"), GetString("CommandArgs.Combo.All")]),
                new(["eff", "effect", "эффект"], "{effect}", GetString("CommandDescription.Effect"), Command.UsageLevels.Host, Command.UsageTimes.InGame, EffectCommand, true, [GetString("CommandArgs.Effect.Effect")]),
                new(["afkexempt", "освафк", "афкосв"], "{id}", GetString("CommandDescription.AFKExempt"), Command.UsageLevels.Host, Command.UsageTimes.Always, AFKExemptCommand, true, [GetString("CommandArgs.AFKExempt.Id")]),
                new(["m", "myrole", "м", "мояроль"], "", GetString("CommandDescription.MyRole"), Command.UsageLevels.Everyone, Command.UsageTimes.InGame, MyRoleCommand, true),
                new(["tpout", "тпаут"], "", GetString("CommandDescription.TPOut"), Command.UsageLevels.Everyone, Command.UsageTimes.InLobby, TPOutCommand, true),
                new(["tpin", "тпин"], "", GetString("CommandDescription.TPIn"), Command.UsageLevels.Everyone, Command.UsageTimes.InLobby, TPInCommand, true),
                new(["t", "template", "т", "темплейт"], "{tag}", GetString("CommandDescription.Template"), Command.UsageLevels.Everyone, Command.UsageTimes.Always, TemplateCommand, true, [GetString("CommandArgs.Template.Tag")]),
                new(["mw", "messagewait", "мв", "медленныйрежим"], "{duration}", GetString("CommandDescription.MessageWait"), Command.UsageLevels.Host, Command.UsageTimes.Always, MessageWaitCommand, true, [GetString("CommandArgs.MessageWait.Duration")]),
                new(["death", "d", "д", "смерть"], "", GetString("CommandDescription.Death"), Command.UsageLevels.Everyone, Command.UsageTimes.AfterDeath, DeathCommand, true),
                new(["say", "s", "сказать", "с"], "{message}", GetString("CommandDescription.Say"), Command.UsageLevels.HostOrModerator, Command.UsageTimes.Always, SayCommand, true, [GetString("CommandArgs.Say.Message")]),
                new(["vote", "голос"], "{id}", GetString("CommandDescription.Vote"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, VoteCommand, true, [GetString("CommandArgs.Vote.Id")]),
                new(["ask", "спр", "спросить"], "{number1} {number2}", GetString("CommandDescription.Ask"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, AskCommand, true, [GetString("CommandArgs.Ask.Number1"), GetString("CommandArgs.Ask.Number2")]),
                new(["ans", "answer", "отв", "ответить"], "{number}", GetString("CommandDescription.Answer"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, AnswerCommand, true, [GetString("CommandArgs.Answer.Number")]),
                new(["qa", "вопротв"], "{letter}", GetString("CommandDescription.QA"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, QACommand, true, [GetString("CommandArgs.QA.Letter")]),
                new(["qs", "вопрпоказать"], "", GetString("CommandDescription.QS"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, QSCommand, true),
                new(["target", "цель"], "{id}", GetString("CommandDescription.Target"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, TargetCommand, true, [GetString("CommandArgs.Target.Id")]),
                new(["chat", "сообщение"], "{message}", GetString("CommandDescription.Chat"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, ChatCommand, true, [GetString("CommandArgs.Chat.Message")]),
                new(["check", "проверить"], "{id} {role}", GetString("CommandDescription.Check"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, CheckCommand, true, [GetString("CommandArgs.Check.Id"), GetString("CommandArgs.Check.Role")]),
                new(["ban", "kick", "бан", "кик", "забанить", "кикнуть"], "{id}", GetString("CommandDescription.Ban"), Command.UsageLevels.HostOrModerator, Command.UsageTimes.Always, BanKickCommand, true, [GetString("CommandArgs.Ban.Id")]),
                new(["exe", "выкинуть"], "{id}", GetString("CommandDescription.Exe"), Command.UsageLevels.Host, Command.UsageTimes.Always, ExeCommand, true, [GetString("CommandArgs.Exe.Id")]),
                new(["kill", "убить"], "{id}", GetString("CommandDescription.Kill"), Command.UsageLevels.Host, Command.UsageTimes.Always, KillCommand, true, [GetString("CommandArgs.Kill.Id")]),
                new(["colour", "color", "цвет"], "{color}", GetString("CommandDescription.Colour"), Command.UsageLevels.Everyone, Command.UsageTimes.InLobby, ColorCommand, true, [GetString("CommandArgs.Colour.Color")]),
                new(["xf", "испр"], "", GetString("CommandDescription.XF"), Command.UsageLevels.Everyone, Command.UsageTimes.Always, XFCommand, true),
                new(["id", "guesslist", "айди"], "", GetString("CommandDescription.ID"), Command.UsageLevels.Everyone, Command.UsageTimes.Always, IDCommand, true),
                new(["changerole", "измроль"], "{role}", GetString("CommandDescription.ChangeRole"), Command.UsageLevels.Host, Command.UsageTimes.InGame, ChangeRoleCommand, true, [GetString("CommandArgs.ChangeRole.Role")]),
                new(["end", "завершить"], "", GetString("CommandDescription.End"), Command.UsageLevels.Host, Command.UsageTimes.InGame, EndCommand, true),
                new(["cosid", "костюм", "одежда"], "", GetString("CommandDescription.CosID"), Command.UsageLevels.Modded, Command.UsageTimes.Always, CosIDCommand, true),
                new(["mt", "hy", "собрание"], "", GetString("CommandDescription.MTHY"), Command.UsageLevels.Host, Command.UsageTimes.InGame, MTHYCommand, true),
                new(["csd", "кзвук"], "{sound}", GetString("CommandDescription.CSD"), Command.UsageLevels.Modded, Command.UsageTimes.Always, CSDCommand, true, [GetString("CommandArgs.CSD.Sound")]),
                new(["sd", "взвук"], "{sound}", GetString("CommandDescription.SD"), Command.UsageLevels.Modded, Command.UsageTimes.Always, SDCommand, true, [GetString("CommandArgs.SD.Sound")]),
                new(["gno", "гно"], "{number}", GetString("CommandDescription.GNO"), Command.UsageLevels.Everyone, Command.UsageTimes.AfterDeathOrLobby, GNOCommand, true, [GetString("CommandArgs.GNO.Number")]),
                new(["poll", "опрос"], "{question} {answerA} {answerB} [answerC] [answerD]", GetString("CommandDescription.Poll"), Command.UsageLevels.HostOrModerator, Command.UsageTimes.Always, PollCommand, true, [GetString("CommandArgs.Poll.Question"), GetString("CommandArgs.Poll.AnswerA"), GetString("CommandArgs.Poll.AnswerB"), GetString("CommandArgs.Poll.AnswerC"), GetString("CommandArgs.Poll.AnswerD")]),
                new(["pv", "проголосовать"], "{vote}", GetString("CommandDescription.PV"), Command.UsageLevels.Everyone, Command.UsageTimes.Always, PVCommand, false, [GetString("CommandArgs.PV.Vote")]),
                new(["hm", "мс", "мессенджер"], "{id}", GetString("CommandDescription.HM"), Command.UsageLevels.Everyone, Command.UsageTimes.AfterDeath, HMCommand, true, [GetString("CommandArgs.HM.Id")]),
                new(["decree", "указ"], "{number}", GetString("CommandDescription.Decree"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, DecreeCommand, true, [GetString("CommandArgs.Decree.Number")]),
                new(["addvip", "добавитьвип", "добвип"], "{id}", GetString("CommandDescription.AddVIP"), Command.UsageLevels.Host, Command.UsageTimes.Always, AddVIPCommand, true, [GetString("CommandArgs.AddVIP.Id")]),
                new(["deletevip", "удвип", "убрвип", "удалитьвип", "убратьвип"], "{id}", GetString("CommandDescription.DeleteVIP"), Command.UsageLevels.Host, Command.UsageTimes.Always, DeleteVIPCommand, true, [GetString("CommandArgs.DeleteVIP.Id")]),
                new(["assume", "предположить"], "{id} {number}", GetString("CommandDescription.Assume"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, AssumeCommand, true, [GetString("CommandArgs.Assume.Id"), GetString("CommandArgs.Assume.Number")]),
                new(["note", "заметка"], "{action} [?]", GetString("CommandDescription.Note"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, NoteCommand, true, [GetString("CommandArgs.Note.Action"), GetString("CommandArgs.Note.UnknownValue")]),
                new(["os", "optionset", "шансроли"], "{chance} {role}", GetString("CommandDescription.OS"), Command.UsageLevels.Host, Command.UsageTimes.InLobby, OSCommand, true, [GetString("CommandArgs.OS.Chance"), GetString("CommandArgs.OS.Role")]),
                new(["negotiation", "neg", "наказание"], "{number}", GetString("CommandDescription.Negotiation"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, NegotiationCommand, true, [GetString("CommandArgs.Negotiation.Number")]),
                new(["mute", "мут"], "{id} [duration]", GetString("CommandDescription.Mute"), Command.UsageLevels.HostOrModerator, Command.UsageTimes.AfterDeathOrLobby, MuteCommand, true, [GetString("CommandArgs.Mute.Id"), GetString("CommandArgs.Mute.Duration")]),
                new(["unmute", "размут"], "{id}", GetString("CommandDescription.Unmute"), Command.UsageLevels.Host, Command.UsageTimes.Always, UnmuteCommand, true, [GetString("CommandArgs.Unmute.Id")]),
                new(["draftstart", "ds", "драфтстарт"], "", GetString("CommandDescription.DraftStart"), Command.UsageLevels.Host, Command.UsageTimes.InLobby, DraftStartCommand, true),
                new(["draft", "драфт"], "{number}", GetString("CommandDescription.Draft"), Command.UsageLevels.Everyone, Command.UsageTimes.InLobby, DraftCommand, true, [GetString("CommandArgs.Draft.Number")]),
                new(["readycheck", "rc", "проверитьготовность"], "", GetString("CommandDescription.ReadyCheck"), Command.UsageLevels.Host, Command.UsageTimes.InLobby, ReadyCheckCommand, true),
                new(["ready", "готов"], "", GetString("CommandDescription.Ready"), Command.UsageLevels.Everyone, Command.UsageTimes.InLobby, ReadyCommand, true),
                new(["enableallroles", "всероли"], "", GetString("CommandDescription.EnableAllRoles"), Command.UsageLevels.Host, Command.UsageTimes.InLobby, EnableAllRolesCommand, true),
                new(["achievements", "достижения"], "", GetString("CommandDescription.Achievements"), Command.UsageLevels.Modded, Command.UsageTimes.Always, AchievementsCommand, true),
                new(["dn", "deathnote", "заметкамертвого"], "{name}", GetString("CommandDescription.DeathNote"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, DeathNoteCommand, true, [GetString("CommandArgs.DeathNote.Name")]),

                // Commands with action handled elsewhere
                new(["shoot", "guess", "bet", "bt", "st", "угадать", "бт"], "{id} {role}", GetString("CommandDescription.Guess"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, (_, _, _, _) => { }, true, [GetString("CommandArgs.Guess.Id"), GetString("CommandArgs.Guess.Role")]),
                new(["tl", "sp", "jj", "trial", "суд", "засудить"], "{id}", GetString("CommandDescription.Trial"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, (_, _, _, _) => { }, true, [GetString("CommandArgs.Trial.Id")]),
                new(["sw", "swap", "st", "свап", "свапнуть"], "{id}", GetString("CommandDescription.Swap"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, (_, _, _, _) => { }, true, [GetString("CommandArgs.Swap.Id")]),
                new(["compare", "cp", "cmp", "сравнить", "ср"], "{id1} {id2}", GetString("CommandDescription.Compare"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, (_, _, _, _) => { }, true, [GetString("CommandArgs.Compare.Id1"), GetString("CommandArgs.Compare.Id2")]),
                new(["ms", "mediumship", "medium", "медиум"], "{answer}", GetString("CommandDescription.Medium"), Command.UsageLevels.Everyone, Command.UsageTimes.InMeeting, (_, _, _, _) => { }, true, [GetString("CommandArgs.Medium.Answer")]),
                new(["rv", "месть", "отомстить"], "{id}", GetString("CommandDescription.Revenge"), Command.UsageLevels.Everyone, Command.UsageTimes.AfterDeath, (_, _, _, _) => { }, true, [GetString("CommandArgs.Revenge.Id")])
            ];
        }

        // Function to check if a player is a moderator
        public static bool IsPlayerModerator(string friendCode)
        {
            if (friendCode == "" || friendCode == string.Empty || !Options.ApplyModeratorList.GetBool()) return false;

            const string friendCodesFilePath = "./EHR_DATA/Moderators.txt";

            if (!File.Exists(friendCodesFilePath))
            {
                File.WriteAllText(friendCodesFilePath, string.Empty);
                return false;
            }

            string[] friendCodes = File.ReadAllLines(friendCodesFilePath);
            return friendCodes.Any(code => code.Contains(friendCode, StringComparison.OrdinalIgnoreCase));
        }

        // Function to check if a player is a VIP
        public static bool IsPlayerVIP(string friendCode)
        {
            if (friendCode == "" || friendCode == string.Empty || !Options.ApplyVIPList.GetBool()) return false;

            const string friendCodesFilePath = "./EHR_DATA/VIPs.txt";

            if (!File.Exists(friendCodesFilePath))
            {
                File.WriteAllText(friendCodesFilePath, string.Empty);
                return false;
            }

            string[] friendCodes = File.ReadAllLines(friendCodesFilePath);
            return friendCodes.Any(code => code.Contains(friendCode, StringComparison.OrdinalIgnoreCase));
        }

        public static bool Prefix(ChatController __instance)
        {
            if (__instance.quickChatField.visible) return true;

            if (__instance.freeChatField.textArea.text == string.Empty) return false;

            __instance.timeSinceLastMessage = 3f;

            string text = __instance.freeChatField.textArea.text.Trim();

            if (ChatHistory.Count == 0 || ChatHistory[^1] != text) ChatHistory.Add(text);

            ChatControllerUpdatePatch.CurrentHistorySelection = ChatHistory.Count;

            string[] args = text.Split(' ');
            var canceled = false;
            var cancelVal = string.Empty;
            Main.IsChatCommand = true;

            Logger.Info(text, "SendChat");

            ChatManager.SendMessage(PlayerControl.LocalPlayer, text);

            if (GuessManager.GuesserMsg(PlayerControl.LocalPlayer, text)) goto Canceled;
            if (Judge.TrialMsg(PlayerControl.LocalPlayer, text)) goto Canceled;
            if (NiceSwapper.SwapMsg(PlayerControl.LocalPlayer, text)) goto Canceled;
            if (ParityCop.ParityCheckMsg(PlayerControl.LocalPlayer, text)) goto Canceled;
            if (Councillor.MurderMsg(PlayerControl.LocalPlayer, text)) goto Canceled;
            if (Mediumshiper.MsMsg(PlayerControl.LocalPlayer, text)) goto Canceled;
            if (Mafia.MafiaMsgCheck(PlayerControl.LocalPlayer, text)) goto Canceled;

            Main.IsChatCommand = false;

            if (text.StartsWith('/'))
            {
                foreach (Command command in AllCommands)
                {
                    if (!command.IsThisCommand(text)) continue;

                    Logger.Info($" Recognized command: {text}", "ChatCommand");
                    Main.IsChatCommand = true;

                    if (!command.CanUseCommand(PlayerControl.LocalPlayer))
                    {
                        Utils.SendMessage(GetString("Commands.NoAccess"), PlayerControl.LocalPlayer.PlayerId);
                        goto Canceled;
                    }

                    command.Action(__instance, PlayerControl.LocalPlayer, text, args);
                    if (command.IsCanceled) goto Canceled;

                    break;
                }

                Statistics.HasUsedAnyCommand = true;
            }

            if (CheckMute(PlayerControl.LocalPlayer.PlayerId)) goto Canceled;

            if (GameStates.InGame && (Silencer.ForSilencer.Contains(PlayerControl.LocalPlayer.PlayerId) || (Main.PlayerStates[PlayerControl.LocalPlayer.PlayerId].Role is Dad { IsEnable: true } dad && dad.UsingAbilities.Contains(Dad.Ability.GoForMilk))) && PlayerControl.LocalPlayer.IsAlive()) goto Canceled;

            if (GameStates.IsInGame && (PlayerControl.LocalPlayer.IsAlive() || ExileController.Instance) && Lovers.PrivateChat.GetBool() && (ExileController.Instance || !GameStates.IsMeeting))
            {
                if (PlayerControl.LocalPlayer.Is(CustomRoles.Lovers) || PlayerControl.LocalPlayer.GetCustomRole() is CustomRoles.LovingCrewmate or CustomRoles.LovingImpostor)
                {
                    PlayerControl otherLover = Main.LoversPlayers.First(x => x.PlayerId != PlayerControl.LocalPlayer.PlayerId);
                    string title = PlayerControl.LocalPlayer.GetRealName();
                    ChatUpdatePatch.LoversMessage = true;
                    Utils.SendMessage(text, otherLover.PlayerId, title);
                    Utils.SendMessage(text, PlayerControl.LocalPlayer.PlayerId, title);
                    LateTask.New(() => ChatUpdatePatch.LoversMessage = false, Math.Max(AmongUsClient.Instance.Ping / 1000f * 2f, Main.MessageWait.Value + 0.5f), log: false);
                }

                goto Canceled;
            }

            goto Skip;
            Canceled:
            Main.IsChatCommand = false;
            canceled = true;
            Skip:

            if (canceled)
            {
                Logger.Info("Command Canceled", "ChatCommand");
                __instance.freeChatField.textArea.Clear();
                __instance.freeChatField.textArea.SetText(cancelVal);
            }

            if (text.Contains("666") && PlayerControl.LocalPlayer.Is(CustomRoles.Gamer))
                Achievements.Type.WhatTheHell.CompleteAfterGameEnd();

            return !canceled;
        }

        private static void RequestCommandProcessingFromHost(string methodName, string text)
        {
            PlayerControl pc = PlayerControl.LocalPlayer;
            MessageWriter w = AmongUsClient.Instance.StartRpc(pc.NetId, (byte)CustomRPC.RequestCommandProcessing);
            w.Write(methodName);
            w.Write(pc.PlayerId);
            w.Write(text);
            w.EndMessage();
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------

        private static void DeathNoteCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(DeathNoteCommand), text);
                return;
            }

            if (!player.Is(CustomRoles.NoteKiller) || args.Length < 2) return;

            if (!player.IsLocalPlayer()) ChatManager.SendPreviousMessagesToAll();

            var guess = args[1].ToLower();
            guess = char.ToUpper(guess[0]) + guess[1..];
            var deadPlayer = NoteKiller.RealNames.GetKeyByValue(guess);

            if (deadPlayer == default && (!NoteKiller.RealNames.TryGetValue(default, out var name) || name != guess))
            {
                Utils.SendMessage(GetString("DeathNoteCommand.WrongName"), player.PlayerId);
                return;
            }

            var pc = deadPlayer.GetPlayer();

            if (pc == null || !pc.IsAlive())
            {
                Utils.SendMessage(GetString("DeathNoteCommand.PlayerNotFoundOrDead"), player.PlayerId);
                return;
            }

            var state = Main.PlayerStates[pc.PlayerId];
            state.deathReason = PlayerState.DeathReason.Kill;
            state.RealKiller.ID = player.PlayerId;
            state.SetDead();

            pc.RpcExileV2();
            SoundManager.Instance.PlaySound(pc.KillSfx, false, 0.8f);

            Utils.SendMessage("\n", player.PlayerId, string.Format(GetString("DeathNoteCommand.Success"), deadPlayer.ColoredPlayerName()));
            Utils.SendMessage(string.Format(GetString("DeathNoteCommand.SuccessForOthers"), deadPlayer.ColoredPlayerName()));

            NoteKiller.Kills++;
        }

        private static void AchievementsCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            Func<Achievements.Type, string> ToAchievementString = x => $"<b>{GetString($"Achievement.{x}")}</b> - {GetString($"Achievement.{x}.Description")}";

            Achievements.Type[] allAchievements = Enum.GetValues<Achievements.Type>();
            Achievements.Type[] union = Achievements.CompletedAchievements.Union(Achievements.WaitingAchievements).ToArray();
            string completedAchievements = $"<size=70%>{union.Join(ToAchievementString, "\n")}</size>";
            string incompleteAchievements = $"<size=70%>{allAchievements.Except(union).Join(ToAchievementString, "\n")}</size>";

            Utils.SendMessage(incompleteAchievements, player.PlayerId, GetString("IncompleteAchievementsTitle"));
            Utils.SendMessage(completedAchievements, player.PlayerId, GetString("CompletedAchievementsTitle") + $" <#00a5ff>(<#00ffa5>{union.Length}</color>/{allAchievements.Length})</color>");
        }

        private static void EnableAllRolesCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            Options.CustomRoleSpawnChances.Values.DoIf(x => x.GetValue() == 0, x => x.SetValue(1));
        }

        private static void ReadyCheckCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            Utils.SendMessage(GetString("ReadyCheckMessage"), title: GetString("ReadyCheckTitle"));
            ReadyPlayers = [player.PlayerId];
            Main.Instance.StartCoroutine(Countdown());
            return;

            IEnumerator Countdown()
            {
                var timer = 30f;

                while (timer > 0f)
                {
                    if (!GameStates.IsLobby) yield break;

                    if (Main.AllPlayerControls.Select(x => x.PlayerId).All(ReadyPlayers.Contains)) break;

                    timer -= Time.deltaTime;
                    yield return null;
                }

                byte[] notReadyPlayers = Main.AllPlayerControls.Select(x => x.PlayerId).Except(ReadyPlayers).ToArray();

                if (notReadyPlayers.Length == 0)
                    Utils.SendMessage("\n", player.PlayerId, GetString("EveryoneReadyTitle"));
                else
                    Utils.SendMessage(string.Join(", ", notReadyPlayers.Select(x => x.ColoredPlayerName())), player.PlayerId, string.Format(GetString("PlayersNotReadyTitle"), notReadyPlayers.Length));
            }
        }

        private static void ReadyCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            ReadyPlayers.Add(player.PlayerId);
        }

        private static void DraftStartCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            DraftResult = [];

            byte[] allPlayerIds = Main.AllPlayerControls.Select(x => x.PlayerId).ToArray();
            List<CustomRoles> allRoles = Enum.GetValues<CustomRoles>().Where(x => x < CustomRoles.NotAssigned && x.IsEnable() && !x.IsForOtherGameMode() && !HnSManager.AllHnSRoles.Contains(x) && !x.IsVanilla() && x is not CustomRoles.GM and not CustomRoles.Konan).ToList();

            if (allRoles.Count < allPlayerIds.Length)
            {
                Utils.SendMessage(GetString("DraftNotEnoughRoles"), player.PlayerId);
                return;
            }

            IEnumerable<CustomRoles> impRoles = allRoles.Where(x => x.IsImpostor()).Shuffle().Take(Main.NormalOptions.NumImpostors);
            IEnumerable<CustomRoles> nkRoles = allRoles.Where(x => x.IsNK()).Shuffle().Take(Options.NeutralKillingRolesMaxPlayer.GetInt());
            IEnumerable<CustomRoles> nnkRoles = allRoles.Where(x => x.IsNonNK()).Shuffle().Take(Options.NonNeutralKillingRolesMaxPlayer.GetInt());

            allRoles.RemoveAll(x => x.IsImpostor());
            allRoles.RemoveAll(x => x.IsNK());
            allRoles.RemoveAll(x => x.IsNonNK());

            DraftRoles = allRoles.CombineWith(impRoles, nkRoles, nnkRoles).Shuffle().Partition(allPlayerIds.Length).Zip(allPlayerIds).ToDictionary(x => x.Second, x => x.First.Take(5).ToList());

            foreach ((byte id, List<CustomRoles> roles) in DraftRoles)
            {
                IEnumerable<string> roleList = roles.Select((x, i) => $"{i + 1}. {x.ToColoredString()}");
                string msg = string.Format(GetString("DraftStart"), string.Join('\n', roleList));
                Utils.SendMessage(msg, id, GetString("DraftTitle"));
            }
        }

        private static void DraftCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (DraftRoles.Count == 0 || !DraftRoles.TryGetValue(player.PlayerId, out List<CustomRoles> roles) || args.Length < 2 || !int.TryParse(args[1], out int chosenIndex) || roles.Count < chosenIndex) return;

            CustomRoles role = roles[chosenIndex - 1];
            DraftResult[player.PlayerId] = role;
            Utils.SendMessage(string.Format(GetString("DraftChosen"), role.ToColoredString()), player.PlayerId, GetString("DraftTitle"));
        }

        private static void MuteCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!player.IsHost() && (GameStates.InGame || MutedPlayers.ContainsKey(player.PlayerId))) return;

            if (args.Length < 3 || !byte.TryParse(args[1], out byte id) || id.IsHost()) return;

            int duration = !int.TryParse(args[2], out int dur) ? 60 : dur;
            MutedPlayers[id] = (Utils.TimeStamp, duration);
            Utils.SendMessage("\n", player.PlayerId, string.Format(GetString("PlayerMuted"), id.ColoredPlayerName(), duration));
            Utils.SendMessage("\n", id, string.Format(GetString("YouMuted"), player.PlayerId.ColoredPlayerName(), duration));
            if (!player.IsHost()) Utils.SendMessage("\n", 0, string.Format(GetString("ModeratorMuted"), player.PlayerId.ColoredPlayerName(), id.ColoredPlayerName(), duration));
        }

        private static void UnmuteCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (args.Length < 2 || !byte.TryParse(args[1], out byte id)) return;

            MutedPlayers.Remove(id);
            Utils.SendMessage("\n", player.PlayerId, string.Format(GetString("PlayerUnmuted"), id.ColoredPlayerName()));
            Utils.SendMessage("\n", id, string.Format(GetString("YouUnmuted"), player.PlayerId.ColoredPlayerName()));
        }

        private static void NegotiationCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(NegotiationCommand), text);
                return;
            }

            if (!Negotiator.On || !player.IsAlive() || args.Length < 2 || !int.TryParse(args[1], out int index)) return;

            Negotiator.ReceiveCommand(player, index);
        }

        private static void OSCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!GameStates.IsLobby || args.Length < 3 || !byte.TryParse(args[1], out byte chance) || chance > 100 || chance % 5 != 0 || !GetRoleByName(string.Join(' ', args[2..]), out CustomRoles role) || !Options.CustomRoleSpawnChances.TryGetValue(role, out StringOptionItem option)) return;

            if (role.IsAdditionRole())
            {
                option.SetValue(chance == 0 ? 0 : 1);
                if (!Options.CustomAdtRoleSpawnRate.TryGetValue(role, out IntegerOptionItem adtOption)) return;

                adtOption.SetValue(chance / 5);
            }
            else
                option.SetValue(chance / 5);
        }

        private static void NoteCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(NoteCommand), text);
                return;
            }

            if (player.Is(CustomRoles.Journalist) && player.IsAlive())
            {
                if (PlayerControl.LocalPlayer.PlayerId != player.PlayerId) ChatManager.SendPreviousMessagesToAll();

                Journalist.OnReceiveCommand(player, args);
            }
        }

        private static void AssumeCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(AssumeCommand), text);
                return;
            }

            if (args.Length < 3 || !byte.TryParse(args[1], out byte id) || !int.TryParse(args[2], out int num) || !player.Is(CustomRoles.Assumer) || !player.IsAlive()) return;

            if (PlayerControl.LocalPlayer.PlayerId != player.PlayerId) ChatManager.SendPreviousMessagesToAll();

            Assumer.Assume(player.PlayerId, id, num);
        }

        private static void DeleteVIPCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (args.Length < 2 || !byte.TryParse(args[1], out byte VIPId)) return;

            PlayerControl VIPPc = Utils.GetPlayerById(VIPId);
            if (VIPPc == null) return;

            string fc = VIPPc.FriendCode;
            if (!IsPlayerVIP(fc)) Utils.SendMessage(GetString("PlayerNotVIP"), player.PlayerId);

            string[] lines = File.ReadAllLines("./EHR_DATA/VIPs.txt").Where(line => !line.Contains(fc)).ToArray();
            File.WriteAllLines("./EHR_DATA/VIPs.txt", lines);
            Utils.SendMessage(GetString("PlayerRemovedFromVIPList"), player.PlayerId);
        }

        private static void AddVIPCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (args.Length < 2 || !byte.TryParse(args[1], out byte newVIPId)) return;

            PlayerControl newVIPPc = Utils.GetPlayerById(newVIPId);
            if (newVIPPc == null) return;

            string fc = newVIPPc.FriendCode;
            if (IsPlayerVIP(fc)) Utils.SendMessage(GetString("PlayerAlreadyVIP"), player.PlayerId);

            File.AppendAllText("./EHR_DATA/VIPs.txt", $"\n{fc}");
            Utils.SendMessage(GetString("PlayerAddedToVIPList"), player.PlayerId);
        }

        private static void DecreeCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(DecreeCommand), text);
                return;
            }

            if (!player.Is(CustomRoles.President)) return;

            if (player.PlayerId != PlayerControl.LocalPlayer.PlayerId) ChatManager.SendPreviousMessagesToAll();

            LateTask.New(() =>
            {
                if (args.Length < 2)
                {
                    Utils.SendMessage(President.GetHelpMessage(), player.PlayerId);
                    return;
                }

                President.UseDecree(player, args[1]);
            }, 0.2f, log: false);
        }

        private static void HMCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(HMCommand), text);
                return;
            }

            if (Messenger.Sent.Contains(player.PlayerId) || args.Length < 2 || !int.TryParse(args[1], out int id) || id is > 3 or < 1) return;

            Main.Instance.StartCoroutine(SendOnMeeting());
            return;

            IEnumerator SendOnMeeting()
            {
                bool meeting = GameStates.IsMeeting;
                while (!GameStates.IsMeeting) yield return null;

                if (!meeting) yield return new WaitForSeconds(7f);

                PlayerControl killer = player.GetRealKiller();
                if (killer == null && id != 3) yield break;

                Team team = player.GetTeam();

                string message = id switch
                {
                    1 => string.Format(GetString("MessengerMessage.1"), GetString(Main.PlayerStates[killer.PlayerId].LastRoom.RoomId.ToString())),
                    2 => string.Format(GetString("MessengerMessage.2"), killer.GetCustomRole().ToColoredString()),
                    _ => string.Format(GetString("MessengerMessage.3"), Utils.ColorString(team.GetTeamColor(), GetString($"{team}")))
                };

                Utils.SendMessage(message, title: string.Format(GetString("MessengerTitle"), player.PlayerId.ColoredPlayerName()));
                Messenger.Sent.Add(player.PlayerId);
            }
        }

        // Credit: Drakos for the base code
        private static void PollCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            PollVotes.Clear();
            PollAnswers.Clear();
            PollVoted.Clear();

            if (!args.Any(x => x.Contains('?')))
            {
                Utils.SendMessage(GetString("PollUsage"), player.PlayerId);
                return;
            }

            PollTimer = 60f;

            int splitIndex = Array.IndexOf(args, args.First(x => x.Contains('?'))) + 1;
            string[] answers = args.Skip(splitIndex).ToArray();

            string msg = string.Join(" ", args.Take(splitIndex).Skip(1)) + "\n";

            for (var i = 0; i < Math.Clamp(answers.Length, 2, 5); i++)
            {
                var choiceLetter = (char)(i + 65);
                msg += Utils.ColorString(RandomColor(), $"{char.ToUpper(choiceLetter)}) {answers[i]}\n");
                PollVotes[choiceLetter] = 0;
                PollAnswers[choiceLetter] = $"<size=45%>〖 {answers[i]} 〗</size>";
            }

            msg += $"\n{GetString("Poll.Begin")}\n<size=55%><i>{GetString("Poll.TimeInfo")}</i></size>";
            string title = GetString("Poll.Title");
            Utils.SendMessage(msg, title: title);

            Main.Instance.StartCoroutine(StartPollCountdown());
            return;

            IEnumerator StartPollCountdown()
            {
                if (PollVotes.Count == 0) yield break;

                bool playervoted = Main.AllPlayerControls.Length - 1 > PollVotes.Values.Sum();

                var resendTimer = 0f;

                while (playervoted && PollTimer > 0f)
                {
                    playervoted = Main.AllPlayerControls.Length - 1 > PollVotes.Values.Sum();
                    PollTimer -= Time.deltaTime;
                    resendTimer += Time.deltaTime;

                    if (resendTimer >= 15f)
                    {
                        resendTimer = 0f;
                        Utils.SendMessage(msg, title: title);
                    }

                    yield return null;
                }

                DetermineResults();
            }

            static void DetermineResults()
            {
                int maxVotes = PollVotes.Values.Max();
                KeyValuePair<char, int>[] winners = PollVotes.Where(x => x.Value == maxVotes).ToArray();

                string msg = winners.Length == 1
                    ? string.Format(GetString("Poll.Winner"), winners[0].Key, PollAnswers[winners[0].Key], winners[0].Value) +
                      PollVotes.Where(x => x.Key != winners[0].Key).Aggregate("", (s, t) => s + $"{t.Key} / {t.Value} {PollAnswers[t.Key]}\n")
                    : string.Format(GetString("Poll.Tie"), string.Join(" & ", winners.Select(x => $"{x.Key}{PollAnswers[x.Key]}")), maxVotes);

                Utils.SendMessage(msg, title: Utils.ColorString(new(0, 255, 165, 255), GetString("PollResultTitle")));

                PollVotes.Clear();
                PollAnswers.Clear();
                PollVoted.Clear();
            }

            static Color32 RandomColor()
            {
                byte[] colors = IRandom.Sequence(3, 0, 160).Select(x => (byte)x).ToArray();
                return new(colors[0], colors[1], colors[2], 255);
            }
        }

        private static void PVCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (PollVotes.Count == 0)
            {
                Utils.SendMessage(GetString("Poll.Inactive"), player.PlayerId);
                return;
            }

            if (PollVoted.Contains(player.PlayerId))
            {
                Utils.SendMessage(GetString("Poll.AlreadyVoted"), player.PlayerId);
                return;
            }

            if (args.Length != 2 || !char.TryParse(args[1], out char vote) || !PollVotes.ContainsKey(char.ToUpper(vote)))
            {
                Utils.SendMessage(GetString("Poll.VotingInfo"), player.PlayerId);
                return;
            }

            vote = char.ToUpper(vote);

            PollVoted.Add(player.PlayerId);
            PollVotes[vote]++;
            Utils.SendMessage(string.Format(GetString("Poll.YouVoted"), vote, PollVotes[vote]), player.PlayerId);
        }

        private static void HelpCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            Utils.ShowHelp(player.PlayerId);
        }

        private static void DumpCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            Utils.DumpLog();
        }

        private static void GNOCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!GameStates.IsLobby && player.IsAlive())
            {
                Utils.SendMessage(GetString("GNoCommandInfo"), player.PlayerId);
                return;
            }

            string subArgs = args.Length != 2 ? "" : args[1];

            if (subArgs == "" || !int.TryParse(subArgs, out int guessedNo))
            {
                Utils.SendMessage(GetString("GNoCommandInfo"), player.PlayerId);
                return;
            }

            if (guessedNo is < 0 or > 99)
            {
                Utils.SendMessage(GetString("GNoCommandInfo"), player.PlayerId);
                return;
            }

            int targetNumber = Main.GuessNumber[player.PlayerId][0];

            if (Main.GuessNumber[player.PlayerId][0] == -1)
            {
                var rand = IRandom.Instance;
                Main.GuessNumber[player.PlayerId][0] = rand.Next(0, 100);
                targetNumber = Main.GuessNumber[player.PlayerId][0];
            }

            Main.GuessNumber[player.PlayerId][1]--;

            if (Main.GuessNumber[player.PlayerId][1] == 0 && guessedNo != targetNumber)
            {
                Main.GuessNumber[player.PlayerId][0] = -1;
                Main.GuessNumber[player.PlayerId][1] = 7;
                Utils.SendMessage(string.Format(GetString("GNoLost"), targetNumber), player.PlayerId);
                return;
            }

            if (guessedNo < targetNumber)
            {
                Utils.SendMessage(string.Format(GetString("GNoLow"), Main.GuessNumber[player.PlayerId][1]), player.PlayerId);
                return;
            }

            if (guessedNo > targetNumber)
            {
                Utils.SendMessage(string.Format(GetString("GNoHigh"), Main.GuessNumber[player.PlayerId][1]), player.PlayerId);
                return;
            }

            Utils.SendMessage(string.Format(GetString("GNoWon"), Main.GuessNumber[player.PlayerId][1]), player.PlayerId);
            Main.GuessNumber[player.PlayerId][0] = -1;
            Main.GuessNumber[player.PlayerId][1] = 7;
        }

        private static void SDCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (args.Length < 1 || !int.TryParse(args[1], out int sound1)) return;

            RPC.PlaySoundRPC(player.PlayerId, (Sounds)sound1);
        }

        private static void CSDCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            string subArgs = text.Remove(0, 3);
            player.RPCPlayCustomSound(subArgs.Trim());
        }

        private static void MTHYCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (GameStates.IsMeeting)
                MeetingHud.Instance.RpcClose();
            else
                player.NoCheckStartMeeting(null, true);
        }

        private static void CosIDCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            NetworkedPlayerInfo.PlayerOutfit of = player.Data.DefaultOutfit;
            Logger.Warn($"ColorId: {of.ColorId}", "Get Cos Id");
            Logger.Warn($"PetId: {of.PetId}", "Get Cos Id");
            Logger.Warn($"HatId: {of.HatId}", "Get Cos Id");
            Logger.Warn($"SkinId: {of.SkinId}", "Get Cos Id");
            Logger.Warn($"VisorId: {of.VisorId}", "Get Cos Id");
            Logger.Warn($"NamePlateId: {of.NamePlateId}", "Get Cos Id");
        }

        private static void EndCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Draw);
            GameManager.Instance.LogicFlow.CheckEndCriteria();
        }

        private static void ChangeRoleCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (GameStates.IsLobby || !player.FriendCode.GetDevUser().IsUp) return;

            string subArgs = text.Remove(0, 8);
            string setRole = FixRoleNameInput(subArgs.Trim());

            foreach (CustomRoles rl in Enum.GetValues<CustomRoles>())
            {
                if (rl.IsVanilla()) continue;

                string roleName = GetString(rl.ToString()).ToLower().Trim();

                if (setRole.Contains(roleName))
                {
                    if (!rl.IsAdditionRole()) player.SetRole(rl.GetRoleTypes());

                    player.RpcSetCustomRole(rl);

                    if (rl.IsGhostRole()) GhostRolesManager.SpecificAssignGhostRole(player.PlayerId, rl, true);

                    Main.PlayerStates[player.PlayerId].RemoveSubRole(CustomRoles.NotAssigned);
                    Main.ChangedRole = true;
                    break;
                }
            }
        }

        private static void IDCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            string msgText = GetString("PlayerIdList");
            msgText = Main.AllPlayerControls.Aggregate(msgText, (current, pc) => $"{current}\n{pc.PlayerId} \u2192 {pc.GetRealName()}");

            Utils.SendMessage(msgText, player.PlayerId);
        }

        private static void XFCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!GameStates.IsInGame && !player.IsHost())
            {
                Utils.SendMessage(GetString("Message.CanNotUseInLobby"), player.PlayerId);
                return;
            }

            foreach (PlayerControl pc in Main.AllAlivePlayerControls) pc.RpcSetNameEx(pc.GetRealName(true));

            ChatUpdatePatch.DoBlockChat = false;
            Utils.NotifyRoles(GameStates.IsMeeting, NoCache: true);
            Utils.SendMessage(GetString("Message.TryFixName"), player.PlayerId);
        }

        private static void ColorCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (GameStates.IsInGame)
            {
                Utils.SendMessage(GetString("Message.OnlyCanUseInLobby"), player.PlayerId);
                return;
            }

            if (!player.IsHost() && !Options.PlayerCanSetColor.GetBool() && !IsPlayerVIP(player.FriendCode) && !player.FriendCode.GetDevUser().IsUp)
            {
                Utils.SendMessage(GetString("DisableUseCommand"), player.PlayerId);
                return;
            }

            string subArgs = args.Length < 2 ? string.Empty : args[1];
            byte color = Utils.MsgToColor(subArgs, true);

            if (color == byte.MaxValue)
            {
                Utils.SendMessage(GetString("IllegalColor"), player.PlayerId);
                return;
            }

            player.RpcSetColor(color);
            Utils.SendMessage(string.Format(GetString("Message.SetColor"), subArgs), player.PlayerId);
        }

        private static void KillCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (GameStates.IsLobby)
            {
                Utils.SendMessage(GetString("Message.CanNotUseInLobby"), player.PlayerId);
                return;
            }

            if (args.Length < 2 || !int.TryParse(args[1], out int id2)) return;

            PlayerControl target = Utils.GetPlayerById(id2);

            if (target != null)
            {
                target.Kill(target);

                if (target.AmOwner)
                    Utils.SendMessage(GetString("HostKillSelfByCommand"), title: $"<color=#ff0000>{GetString("DefaultSystemMessageTitle")}</color>");
                else
                    Utils.SendMessage(string.Format(GetString("Message.Executed"), target.Data.PlayerName));
            }
        }

        private static void ExeCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (GameStates.IsLobby)
            {
                Utils.SendMessage(GetString("Message.CanNotUseInLobby"), player.PlayerId);
                return;
            }

            if (args.Length < 2 || !int.TryParse(args[1], out int id)) return;

            PlayerControl pc = Utils.GetPlayerById(id);

            if (pc != null)
            {
                pc.Data.IsDead = true;
                Main.PlayerStates[pc.PlayerId].deathReason = PlayerState.DeathReason.etc;
                pc.RpcExileV2();
                Main.PlayerStates[pc.PlayerId].SetDead();

                if (pc.AmOwner)
                    Utils.SendMessage(GetString("HostKillSelfByCommand"), title: $"<color=#ff0000>{GetString("DefaultSystemMessageTitle")}</color>");
                else
                    Utils.SendMessage(string.Format(GetString("Message.Executed"), pc.Data.PlayerName));
            }
        }

        private static void BanKickCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            // Check if the kick command is enabled in the settings
            if (!Options.ApplyModeratorList.GetBool() && !player.IsHost())
            {
                Utils.SendMessage(GetString("KickCommandDisabled"), player.PlayerId);
                return;
            }

            // Check if the player has the necessary privileges to use the command
            if (!IsPlayerModerator(player.FriendCode) && !player.IsHost())
            {
                Utils.SendMessage(GetString("KickCommandNoAccess"), player.PlayerId);
                return;
            }

            string subArgs = args.Length < 2 ? string.Empty : args[1];

            if (string.IsNullOrEmpty(subArgs) || !byte.TryParse(subArgs, out byte kickPlayerId))
            {
                Utils.SendMessage(GetString("KickCommandInvalidID"), player.PlayerId);
                return;
            }

            if (kickPlayerId.IsHost())
            {
                Utils.SendMessage(GetString("KickCommandKickHost"), player.PlayerId);
                return;
            }

            PlayerControl kickedPlayer = Utils.GetPlayerById(kickPlayerId);

            if (kickedPlayer == null)
            {
                Utils.SendMessage(GetString("KickCommandInvalidID"), player.PlayerId);
                return;
            }

            // Prevent moderators from kicking other moderators
            if (IsPlayerModerator(kickedPlayer.FriendCode))
            {
                Utils.SendMessage(GetString("KickCommandKickMod"), player.PlayerId);
                return;
            }

            // Kick the specified player
            AmongUsClient.Instance.KickPlayer(kickedPlayer.GetClientId(), args[0] == "/ban");
            string kickedPlayerName = kickedPlayer.GetRealName();
            var textToSend = $"{kickedPlayerName} {GetString("KickCommandKicked")}";
            if (GameStates.IsInGame) textToSend += $"{GetString("KickCommandKickedRole")} {kickedPlayer.GetCustomRole().ToColoredString()}";

            Utils.SendMessage(textToSend);
        }

        private static void CheckCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(CheckCommand), text);
                return;
            }

            if (!player.IsAlive() || !player.Is(CustomRoles.Inquirer) || player.GetAbilityUseLimit() < 1) return;

            if (args.Length < 3 || !GuessManager.MsgToPlayerAndRole(text[6..], out byte checkId, out CustomRoles checkRole, out _)) return;

            bool hasRole = Utils.GetPlayerById(checkId).Is(checkRole);
            if (IRandom.Instance.Next(100) < Inquirer.FailChance.GetInt()) hasRole = !hasRole;

            if (player.PlayerId != PlayerControl.LocalPlayer.PlayerId) ChatManager.SendPreviousMessagesToAll();

            LateTask.New(() => Utils.SendMessage(GetString(hasRole ? "Inquirer.MessageTrue" : "Inquirer.MessageFalse"), player.PlayerId), 0.2f, log: false);
            player.RpcRemoveAbilityUse();
        }

        private static void ChatCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(ChatCommand), text);
                return;
            }

            if (!Ventriloquist.On || !player.IsAlive() || !player.Is(CustomRoles.Ventriloquist) || player.PlayerId.GetAbilityUseLimit() < 1) return;

            var vl2 = (Ventriloquist)Main.PlayerStates[player.PlayerId].Role;
            if (vl2.Target == byte.MaxValue) return;

            if (player.PlayerId != PlayerControl.LocalPlayer.PlayerId) ChatManager.SendPreviousMessagesToAll();

            LateTask.New(() => Utils.GetPlayerById(vl2.Target)?.RpcSendChat(text[6..]), 0.2f, log: false);
            player.RpcRemoveAbilityUse();
        }

        private static void TargetCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(TargetCommand), text);
                return;
            }

            if (!Ventriloquist.On || !player.IsAlive() || !player.Is(CustomRoles.Ventriloquist) || player.PlayerId.GetAbilityUseLimit() < 1) return;

            var vl = (Ventriloquist)Main.PlayerStates[player.PlayerId].Role;
            vl.Target = args.Length < 2 ? byte.MaxValue : byte.TryParse(args[1], out byte targetId) ? targetId : byte.MaxValue;
            if (player.PlayerId != PlayerControl.LocalPlayer.PlayerId) ChatManager.SendPreviousMessagesToAll();
        }

        private static void QSCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(QSCommand), text);
                return;
            }

            if (!QuizMaster.On || !player.IsAlive()) return;

            var qm2 = (QuizMaster)Main.PlayerStates.Values.First(x => x.Role is QuizMaster).Role;
            if (qm2.Target != player.PlayerId || !QuizMaster.MessagesToSend.TryGetValue(player.PlayerId, out string msg)) return;

            Utils.SendMessage(msg, player.PlayerId, GetString("QuizMaster.QuestionSample.Title"));
        }

        private static void QACommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(QACommand), text);
                return;
            }

            if (args.Length < 2 || !QuizMaster.On || !player.IsAlive()) return;

            var qm = (QuizMaster)Main.PlayerStates.Values.First(x => x.Role is QuizMaster).Role;
            if (qm.Target != player.PlayerId) return;

            qm.Answer(args[1].ToUpper());
        }

        private static void AnswerCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(AnswerCommand), text);
                return;
            }

            if (args.Length < 2) return;

            Mathematician.Reply(player, args[1]);
        }

        private static void AskCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(AskCommand), text);
                return;
            }

            if (args.Length < 3 || !player.Is(CustomRoles.Mathematician)) return;

            if (player.PlayerId != PlayerControl.LocalPlayer.PlayerId) ChatManager.SendPreviousMessagesToAll();

            Mathematician.Ask(player, args[1], args[2]);
        }

        private static void VoteCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                RequestCommandProcessingFromHost(nameof(VoteCommand), text);
                return;
            }

            if (text.Length < 6 || !GameStates.IsMeeting) return;

            string toVote = text[6..].Replace(" ", string.Empty);
            if (!byte.TryParse(toVote, out byte voteId) || MeetingHud.Instance.playerStates?.FirstOrDefault(x => x.TargetPlayerId == player.PlayerId)?.DidVote is true or null) return;

            if (voteId > Main.AllPlayerControls.Length) return;

            if (player.PlayerId != PlayerControl.LocalPlayer.PlayerId)
                ChatManager.SendPreviousMessagesToAll();

            var votedPlayer = voteId.GetPlayer();
            if (Main.PlayerStates.TryGetValue(player.PlayerId, out var state) && votedPlayer != null && state.Role.OnVote(player, votedPlayer)) return;

            if (!player.IsHost())
                MeetingHud.Instance.CastVote(player.PlayerId, voteId);
            else
                MeetingHud.Instance.CmdCastVote(player.PlayerId, voteId);
        }

        private static void SayCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!AmongUsClient.Instance.AmHost && !IsPlayerModerator(player.FriendCode)) return;
            if (args.Length > 1) Utils.SendMessage(args.Skip(1).Join(delimiter: " "), title: $"<color=#ff0000>{GetString(player.IsHost() ? "MessageFromTheHost" : "SayTitle")}</color>");
        }

        private static void DeathCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!GameStates.IsInGame) return;

            PlayerControl killer = player.GetRealKiller();

            if (killer == null)
            {
                Utils.SendMessage("\n", player.PlayerId, GetString("DeathCommandFail"));
                return;
            }

            Utils.SendMessage("\n", player.PlayerId, string.Format(GetString("DeathCommand"), Utils.ColorString(Main.PlayerColors.TryGetValue(killer.PlayerId, out Color32 kColor) ? kColor : Color.white, killer.GetRealName()), (killer.Is(CustomRoles.Bloodlust) ? CustomRoles.Bloodlust.ToColoredString() : string.Empty) + killer.GetCustomRole().ToColoredString()));
        }

        private static void MessageWaitCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (args.Length > 1 && int.TryParse(args[1], out int sec))
            {
                Main.MessageWait.Value = sec;
                Utils.SendMessage(string.Format(GetString("Message.SetToSeconds"), sec), 0);
            }
            else
                Utils.SendMessage($"{GetString("Message.MessageWaitHelp")}\n{GetString("ForExample")}:\n{args[0]} 3", 0);
        }

        private static void TemplateCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (player.IsLocalPlayer())
            {
                if (args.Length > 1)
                    TemplateManager.SendTemplate(args[1]);
                else
                    HudManager.Instance.Chat.AddChat(player, (player.FriendCode.GetDevUser().HasTag() ? "\n" : string.Empty) + $"{GetString("ForExample")}:\n{args[0]} test");
            }
            else
            {
                if (args.Length > 1)
                    TemplateManager.SendTemplate(args[1], player.PlayerId);
                else
                    Utils.SendMessage($"{GetString("ForExample")}:\n{args[0]} test", player.PlayerId);
            }
        }

        private static void TPInCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!GameStates.IsLobby || !Options.PlayerCanTPInAndOut.GetBool()) return;

            player.TP(new Vector2(-0.2f, 1.3f));
        }

        private static void TPOutCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!GameStates.IsLobby || !Options.PlayerCanTPInAndOut.GetBool()) return;

            player.TP(new Vector2(0.1f, 3.8f));
        }

        private static void MyRoleCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            CustomRoles role = player.GetCustomRole();

            if (GameStates.IsInGame)
            {
                StringBuilder sb = new();
                StringBuilder titleSb = new();
                StringBuilder settings = new();
                settings.Append("<size=70%>");
                titleSb.Append($"{role.ToColoredString()} {Utils.GetRoleMode(role)}");
                sb.Append("<size=90%>");
                sb.Append(player.GetRoleInfo(true).TrimStart());
                if (Options.CustomRoleSpawnChances.TryGetValue(role, out StringOptionItem opt)) Utils.ShowChildrenSettings(opt, ref settings, disableColor: false);

                settings.Append("</size>");
                if (role.PetActivatedAbility()) sb.Append($"<size=50%>{GetString("SupportsPetMessage")}</size>");

                string searchStr = GetString(role.ToString());
                sb.Replace(searchStr, role.ToColoredString());
                sb.Replace(searchStr.ToLower(), role.ToColoredString());
                sb.Append("<size=70%>");

                foreach (CustomRoles subRole in Main.PlayerStates[player.PlayerId].SubRoles)
                {
                    sb.Append($"\n\n{subRole.ToColoredString()} {Utils.GetRoleMode(subRole)} {GetString($"{subRole}InfoLong")}");
                    string searchSubStr = GetString(subRole.ToString());
                    sb.Replace(searchSubStr, subRole.ToColoredString());
                    sb.Replace(searchSubStr.ToLower(), subRole.ToColoredString());
                }

                if (settings.Length > 0) Utils.SendMessage("\n", player.PlayerId, settings.ToString());

                Utils.SendMessage(sb.Append("</size>").ToString(), player.PlayerId, titleSb.ToString());
                if (role.UsesPetInsteadOfKill()) Utils.SendMessage("\n", player.PlayerId, GetString("UsesPetInsteadOfKillNotice"));
            }
            else
                Utils.SendMessage((player.FriendCode.GetDevUser().HasTag() ? "\n" : string.Empty) + GetString("Message.CanNotUseInLobby"), player.PlayerId);
        }

        private static void AFKExemptCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (args.Length < 2 || !byte.TryParse(args[1], out byte afkId)) return;

            AFKDetector.ExemptedPlayers.Add(afkId);
            Utils.SendMessage("\n", player.PlayerId, string.Format(GetString("PlayerExemptedFromAFK"), afkId.ColoredPlayerName()));
        }

        private static void EffectCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (args.Length < 2 || !GameStates.IsInTask || !Randomizer.Exists) return;

            if (Enum.TryParse(args[1], true, out Randomizer.Effect effect)) effect.Apply(player);
        }

        private static void ComboCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (args.Length < 4)
            {
                if (Main.AlwaysSpawnTogetherCombos.Count == 0 && Main.NeverSpawnTogetherCombos.Count == 0) return;

                StringBuilder sb = new();
                sb.Append("<size=70%>");

                if (Main.AlwaysSpawnTogetherCombos.TryGetValue(OptionItem.CurrentPreset, out Dictionary<CustomRoles, List<CustomRoles>> alwaysList) && alwaysList.Count > 0)
                {
                    sb.AppendLine(GetString("AlwaysComboListTitle"));
                    sb.AppendLine(alwaysList.Join(x => $"{x.Key.ToColoredString()} \u00a7 {x.Value.Join(r => r.ToColoredString())}", "\n"));
                    sb.AppendLine();
                }

                if (Main.NeverSpawnTogetherCombos.TryGetValue(OptionItem.CurrentPreset, out Dictionary<CustomRoles, List<CustomRoles>> neverList) && neverList.Count > 0)
                {
                    sb.AppendLine(GetString("NeverComboListTitle"));
                    sb.AppendLine(neverList.Join(x => $"{x.Key.ToColoredString()} \u2194 {x.Value.Join(r => r.ToColoredString())}", "\n"));
                    sb.AppendLine();
                }

                sb.Append(GetString("ComboUsage"));

                Utils.SendMessage("\n", player.PlayerId, sb.ToString());
                return;
            }

            switch (args[1])
            {
                case "add":
                case "ban":
                    if (GetRoleByName(args[2], out CustomRoles mainRole) && GetRoleByName(args[3], out CustomRoles addOn))
                    {
                        if (mainRole.IsAdditionRole() || !addOn.IsAdditionRole() || (addOn == CustomRoles.Lovers && args[1] == "add")) break;

                        if (args[1] == "add")
                        {
                            if (!Main.AlwaysSpawnTogetherCombos.ContainsKey(OptionItem.CurrentPreset)) Main.AlwaysSpawnTogetherCombos[OptionItem.CurrentPreset] = [];

                            if (!Main.AlwaysSpawnTogetherCombos[OptionItem.CurrentPreset].TryGetValue(mainRole, out List<CustomRoles> list1))
                                Main.AlwaysSpawnTogetherCombos[OptionItem.CurrentPreset][mainRole] = [addOn];
                            else if (!list1.Contains(addOn)) list1.Add(addOn);

                            if (text.EndsWith(" all"))
                            {
                                for (var preset = 0; preset < OptionItem.NumPresets; preset++)
                                {
                                    if (preset == OptionItem.CurrentPreset) continue;

                                    if (!Main.AlwaysSpawnTogetherCombos.ContainsKey(preset)) Main.AlwaysSpawnTogetherCombos[preset] = [];

                                    if (!Main.AlwaysSpawnTogetherCombos[preset].TryGetValue(mainRole, out List<CustomRoles> list2))
                                        Main.AlwaysSpawnTogetherCombos[preset][mainRole] = [addOn];
                                    else if (!list2.Contains(addOn)) list2.Add(addOn);
                                }
                            }
                        }
                        else
                        {
                            if (!Main.NeverSpawnTogetherCombos.ContainsKey(OptionItem.CurrentPreset)) Main.NeverSpawnTogetherCombos[OptionItem.CurrentPreset] = [];

                            if (!Main.NeverSpawnTogetherCombos[OptionItem.CurrentPreset].TryGetValue(mainRole, out List<CustomRoles> list2))
                                Main.NeverSpawnTogetherCombos[OptionItem.CurrentPreset][mainRole] = [addOn];
                            else if (!list2.Contains(addOn)) list2.Add(addOn);

                            if (text.EndsWith(" all"))
                            {
                                for (var preset = 0; preset < OptionItem.NumPresets; preset++)
                                {
                                    if (preset == OptionItem.CurrentPreset) continue;

                                    if (!Main.NeverSpawnTogetherCombos.ContainsKey(preset)) Main.NeverSpawnTogetherCombos[preset] = [];

                                    if (!Main.NeverSpawnTogetherCombos[preset].TryGetValue(mainRole, out List<CustomRoles> list3))
                                        Main.NeverSpawnTogetherCombos[preset][mainRole] = [addOn];
                                    else if (!list3.Contains(addOn)) list3.Add(addOn);
                                }
                            }
                        }

                        Utils.SendMessage(string.Format(args[1] == "add" ? GetString("ComboAdd") : GetString("ComboBan"), GetString(mainRole.ToString()), GetString(addOn.ToString())), player.PlayerId);
                        Utils.SaveComboInfo();
                    }

                    break;
                case "remove":
                case "allow":
                    if (GetRoleByName(args[2], out CustomRoles mainRole2) && GetRoleByName(args[3], out CustomRoles addOn2))
                    {
                        if (mainRole2.IsAdditionRole() || !addOn2.IsAdditionRole()) break;

                        if (text.EndsWith(" all"))
                        {
                            for (var preset = 0; preset < OptionItem.NumPresets; preset++)
                            {
                                if (Main.AlwaysSpawnTogetherCombos.TryGetValue(preset, out Dictionary<CustomRoles, List<CustomRoles>> list1))
                                {
                                    if (list1.TryGetValue(mainRole2, out List<CustomRoles> list2))
                                    {
                                        list2.Remove(addOn2);
                                        if (list2.Count == 0) list1.Remove(mainRole2);

                                        if (list1.Count == 0) Main.AlwaysSpawnTogetherCombos.Remove(preset);
                                    }
                                }

                                if (Main.NeverSpawnTogetherCombos.TryGetValue(preset, out Dictionary<CustomRoles, List<CustomRoles>> list3))
                                {
                                    if (list3.TryGetValue(mainRole2, out List<CustomRoles> list4))
                                    {
                                        list4.Remove(addOn2);
                                        if (list4.Count == 0) list3.Remove(mainRole2);

                                        if (list3.Count == 0) Main.NeverSpawnTogetherCombos.Remove(preset);
                                    }
                                }
                            }

                            Utils.SendMessage(string.Format(GetString("ComboRemove"), GetString(mainRole2.ToString()), GetString(addOn2.ToString())), player.PlayerId);
                            Utils.SaveComboInfo();
                        }
                        else
                        {
                            if (args[1] == "remove" && Main.AlwaysSpawnTogetherCombos.TryGetValue(OptionItem.CurrentPreset, out Dictionary<CustomRoles, List<CustomRoles>> alwaysList) && alwaysList.TryGetValue(mainRole2, out List<CustomRoles> list3))
                            {
                                list3.Remove(addOn2);
                                if (list3.Count == 0) alwaysList.Remove(mainRole2);

                                Utils.SendMessage(string.Format(GetString("ComboRemove"), GetString(mainRole2.ToString()), GetString(addOn2.ToString())), player.PlayerId);
                                Utils.SaveComboInfo();
                            }
                            else if (Main.NeverSpawnTogetherCombos.TryGetValue(OptionItem.CurrentPreset, out Dictionary<CustomRoles, List<CustomRoles>> neverList) && neverList.TryGetValue(mainRole2, out List<CustomRoles> list4))
                            {
                                list4.Remove(addOn2);
                                if (list4.Count == 0) neverList.Remove(mainRole2);

                                Utils.SendMessage(string.Format(GetString("ComboAllow"), GetString(mainRole2.ToString()), GetString(addOn2.ToString())), player.PlayerId);
                                Utils.SaveComboInfo();
                            }
                        }
                    }

                    break;
            }
        }

        private static void DeleteModCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (args.Length < 2 || !byte.TryParse(args[1], out byte remModId)) return;

            PlayerControl remModPc = Utils.GetPlayerById(remModId);
            if (remModPc == null) return;

            string remFc = remModPc.FriendCode;
            if (!IsPlayerModerator(remFc)) Utils.SendMessage(GetString("PlayerNotMod"), player.PlayerId);

            File.WriteAllLines("./EHR_DATA/Moderators.txt", File.ReadAllLines("./EHR_DATA/Moderators.txt").Where(x => !x.Contains(remFc)));
            Utils.SendMessage(GetString("PlayerRemovedFromModList"), player.PlayerId);
        }

        private static void AddModCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (args.Length < 2 || !byte.TryParse(args[1], out byte newModId)) return;

            PlayerControl newModPc = Utils.GetPlayerById(newModId);
            if (newModPc == null) return;

            string fc = newModPc.FriendCode;
            if (IsPlayerModerator(fc)) Utils.SendMessage(GetString("PlayerAlreadyMod"), player.PlayerId);

            File.AppendAllText("./EHR_DATA/Moderators.txt", $"\n{fc}");
            Utils.SendMessage(GetString("PlayerAddedToModList"), player.PlayerId);
        }

        private static void KCountCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (GameStates.IsLobby || !Options.EnableKillerLeftCommand.GetBool() || Main.AllAlivePlayerControls.Length < Options.MinPlayersForGameStateCommand.GetInt()) return;

            Utils.SendMessage("\n", player.PlayerId, Utils.GetGameStateData());
        }

        private static void SetRoleCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            string subArgs = text.Remove(0, 8);

            if (!GameStates.IsLobby)
            {
                Utils.SendMessage(GetString("Message.OnlyCanUseInLobby"), player.PlayerId);
                return;
            }

            if (!GuessManager.MsgToPlayerAndRole(subArgs, out byte resultId, out CustomRoles roleToSet, out _))
            {
                Utils.SendMessage($"{GetString("InvalidArguments")}", player.PlayerId);
                return;
            }

            if (resultId != 0 && !player.FriendCode.GetDevUser().IsUp)
            {
                Utils.SendMessage($"{GetString("Message.NoPermissionSetRoleOthers")}", player.PlayerId);
                return;
            }

            PlayerControl targetPc = Utils.GetPlayerById(resultId);
            if (targetPc == null) return;

            if (roleToSet.IsAdditionRole())
            {
                if (!Main.SetAddOns.ContainsKey(resultId)) Main.SetAddOns[resultId] = [];

                if (Main.SetAddOns[resultId].Contains(roleToSet))
                    Main.SetAddOns[resultId].Remove(roleToSet);
                else
                    Main.SetAddOns[resultId].Add(roleToSet);
            }
            else
                Main.SetRoles[targetPc.PlayerId] = roleToSet;

            var playername = $"<b>{Utils.ColorString(Main.PlayerColors.TryGetValue(resultId, out Color32 textColor) ? textColor : Color.white, targetPc.GetRealName())}</b>";
            var rolename = $"<color={Main.RoleColors[roleToSet]}> {GetString(roleToSet.ToString())} </color>";
            Utils.SendMessage("\n", player.PlayerId, string.Format(GetString("RoleSelected"), playername, rolename));
        }

        private static void UpCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            Utils.SendMessage($"{GetString("UpReplacedMessage")}", player.PlayerId);
        }

        private static void RCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            string subArgs = text.Remove(0, 2);
            SendRolesInfo(subArgs, player.PlayerId, player.FriendCode.GetDevUser().DeBug);
        }

        private static void DisconnectCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            string subArgs = args.Length < 2 ? string.Empty : args[1];

            switch (subArgs)
            {
                case "crew":
                    GameManager.Instance.enabled = false;
                    GameManager.Instance.RpcEndGame(GameOverReason.HumansDisconnect, false);
                    break;

                case "imp":
                    GameManager.Instance.enabled = false;
                    GameManager.Instance.RpcEndGame(GameOverReason.ImpostorDisconnect, false);
                    break;

                default:
                    __instance?.AddChat(player, "crew | imp");
                    break;
            }

            ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Admin, 0);
        }

        private static void NowCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            string subArgs = args.Length < 2 ? string.Empty : args[1];

            switch (subArgs)
            {
                case "r":
                case "roles":
                    Utils.ShowActiveRoles(player.PlayerId);
                    break;
                case "a":
                case "all":
                    Utils.ShowAllActiveSettings(player.PlayerId);
                    break;
                default:
                    Utils.ShowActiveSettings(player.PlayerId);
                    break;
            }
        }

        private static void LevelCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            string subArgs = args.Length < 2 ? string.Empty : args[1];
            Utils.SendMessage(string.Format(GetString("Message.SetLevel"), subArgs), player.PlayerId);
            _ = int.TryParse(subArgs, out int input);

            if (input is < 1 or > 999)
            {
                Utils.SendMessage(GetString("Message.AllowLevelRange"), player.PlayerId);
                return;
            }

            var number = Convert.ToUInt32(input);
            player.RpcSetLevel(number - 1);
        }

        private static void HideNameCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            Main.HideName.Value = args.Length > 1 ? args.Skip(1).Join(delimiter: " ") : Main.HideName.DefaultValue.ToString();

            GameStartManagerPatch.GameStartManagerStartPatch.HideName.text =
                ColorUtility.TryParseHtmlString(Main.HideColor.Value, out _)
                    ? $"<color={Main.HideColor.Value}>{Main.HideName.Value}</color>"
                    : $"<color={Main.ModColor}>{Main.HideName.Value}</color>";
        }

        private static void RenameCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (args.Length < 2) return;

            if (args[1].Length is > 50 or < 1)
                Utils.SendMessage(GetString("Message.AllowNameLength"), player.PlayerId);
            else
            {
                if (player.IsLocalPlayer())
                    Main.NickName = args[1];
                else
                {
                    if (!Options.PlayerCanSetName.GetBool() && !IsPlayerVIP(player.FriendCode)) return;

                    if (GameStates.IsInGame)
                    {
                        Utils.SendMessage(GetString("Message.OnlyCanUseInLobby"), player.PlayerId);
                        return;
                    }

                    string name = args.Skip(1).Join(delimiter: " ");

                    if (name.Length is > 50 or < 1)
                    {
                        Utils.SendMessage(GetString("Message.AllowNameLength"), player.PlayerId);
                        return;
                    }

                    Main.AllPlayerNames[player.PlayerId] = name;
                    player.RpcSetName(name);
                }
            }
        }

        private static void LastResultCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            Utils.ShowKillLog(player.PlayerId);
            Utils.ShowLastAddOns(player.PlayerId);
            Utils.ShowLastRoles(player.PlayerId);
            Utils.ShowLastResult(player.PlayerId);
        }

        private static void WinnerCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (Main.WinnerNameList.Count == 0)
                Utils.SendMessage(GetString("NoInfoExists"));
            else
                Utils.SendMessage("<b><u>Winners:</b></u>\n" + string.Join(", ", Main.WinnerNameList));
        }

        private static void ChangeSettingCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            string subArgs = args.Length < 2 ? "" : args[1];

            switch (subArgs)
            {
                case "map":
                    subArgs = args.Length < 3 ? "" : args[2];

                    switch (subArgs)
                    {
                        case "theskeld":
                            GameOptionsManager.Instance.CurrentGameOptions.SetByte(ByteOptionNames.MapId, 0);
                            break;
                        case "mirahq":
                            GameOptionsManager.Instance.CurrentGameOptions.SetByte(ByteOptionNames.MapId, 1);
                            break;
                        case "polus":
                            GameOptionsManager.Instance.CurrentGameOptions.SetByte(ByteOptionNames.MapId, 2);
                            break;
                        case "dlekseht":
                            GameOptionsManager.Instance.CurrentGameOptions.SetByte(ByteOptionNames.MapId, 3);
                            break;
                        case "airship":
                            GameOptionsManager.Instance.CurrentGameOptions.SetByte(ByteOptionNames.MapId, 4);
                            break;
                        case "thefungle":
                            GameOptionsManager.Instance.CurrentGameOptions.SetByte(ByteOptionNames.MapId, 5);
                            break;
                        case "custom":
                            subArgs = args.Length < 4 ? "" : args[3];
                            GameOptionsManager.Instance.CurrentGameOptions.SetByte(ByteOptionNames.MapId, byte.Parse(subArgs));
                            break;
                    }

                    break;
                case "impostors":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.currentNormalGameOptions.SetInt(Int32OptionNames.NumImpostors, int.Parse(subArgs));
                    AmongUsClient.Instance.StartGame();
                    break;
                case "players":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetInt(Int32OptionNames.MaxPlayers, int.Parse(subArgs));
                    AmongUsClient.Instance.StartGame();
                    break;
                case "recommended":
                    subArgs = args.Length < 3 ? "" : args[2];

                    switch (subArgs)
                    {
                        case "on":
                            GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.IsDefaults, true);
                            break;
                        case "off":
                            GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.IsDefaults, false);
                            break;
                    }

                    break;
                case "confirmejects":
                    subArgs = args.Length < 3 ? "" : args[2];

                    switch (subArgs)
                    {
                        case "on":
                            GameOptionsManager.Instance.currentNormalGameOptions.SetBool(BoolOptionNames.ConfirmImpostor, true);
                            break;
                        case "off":
                            GameOptionsManager.Instance.currentNormalGameOptions.SetBool(BoolOptionNames.ConfirmImpostor, false);
                            break;
                    }

                    break;
                case "emergencymeetings":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.currentNormalGameOptions.SetInt(Int32OptionNames.NumEmergencyMeetings, int.Parse(subArgs));
                    break;
                case "anonymousvotes":
                    subArgs = args.Length < 3 ? "" : args[2];

                    switch (subArgs)
                    {
                        case "on":
                            GameOptionsManager.Instance.currentNormalGameOptions.SetBool(BoolOptionNames.AnonymousVotes, true);
                            break;
                        case "off":
                            GameOptionsManager.Instance.currentNormalGameOptions.SetBool(BoolOptionNames.AnonymousVotes, false);
                            break;
                    }

                    break;
                case "emergencycooldown":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.currentNormalGameOptions.SetInt(Int32OptionNames.EmergencyCooldown, int.Parse(subArgs));
                    break;
                case "discussiontime":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.currentNormalGameOptions.SetInt(Int32OptionNames.DiscussionTime, int.Parse(subArgs));
                    break;
                case "votingtime":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.currentNormalGameOptions.SetInt(Int32OptionNames.VotingTime, int.Parse(subArgs));
                    break;
                case "playerspeed":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.currentNormalGameOptions.SetFloat(FloatOptionNames.PlayerSpeedMod, float.Parse(subArgs));
                    break;
                case "crewmatevision":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.currentNormalGameOptions.SetFloat(FloatOptionNames.CrewLightMod, float.Parse(subArgs));
                    break;
                case "impostorvision":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.currentNormalGameOptions.SetFloat(FloatOptionNames.ImpostorLightMod, float.Parse(subArgs));
                    break;
                case "killcooldown":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.currentNormalGameOptions.SetFloat(FloatOptionNames.KillCooldown, float.Parse(subArgs));
                    break;
                case "killdistance":
                    subArgs = args.Length < 3 ? "" : args[2];

                    switch (subArgs)
                    {
                        case "short":
                            GameOptionsManager.Instance.currentNormalGameOptions.SetInt(Int32OptionNames.KillDistance, 0);
                            break;
                        case "medium":
                            GameOptionsManager.Instance.currentNormalGameOptions.SetInt(Int32OptionNames.KillDistance, 1);
                            break;
                        case "long":
                            GameOptionsManager.Instance.currentNormalGameOptions.SetInt(Int32OptionNames.KillDistance, 2);
                            break;
                        case "custom":
                            subArgs = args.Length < 4 ? "" : args[3];
                            GameOptionsManager.Instance.currentNormalGameOptions.SetInt(Int32OptionNames.KillDistance, int.Parse(subArgs));
                            break;
                    }

                    break;
                case "taskbarupdates":
                    subArgs = args.Length < 3 ? "" : args[2];

                    GameOptionsManager.Instance.currentNormalGameOptions.TaskBarMode = subArgs switch
                    {
                        "always" => AmongUs.GameOptions.TaskBarMode.Normal,
                        "meetings" => AmongUs.GameOptions.TaskBarMode.MeetingOnly,
                        "never" => AmongUs.GameOptions.TaskBarMode.Invisible,
                        _ => GameOptionsManager.Instance.currentNormalGameOptions.TaskBarMode
                    };

                    break;
                case "visualtasks":
                    subArgs = args.Length < 3 ? "" : args[2];

                    switch (subArgs)
                    {
                        case "on":
                            GameOptionsManager.Instance.currentNormalGameOptions.SetBool(BoolOptionNames.VisualTasks, true);
                            break;
                        case "off":
                            GameOptionsManager.Instance.currentNormalGameOptions.SetBool(BoolOptionNames.VisualTasks, false);
                            break;
                    }

                    break;
                case "commontasks":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetInt(Int32OptionNames.NumCommonTasks, int.Parse(subArgs));
                    break;
                case "longtasks":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetInt(Int32OptionNames.NumLongTasks, int.Parse(subArgs));
                    break;
                case "shorttasks":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetInt(Int32OptionNames.NumShortTasks, int.Parse(subArgs));
                    break;
                case "scientistcount":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.Scientist, int.Parse(subArgs), GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetChancePerGame(RoleTypes.Scientist));
                    break;
                case "scientistchance":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.Scientist, GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetNumPerGame(RoleTypes.Scientist), int.Parse(subArgs));
                    break;
                case "vitalsdisplaycooldown":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.ScientistCooldown, float.Parse(subArgs));
                    break;
                case "batteryduration":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.ScientistBatteryCharge, float.Parse(subArgs));
                    break;
                case "engineercount":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.currentNormalGameOptions.RoleOptions.SetRoleRate(RoleTypes.Engineer, int.Parse(subArgs), GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetChancePerGame(RoleTypes.Engineer));
                    break;
                case "engineerchance":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.Engineer, GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetNumPerGame(RoleTypes.Engineer), int.Parse(subArgs));
                    break;
                case "ventusecooldown":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.EngineerCooldown, float.Parse(subArgs));
                    break;
                case "maxtimeinvents":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.EngineerInVentMaxTime, float.Parse(subArgs));
                    break;
                case "guardianangelcount":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.GuardianAngel, int.Parse(subArgs), GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetChancePerGame(RoleTypes.GuardianAngel));
                    break;
                case "guardianangelchance":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.GuardianAngel, GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetNumPerGame(RoleTypes.GuardianAngel), int.Parse(subArgs));
                    break;
                case "protectcooldown":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.GuardianAngelCooldown, float.Parse(subArgs));
                    break;
                case "protectduration":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.ProtectionDurationSeconds, float.Parse(subArgs));
                    break;
                case "protectvisibletoimpostors":
                    subArgs = args.Length < 3 ? "" : args[2];

                    switch (subArgs)
                    {
                        case "on":
                            GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.ImpostorsCanSeeProtect, true);
                            break;
                        case "off":
                            GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.ImpostorsCanSeeProtect, false);
                            break;
                    }

                    break;
                case "shapeshiftercount":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.Shapeshifter, int.Parse(subArgs), GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetChancePerGame(RoleTypes.Shapeshifter));
                    break;
                case "shapeshifterchance":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.Shapeshifter, GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetNumPerGame(RoleTypes.Shapeshifter), int.Parse(subArgs));
                    break;
                case "shapeshiftduration":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.ShapeshifterDuration, float.Parse(subArgs));
                    break;
                case "shapeshiftcooldown":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.ShapeshifterCooldown, float.Parse(subArgs));
                    break;
                case "leaveshapeshiftevidence":
                    subArgs = args.Length < 3 ? "" : args[2];

                    switch (subArgs)
                    {
                        case "on":
                            GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.ShapeshifterLeaveSkin, true);
                            break;
                        case "off":
                            GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.ShapeshifterLeaveSkin, false);
                            break;
                    }

                    break;
                case "phantomcount":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.Phantom, int.Parse(subArgs), GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetChancePerGame(RoleTypes.Phantom));
                    break;
                case "phantomchance":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.Phantom, GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetNumPerGame(RoleTypes.Phantom), int.Parse(subArgs));
                    break;
                case "invisduration":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.PhantomDuration, float.Parse(subArgs));
                    break;
                case "inviscooldown":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.PhantomCooldown, float.Parse(subArgs));
                    break;
                case "noisemakercount":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.Noisemaker, int.Parse(subArgs), GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetChancePerGame(RoleTypes.Noisemaker));
                    break;
                case "noisemakerchance":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.Noisemaker, GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetNumPerGame(RoleTypes.Noisemaker), int.Parse(subArgs));
                    break;
                case "noisemakerimpostoralert":
                    subArgs = args.Length < 3 ? "" : args[2];

                    switch (subArgs)
                    {
                        case "on":
                            GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.NoisemakerImpostorAlert, true);
                            break;
                        case "off":
                            GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.NoisemakerImpostorAlert, false);
                            break;
                    }

                    break;
                case "noisemakeralertduration":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.NoisemakerAlertDuration, int.Parse(subArgs));
                    break;
                case "trackercount":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.Tracker, int.Parse(subArgs), GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetChancePerGame(RoleTypes.Tracker));
                    break;
                case "trackerchance":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.SetRoleRate(RoleTypes.Tracker, GameOptionsManager.Instance.CurrentGameOptions.RoleOptions.GetNumPerGame(RoleTypes.Tracker), int.Parse(subArgs));
                    break;
                case "trackduration":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.TrackerDuration, float.Parse(subArgs));
                    break;
                case "trackcooldown":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.TrackerCooldown, float.Parse(subArgs));
                    break;
                case "trackdelay":
                    subArgs = args.Length < 3 ? "" : args[2];
                    GameOptionsManager.Instance.CurrentGameOptions.SetFloat(FloatOptionNames.TrackerDelay, float.Parse(subArgs));
                    break;
                case "ghostdotasks":
                    subArgs = args.Length < 3 ? "" : args[2];

                    switch (subArgs)
                    {
                        case "on":
                            GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.GhostsDoTasks, true);
                            break;
                        case "off":
                            GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.GhostsDoTasks, false);
                            break;
                    }

                    break;
                default:
                    Utils.SendMessage(GetString("Commands.ChangeSettingHelp"), player.PlayerId);
                    break;
            }

            GameOptionsManager.Instance.GameHostOptions = GameOptionsManager.Instance.CurrentGameOptions;
            GameManager.Instance.LogicOptions.SyncOptions();
        }

        private static void VersionCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            string version_text = Main.PlayerVersion.OrderBy(pair => pair.Key).Aggregate(string.Empty, (current, kvp) => current + $"{kvp.Key}: ({Main.AllPlayerNames[kvp.Key]}) {kvp.Value.forkId}/{kvp.Value.version}({kvp.Value.tag})\n");
            if (version_text != string.Empty) HudManager.Instance.Chat.AddChat(player, (player.FriendCode.GetDevUser().HasTag() ? "\n" : string.Empty) + version_text);
        }

        private static void LTCommand(ChatController __instance, PlayerControl player, string text, string[] args)
        {
            if (!GameStates.IsLobby) return;

            float timer = GameStartManagerPatch.Timer;
            int minutes = (int)timer / 60;
            int seconds = (int)timer % 60;
            string lt = string.Format(GetString("LobbyCloseTimer"), $"{minutes:00}:{seconds:00}");
            if (timer <= 60) lt = Utils.ColorString(Color.red, lt);

            Utils.SendMessage(lt, player.PlayerId);
        }

        // -------------------------------------------------------------------------------------------------------------------------

        private static bool CheckMute(byte id)
        {
            if (!MutedPlayers.TryGetValue(id, out (long MuteTimeStamp, int Duration) mute)) return false;

            long timeLeft = mute.Duration - (Utils.TimeStamp - mute.MuteTimeStamp);

            if (timeLeft <= 0)
            {
                MutedPlayers.Remove(id);
                return false;
            }

            Utils.SendMessage("\n", id, string.Format(GetString("MuteMessage"), timeLeft));
            return true;
        }

        public static string FixRoleNameInput(string text)
        {
            text = text.Replace("着", "者").Trim().ToLower();

            return text switch
            {
                "管理員" or "管理" or "gm" => GetString("GM"),
                "賞金獵人" or "赏金" or "bh" or "bounty" => GetString("BountyHunter"),
                "自爆兵" or "自爆" => GetString("Bomber"),
                "邪惡的追踪者" or "邪恶追踪者" or "追踪" or "et" => GetString("EvilTracker"),
                "煙花商人" or "烟花" or "fw" => GetString("FireWorks"),
                "夢魘" or "夜魇" => GetString("Mare"),
                "詭雷" => GetString("BoobyTrap"),
                "黑手黨" or "黑手" => GetString("Mafia"),
                "嗜血殺手" or "嗜血" or "sk" => GetString("SerialKiller"),
                "千面鬼" or "千面" => GetString("ShapeMaster"),
                "狂妄殺手" or "狂妄" or "arr" => GetString("Sans"),
                "殺戮機器" or "杀戮" or "机器" or "杀戮兵器" or "km" => GetString("Minimalism"),
                "蝕時者" or "蚀时" or "偷时" or "tt" => GetString("TimeThief"),
                "狙擊手" or "狙击" => GetString("Sniper"),
                "傀儡師" or "傀儡" => GetString("Puppeteer"),
                "殭屍" or "丧尸" => GetString("Zombie"),
                "吸血鬼" or "吸血" or "vamp" => GetString("Vampire"),
                "術士" => GetString("Warlock"),
                "駭客" or "黑客" => GetString("Hacker"),
                "刺客" or "忍者" => GetString("Assassin"),
                "礦工" => GetString("Miner"),
                "逃逸者" or "逃逸" => GetString("Escapee"),
                "女巫" => GetString("Witch"),
                "監視者" or "监管" or "aa" => GetString("AntiAdminer"),
                "清道夫" or "清道" or "scav" => GetString("Scavenger"),
                "窺視者" or "窥视" => GetString("Watcher"),
                "誘餌" or "大奖" or "头奖" => GetString("Bait"),
                "擺爛人" or "摆烂" => GetString("Needy"),
                "獨裁者" or "独裁" or "dict" => GetString("Dictator"),
                "法醫" or "doc" => GetString("Doctor"),
                "偵探" or "det" => GetString("Detective"),
                "幸運兒" or "幸运" => GetString("Luckey"),
                "大明星" or "明星" or "ss" => GetString("SuperStar"),
                "網紅" or "cel" or "celeb" => GetString("CyberStar"),
                "demo" => GetString("Demolitionist"),
                "俠客" => GetString("SwordsMan"),
                "正義賭怪" or "正义的赌怪" or "好赌" or "正义赌" or "ng" => GetString("NiceGuesser"),
                "邪惡賭怪" or "邪恶的赌怪" or "坏赌" or "恶赌" or "邪恶赌" or "赌怪" or "eg" => GetString("EvilGuesser"),
                "市長" or "逝长" => GetString("Mayor"),
                "被害妄想症" or "被害妄想" or "被迫害妄想症" or "被害" or "妄想" or "妄想症" => GetString("Paranoia"),
                "愚者" or "愚" => GetString("Psychic"),
                "修理大师" or "修理" or "维修" or "sm" => GetString("SabotageMaster"),
                "警長" => GetString("Sheriff"),
                "告密者" or "告密" => GetString("Snitch"),
                "增速者" or "增速" => GetString("SpeedBooster"),
                "時間操控者" or "时间操控人" or "时间操控" or "tm" => GetString("TimeManager"),
                "陷阱師" or "陷阱" or "小奖" => GetString("Trapper"),
                "傳送師" or "传送" or "trans" => GetString("Transporter"),
                "縱火犯" or "纵火" or "arso" => GetString("Arsonist"),
                "處刑人" or "处刑" or "exe" => GetString("Executioner"),
                "小丑" or "丑皇" or "jest" => GetString("Jester"),
                "投機者" or "投机" or "oppo" => GetString("Opportunist"),
                "馬里奧" or "马力欧" => GetString("Mario"),
                "恐怖分子" or "恐怖" or "terro" => GetString("Terrorist"),
                "豺狼" or "蓝狼" or "狼" => GetString("Jackal"),
                "神" or "上帝" => GetString("God"),
                "情人" or "愛人" or "链子" or "老婆" or "老公" or "lover" => GetString("Lovers"),
                "絕境者" or "绝境" or "last" or "lastimp" or "last imp" or "Last" => GetString("LastImpostor"),
                "閃電俠" or "闪电" => GetString("Flashman"),
                "靈媒" => GetString("Seer"),
                "破平者" or "破平" => GetString("Brakar"),
                "執燈人" or "执灯" or "灯人" => GetString("Torch"),
                "膽小" or "胆小" or "obli" => GetString("Oblivious"),
                "迷惑者" or "迷幻" or "bew" => GetString("Bewilder"),
                "sun" => GetString("Sunglasses"),
                "蠢蛋" or "笨蛋" or "蠢狗" or "傻逼" => GetString("Fool"),
                "冤罪師" or "冤罪" or "inno" => GetString("Innocent"),
                "資本家" or "资本主义" or "资本" or "cap" or "capi" => GetString("Capitalism"),
                "老兵" or "vet" => GetString("Veteran"),
                "加班狂" or "加班" => GetString("Workhorse"),
                "復仇者" or "复仇" => GetString("Avanger"),
                "鵜鶘" or "pel" or "peli" => GetString("Pelican"),
                "保鏢" or "bg" => GetString("Bodyguard"),
                "up" or "up主" or "yt" => GetString("Youtuber"),
                "利己主義者" or "利己主义" or "利己" or "ego" => GetString("Egoist"),
                "贗品商" or "赝品" => GetString("Counterfeiter"),
                "擲雷兵" or "掷雷" or "闪光弹" or "gren" or "grena" => GetString("Grenadier"),
                "竊票者" or "偷票" or "偷票者" or "窃票师" or "窃票" => GetString("TicketsStealer"),
                "教父" => GetString("Gangster"),
                "革命家" or "革命" or "revo" => GetString("Revolutionist"),
                "fff團" or "fff" or "fff团" => GetString("FFF"),
                "清理工" or "清潔工" or "清洁工" or "清理" or "清洁" or "janitor" => GetString("Cleaner"),
                "醫生" => GetString("Medicaler"),
                "占卜師" or "占卜" or "ft" => GetString("Divinator"),
                "雙重人格" or "双重" or "双人格" or "人格" or "schizo" or "scizo" or "shizo" => GetString("DualPersonality"),
                "玩家" => GetString("Gamer"),
                "情報販子" or "情报" or "贩子" => GetString("Messenger"),
                "球狀閃電" or "球闪" or "球状" => GetString("BallLightning"),
                "潛藏者" or "潜藏" => GetString("DarkHide"),
                "貪婪者" or "贪婪" => GetString("Greedier"),
                "工作狂" or "工作" or "worka" => GetString("Workaholic"),
                "呪狼" or "咒狼" or "cw" => GetString("CursedWolf"),
                "寶箱怪" or "宝箱" => GetString("Mimic"),
                "集票者" or "集票" or "寄票" or "机票" => GetString("Collector"),
                "活死人" or "活死" => GetString("Glitch"),
                "奪魂者" or "多混" or "夺魂" or "sc" => GetString("ImperiusCurse"),
                "自爆卡車" or "自爆" or "卡车" or "provo" => GetString("Provocateur"),
                "快槍手" or "快枪" or "qs" => GetString("QuickShooter"),
                "隱蔽者" or "隐蔽" or "小黑人" => GetString("Concealer"),
                "抹除者" or "抹除" => GetString("Eraser"),
                "肢解者" or "肢解" => GetString("OverKiller"),
                "劊子手" or "侩子手" or "柜子手" => GetString("Hangman"),
                "陽光開朗大男孩" or "阳光" or "开朗" or "大男孩" or "阳光开朗" or "开朗大男孩" or "阳光大男孩" or "sunny" => GetString("Sunnyboy"),
                "法官" or "审判" => GetString("Judge"),
                "入殮師" or "入检师" or "入殓" or "mor" => GetString("Mortician"),
                "通靈師" or "通灵" => GetString("Mediumshiper"),
                "吟游詩人" or "诗人" => GetString("Bard"),
                "隱匿者" or "隐匿" or "隐身" or "隐身人" or "印尼" => GetString("Swooper"),
                "船鬼" or "cp" => GetString("Crewpostor"),
                "嗜血騎士" or "血骑" or "骑士" or "bk" => GetString("BloodKnight"),
                "賭徒" => GetString("Totocalcio"),
                "分散机" => GetString("Disperser"),
                "和平之鸽" or "和平之鴿" or "和平的鸽子" or "和平" or "dop" or "dove of peace" => GetString("DovesOfNeace"),
                "持槍" or "持械" or "手长" => GetString("Reach"),
                "monarch" => GetString("Monarch"),
                "sch" => GetString("SchrodingersCat"),
                "glitch" => GetString("Glitch"),
                "безумный" or "mad" => GetString("Madmate"),
                "анти админер" or "anti adminer" => GetString("AntiAdminer"),
                _ => text
            };
        }

        public static bool GetRoleByName(string name, out CustomRoles role)
        {
            role = new();
            if (name == "") return false;

            if ((TranslationController.InstanceExists ? TranslationController.Instance.currentLanguage.languageID : SupportedLangs.SChinese) == SupportedLangs.SChinese)
            {
                Regex r = new("[\u4e00-\u9fa5]+$");
                MatchCollection mc = r.Matches(name);
                var result = string.Empty;

                for (var i = 0; i < mc.Count; i++)
                {
                    if (mc[i].ToString() == "是") continue;

                    result += mc[i]; //匹配结果是完整的数字，此处可以不做拼接的
                }

                name = FixRoleNameInput(result.Replace("是", string.Empty).Trim());
            }
            else
                name = name.Trim().ToLower();

            foreach (CustomRoles rl in Enum.GetValues<CustomRoles>())
            {
                if (rl.IsVanilla()) continue;

                string roleName = GetString(rl.ToString()).ToLower().Trim().Replace(" ", string.Empty);
                string nameWithoutId = Regex.Replace(name.Replace(" ", string.Empty), @"^\d+", string.Empty);

                if (nameWithoutId == roleName)
                {
                    role = rl;
                    return true;
                }
            }

            return false;
        }

        private static void SendRolesInfo(string role, byte playerId, bool isDev = false, bool isUp = false)
        {
            if (Options.CurrentGameMode != CustomGameMode.Standard)
            {
                Utils.SendMessage(GetString($"ModeDescribe.{Options.CurrentGameMode}"), playerId);
                if (Options.CurrentGameMode != CustomGameMode.HideAndSeek) return;
            }

            role = role.Trim().ToLower();
            if (role.StartsWith("/r")) _ = role.Replace("/r", string.Empty);

            if (role.StartsWith("/up")) _ = role.Replace("/up", string.Empty);

            if (role.EndsWith("\r\n")) _ = role.Replace("\r\n", string.Empty);

            if (role.EndsWith("\n")) _ = role.Replace("\n", string.Empty);

            if (role == "")
            {
                Utils.ShowActiveRoles(playerId);
                return;
            }

            role = FixRoleNameInput(role).ToLower().Trim().Replace(" ", string.Empty);

            foreach (CustomRoles rl in Enum.GetValues<CustomRoles>())
            {
                if (rl.IsVanilla()) continue;

                string roleName = GetString(rl.ToString());

                if (role == roleName.ToLower().Trim().TrimStart('*').Replace(" ", string.Empty))
                {
                    if ((isDev || isUp) && GameStates.IsLobby)
                    {
                        var devMark = "▲";
                        if (rl.IsAdditionRole() || rl is CustomRoles.GM) devMark = string.Empty;

                        if (rl.GetCount() < 1 || rl.GetMode() == 0) devMark = string.Empty;

                        if (isUp) Utils.SendMessage(devMark == "▲" ? string.Format(GetString("Message.YTPlanSelected"), roleName) : string.Format(GetString("Message.YTPlanSelectFailed"), roleName), playerId);

                        //if (devMark == "▲")
                        //{
                        //    byte pid = playerId == 255 ? (byte)0 : playerId;
                        //    _ = Main.DevRole.Remove(pid);
                        //    Main.DevRole.Add(pid, rl);
                        //}

                        if (isUp) return;
                    }

                    StringBuilder sb = new();
                    var title = $"<{Main.RoleColors[rl]}>{roleName}</color> {Utils.GetRoleMode(rl)}";
                    StringBuilder settings = new();
                    sb.Append(GetString($"{rl}InfoLong").TrimStart());
                    if (Options.CustomRoleSpawnChances.TryGetValue(rl, out StringOptionItem chance)) AddSettings(chance);

                    if (rl is CustomRoles.LovingCrewmate or CustomRoles.LovingImpostor && Options.CustomRoleSpawnChances.TryGetValue(CustomRoles.Lovers, out chance)) AddSettings(chance);

                    string txt = $"<size=90%>{sb}</size>".Replace(roleName, rl.ToColoredString()).Replace(roleName.ToLower(), rl.ToColoredString());
                    sb.Clear().Append(txt);

                    if (rl.PetActivatedAbility()) sb.Append($"<size=50%>{GetString("SupportsPetMessage")}</size>");

                    if (settings.Length > 0) Utils.SendMessage("\n", playerId, settings.ToString());

                    Utils.SendMessage(sb.ToString(), playerId, title);
                    return;

                    void AddSettings(StringOptionItem stringOptionItem)
                    {
                        settings.AppendLine($"<size=70%><u>{GetString("SettingsForRoleText")} <{Main.RoleColors[rl]}>{roleName}</color>:</u>");
                        Utils.ShowChildrenSettings(stringOptionItem, ref settings, disableColor: false);
                        settings.Append("</size>");
                    }
                }
            }

            foreach (CustomGameMode gameMode in Enum.GetValues<CustomGameMode>())
            {
                string gmString = GetString(gameMode.ToString());
                string match = gmString.ToLower().Trim().TrimStart('*').Replace(" ", string.Empty);

                if (role.Equals(match, StringComparison.OrdinalIgnoreCase))
                {
                    Utils.SendMessage(GetString($"ModeDescribe.{gameMode}"), playerId, gmString);
                    return;
                }
            }

            Utils.SendMessage(isUp ? GetString("Message.YTPlanCanNotFindRoleThePlayerEnter") : GetString("Message.CanNotFindRoleThePlayerEnter"), playerId);
        }

        // -------------------------------------------------------------------------------------------------------------------------

        public static void OnReceiveChat(PlayerControl player, string text, out bool canceled)
        {
            canceled = false;
            if (!AmongUsClient.Instance.AmHost || player.IsHost()) return;

            long now = Utils.TimeStamp;

            if (LastSentCommand.TryGetValue(player.PlayerId, out long ts) && ts + 2 >= now)
            {
                Logger.Warn("Chat message ignored, it was sent too soon after their last message", "ReceiveChat");
                return;
            }

            if (!CheckMute(player.PlayerId)) ChatManager.SendMessage(player, text);

            if (text.StartsWith("\n")) text = text[1..];

            string[] args = text.Split(' ');

            if (GuessManager.GuesserMsg(player, text) ||
                Judge.TrialMsg(player, text) ||
                NiceSwapper.SwapMsg(player, text) ||
                ParityCop.ParityCheckMsg(player, text) ||
                Councillor.MurderMsg(player, text))
            {
                canceled = true;
                LastSentCommand[player.PlayerId] = now;
                return;
            }

            if (Mediumshiper.MsMsg(player, text) || Mafia.MafiaMsgCheck(player, text))
            {
                LastSentCommand[player.PlayerId] = now;
                return;
            }

            var isCommand = false;

            if (text.StartsWith('/'))
            {
                foreach (Command command in AllCommands)
                {
                    if (!command.IsThisCommand(text)) continue;

                    Logger.Info($" Recognized command: {text}", "ReceiveChat");
                    isCommand = true;

                    if (!command.CanUseCommand(player))
                    {
                        Utils.SendMessage(GetString("Commands.NoAccess"), player.PlayerId);
                        canceled = true;
                        break;
                    }

                    command.Action(null, player, text, args);
                    if (command.IsCanceled) canceled = true;

                    break;
                }
            }

            if (CheckMute(player.PlayerId))
            {
                canceled = true;
                ChatManager.SendPreviousMessagesToAll();
                return;
            }

            if (GameStates.InGame && (Silencer.ForSilencer.Contains(player.PlayerId) || (Main.PlayerStates[player.PlayerId].Role is Dad { IsEnable: true } dad && dad.UsingAbilities.Contains(Dad.Ability.GoForMilk))) && player.IsAlive())
            {
                ChatManager.SendPreviousMessagesToAll();
                canceled = true;
                LastSentCommand[player.PlayerId] = now;
                return;
            }

            if (GameStates.IsInGame && !ChatUpdatePatch.LoversMessage && (player.IsAlive() || ExileController.Instance) && Lovers.PrivateChat.GetBool() && (ExileController.Instance || !GameStates.IsMeeting))
            {
                ChatManager.SendPreviousMessagesToAll(true);
                canceled = true;

                if (player.Is(CustomRoles.Lovers) || player.GetCustomRole() is CustomRoles.LovingCrewmate or CustomRoles.LovingImpostor)
                {
                    PlayerControl otherLover = Main.LoversPlayers.FirstOrDefault(x => x.PlayerId != player.PlayerId);

                    if (otherLover != null)
                    {
                        LateTask.New(() =>
                        {
                            string title = player.GetRealName();
                            ChatUpdatePatch.LoversMessage = true;
                            Utils.SendMessage(text, otherLover.PlayerId, title);
                            Utils.SendMessage(text, player.PlayerId, title);
                            LateTask.New(() => ChatUpdatePatch.LoversMessage = false, Math.Max(AmongUsClient.Instance.Ping / 1000f * 2f, Main.MessageWait.Value + 0.5f), log: false);
                        }, 0.2f, log: false);
                    }
                }
                else
                    LateTask.New(() => Utils.SendMessage(GetString("LoversChatCannotTalkMsg"), player.PlayerId, GetString("LoversChatCannotTalkTitle")), 0.5f, log: false);
            }

            if (isCommand) LastSentCommand[player.PlayerId] = now;

            SpamManager.CheckSpam(player, text);
        }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Update))]
    internal static class ChatUpdatePatch
    {
        public static bool DoBlockChat;
        public static bool LoversMessage;

        private static readonly List<(string Text, byte SendTo, string Title, long SendTimeStamp)> LastMessages = [];

        public static void Postfix(ChatController __instance)
        {
            var chatBubble = __instance.chatBubblePool.Prefab.Cast<ChatBubble>();
            chatBubble.TextArea.overrideColorTags = false;

            if (Main.DarkTheme.Value)
            {
                chatBubble.TextArea.color = Color.white;
                chatBubble.Background.color = new(0.1f, 0.1f, 0.1f, 1f);
            }

            LastMessages.RemoveAll(x => Utils.TimeStamp - x.SendTimeStamp > 10);

            if (!AmongUsClient.Instance.AmHost || Main.MessagesToSend.Count == 0 || (Main.MessagesToSend[0].ReceiverID == byte.MaxValue && Main.MessageWait.Value > __instance.timeSinceLastMessage) || DoBlockChat) return;

            PlayerControl player = Main.AllAlivePlayerControls.MinBy(x => x.PlayerId) ?? Main.AllPlayerControls.MinBy(x => x.PlayerId) ?? PlayerControl.LocalPlayer;
            if (player == null) return;

            (string msg, byte sendTo, string title) = Main.MessagesToSend[0];
            Main.MessagesToSend.RemoveAt(0);

            SendMessage(player, msg, sendTo, title);

            __instance.timeSinceLastMessage = 0f;

            LastMessages.Add((msg, sendTo, title, Utils.TimeStamp));
        }

        internal static void SendLastMessages()
        {
            PlayerControl player = Main.AllAlivePlayerControls.MinBy(x => x.PlayerId) ?? Main.AllPlayerControls.MinBy(x => x.PlayerId) ?? PlayerControl.LocalPlayer;
            if (player == null) return;

            foreach ((string msg, byte sendTo, string title, _) in LastMessages) SendMessage(player, msg, sendTo, title);
        }

        internal static void SendMessage(PlayerControl player, string msg, byte sendTo, string title)
        {
            int clientId = sendTo == byte.MaxValue ? -1 : Utils.GetPlayerById(sendTo).GetClientId();

            string name = player.Data.PlayerName;

            if (clientId == -1)
            {
                player.SetName(title);
                DestroyableSingleton<HudManager>.Instance.Chat.AddChat(player, msg);
                player.SetName(name);
            }

            var writer = CustomRpcSender.Create("MessagesToSend");
            writer.StartMessage(clientId);

            writer.StartRpc(player.NetId, (byte)RpcCalls.SetName)
                .Write(player.Data.NetId)
                .Write(title)
                .EndRpc();

            writer.StartRpc(player.NetId, (byte)RpcCalls.SendChat)
                .Write(msg)
                .EndRpc();

            writer.StartRpc(player.NetId, (byte)RpcCalls.SetName)
                .Write(player.Data.NetId)
                .Write(player.Data.PlayerName)
                .EndRpc();

            writer.EndMessage();
            writer.SendMessage();
        }
    }

    [HarmonyPatch(typeof(FreeChatInputField), nameof(FreeChatInputField.UpdateCharCount))]
    internal static class UpdateCharCountPatch
    {
        public static void Postfix(FreeChatInputField __instance)
        {
            int length = __instance.textArea.text.Length;
            __instance.charCountText.SetText(length <= 0 ? GetString("ThankYouForUsingEHR") : $"{length}/{__instance.textArea.characterLimit}");
            __instance.charCountText.enableWordWrapping = false;

            __instance.charCountText.color = length switch
            {
                < 1000 => Color.black,
                < 1200 => new(1f, 1f, 0f, 1f),
                _ => Color.red
            };
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSendChat))]
    internal static class RpcSendChatPatch
    {
        public static bool Prefix(PlayerControl __instance, string chatText, ref bool __result)
        {
            if (string.IsNullOrWhiteSpace(chatText))
            {
                __result = false;
                return false;
            }

            int return_count = PlayerControl.LocalPlayer.name.Count(x => x == '\n');
            chatText = new StringBuilder(chatText).Insert(0, "\n", return_count).ToString();
            if (AmongUsClient.Instance.AmClient && DestroyableSingleton<HudManager>.Instance) DestroyableSingleton<HudManager>.Instance.Chat.AddChat(__instance, chatText);

            if (chatText.Contains("who", StringComparison.OrdinalIgnoreCase)) DestroyableSingleton<UnityTelemetry>.Instance.SendWho();

            MessageWriter messageWriter = AmongUsClient.Instance.StartRpc(__instance.NetId, (byte)RpcCalls.SendChat, SendOption.None);
            messageWriter.Write(chatText);
            messageWriter.EndMessage();
            __result = true;
            return false;
        }
    }
}