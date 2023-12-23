﻿using Hazel;
using System.Collections.Generic;
using System.Linq;
using static TOHE.Options;
using static TOHE.Translator;
using static TOHE.Utils;

namespace TOHE.Roles.AddOns.Crewmate
{
    public static class Stressed
    {
        private static readonly int Id = 14685;

        private static OptionItem StressedExtraTimeAfterTaskComplete;
        private static OptionItem StressedExtraTimeAfterMeeting;
        private static OptionItem StressedExtraTimeAfterImpostorDeath;
        private static OptionItem StressedExtraTimeAfterImpostorEjected;
        private static OptionItem StressedExtraTimeAfterFixingSabotage;
        private static OptionItem StressedExtraTimeAfterReporting;
        private static OptionItem StressedTimePenaltyAfterCrewmateEjected;
        private static OptionItem StressedStartingTime;

        private static int TimeAfterTaskComplete;
        private static int TimeAfterMeeting;
        private static int TimeAfterImpDead;
        private static int TimeAfterImpEject;
        private static int TimeAfterSaboFix;
        private static int TimeAfterReport;
        private static int TimeMinusAfterCrewEject;
        private static int StartingTime;

        private static Dictionary<byte, int> Timers = [];
        private static Dictionary<byte, long> LastUpdates = [];

        public static bool countRepairSabotage;

        private static bool IsEnable => Timers.Count > 0;

        public static void SetupCustomOption()
        {
            SetupAdtRoleOptions(Id, CustomRoles.Stressed, canSetNum: true);
            StressedExtraTimeAfterTaskComplete = IntegerOptionItem.Create(Id + 3, "StressedExtraTimeAfterTask", new(0, 60, 1), 30, TabGroup.Addons, false)
                .SetParent(CustomRoleSpawnChances[CustomRoles.Stressed])
                .SetValueFormat(OptionFormat.Seconds);
            StressedExtraTimeAfterMeeting = IntegerOptionItem.Create(Id + 4, "DamoclesExtraTimeAfterMeeting", new(0, 60, 1), 15, TabGroup.Addons, false)
                .SetParent(CustomRoleSpawnChances[CustomRoles.Stressed])
                .SetValueFormat(OptionFormat.Seconds);
            StressedExtraTimeAfterImpostorDeath = IntegerOptionItem.Create(Id + 5, "StressedExtraTimeAfterImpostorDeath", new(0, 60, 1), 10, TabGroup.Addons, false)
                .SetParent(CustomRoleSpawnChances[CustomRoles.Stressed])
                .SetValueFormat(OptionFormat.Seconds);
            StressedExtraTimeAfterImpostorEjected = IntegerOptionItem.Create(Id + 6, "StressedExtraTimeAfterImpostorEjected", new(0, 60, 1), 25, TabGroup.Addons, false)
                .SetParent(CustomRoleSpawnChances[CustomRoles.Stressed])
                .SetValueFormat(OptionFormat.Seconds);
            StressedExtraTimeAfterFixingSabotage = IntegerOptionItem.Create(Id + 7, "StressedExtraTimeAfterFixingSabotage", new(0, 60, 1), 20, TabGroup.Addons, false)
                .SetParent(CustomRoleSpawnChances[CustomRoles.Stressed])
                .SetValueFormat(OptionFormat.Seconds);
            StressedExtraTimeAfterReporting = IntegerOptionItem.Create(Id + 8, "StressedExtraTimeAfterReporting", new(0, 60, 1), 10, TabGroup.Addons, false)
                .SetParent(CustomRoleSpawnChances[CustomRoles.Stressed])
                .SetValueFormat(OptionFormat.Seconds);
            StressedTimePenaltyAfterCrewmateEjected = IntegerOptionItem.Create(Id + 9, "StressedTimePenaltyAfterCrewmateEjected", new(0, 60, 1), 15, TabGroup.Addons, false)
                .SetParent(CustomRoleSpawnChances[CustomRoles.Stressed])
                .SetValueFormat(OptionFormat.Seconds);
            StressedStartingTime = IntegerOptionItem.Create(Id + 10, "DamoclesStartingTime", new(0, 60, 1), 30, TabGroup.Addons, false)
                .SetParent(CustomRoleSpawnChances[CustomRoles.Stressed])
                .SetValueFormat(OptionFormat.Seconds);
        }

        public static void Init()
        {
            TimeAfterTaskComplete = StressedExtraTimeAfterTaskComplete.GetInt();
            TimeAfterMeeting = StressedExtraTimeAfterMeeting.GetInt();
            StartingTime = StressedStartingTime.GetInt();
            TimeAfterImpDead = StressedExtraTimeAfterImpostorDeath.GetInt();
            TimeAfterImpEject = StressedExtraTimeAfterImpostorEjected.GetInt();
            TimeAfterSaboFix = StressedExtraTimeAfterFixingSabotage.GetInt();
            TimeAfterReport = StressedExtraTimeAfterReporting.GetInt();
            TimeMinusAfterCrewEject = StressedTimePenaltyAfterCrewmateEjected.GetInt();

            Timers = [];
            LastUpdates = [];
            countRepairSabotage = true;
        }

        public static void Add()
        {
            _ = new LateTask(() =>
            {
                foreach (var pc in Main.AllAlivePlayerControls)
                {
                    if (pc.Is(CustomRoles.Stressed))
                    {
                        Timers.Add(pc.PlayerId, StartingTime);
                        LastUpdates.Add(pc.PlayerId, GetTimeStamp() + 1);
                    }
                }
            }, 8f, "Add Stressed Timers");
        }

        public static void Update(PlayerControl pc)
        {
            if (!LastUpdates.TryGetValue(pc.PlayerId, out var x) || x >= GetTimeStamp() || !Timers.ContainsKey(pc.PlayerId) || !IsEnable || !GameStates.IsInTask || !pc.IsAlive() || !pc.Is(CustomRoles.Stressed)) return;
            LastUpdates[pc.PlayerId] = GetTimeStamp();

            if (pc.GetPlayerTaskState().IsTaskFinished)
            {
                Main.PlayerStates[pc.PlayerId].RemoveSubRole(CustomRoles.Stressed);
                Timers.Remove(pc.PlayerId);
                LastUpdates.Remove(pc.PlayerId);
            }

            Timers[pc.PlayerId]--;

            if (Timers[pc.PlayerId] < 0)
            {
                Timers[pc.PlayerId] = 0;
                pc.Suicide();
            }

            if (pc.IsModClient() && pc.PlayerId != 0) SendRPC(pc.PlayerId, Timers[pc.PlayerId], LastUpdates[pc.PlayerId]);
            if (pc.PlayerId != 0) NotifyRoles(SpecifySeer: pc, SpecifyTarget: pc);
        }

        public static void SendRPC(byte id, int time, long lastUpdate)
        {
            if (!DoRPC || !IsEnable) return;
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SyncStressedTimer, SendOption.Reliable, -1);
            writer.Write(id);
            writer.Write(time);
            writer.Write(lastUpdate.ToString());
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void ReceiveRPC(MessageReader reader)
        {
            if (!IsEnable) return;
            byte id = reader.ReadByte();
            int time = reader.ReadInt32();
            long lastUpdate = long.Parse(reader.ReadString());
            Timers[id] = time;
            LastUpdates[id] = lastUpdate;
        }

        public static void OnTaskComplete(PlayerControl pc)
        {
            if (!IsEnable) return;
            Timers[pc.PlayerId] += TimeAfterTaskComplete;
        }

        public static void AfterMeetingTasks()
        {
            if (!IsEnable) return;
            AdjustTime(TimeAfterMeeting + 9);
            countRepairSabotage = true;
        }

        public static void OnNonCrewmateDead()
        {
            if (!IsEnable) return;
            AdjustTime(TimeAfterImpDead);
        }

        public static void OnNonCrewmateEjected()
        {
            if (!IsEnable) return;
            AdjustTime(TimeAfterImpEject);
        }

        public static void OnCrewmateEjected()
        {
            if (!IsEnable) return;
            AdjustTime(TimeMinusAfterCrewEject);
        }

        public static void OnRepairSabotage(PlayerControl pc)
        {
            if (!IsEnable) return;
            Timers[pc.PlayerId] += TimeAfterSaboFix;
        }

        public static void OnReport(PlayerControl pc)
        {
            if (!IsEnable) return;
            Timers[pc.PlayerId] += TimeAfterReport;
        }

        public static string GetProgressText(byte playerId) => Timers.TryGetValue(playerId, out var x) ? string.Format(GetString("DamoclesTimeLeft"), x) : string.Empty;

        private static void AdjustTime(int change)
        {
            if (!IsEnable) return;
            foreach (var x in Timers.Keys.ToArray())
            {
                Timers[x] += change;
            }
        }
    }
}