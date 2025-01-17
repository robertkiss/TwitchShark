﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static Twitch;

public class NameRepository
{
    Twitch client;
    private string username;
    private HNotification connectionNotification;
    private CancellationTokenSource cts;
    private readonly static Dictionary<string, NameEntry> activeChattersWithColours = new Dictionary<string, NameEntry>();
    private HashSet<string> blacklist;
    private bool isTest = false;

    public void Stop()
    {
        try
        {
            Debug.Log("Stop requested");

            if (cts.Token.CanBeCanceled)
            {
                cts.Cancel();
            }
        }
        catch (ObjectDisposedException e)
        {
            if (TwitchSharkName.ExtraSettingsAPI_GetCheckboxState(TwitchSharkName.SETTINGS_DEBUG))
            {
                Debug.Log("Already cancelled, no need to recancel");
            }
        }
    }

    public async Task Start(String username, String token, String channel, bool isTest = false)
    {
        this.isTest = isTest;
        blacklist = new HashSet<string>(TwitchSharkName.ExtraSettingsAPI_GetDataNames(TwitchSharkName.SETTINGS_BLACKLIST));
        this.username = username;
        var msg = isTest ? "Testing Twitch Connection" : "Connecting to Twitch";
        connectionNotification = TwitchSharkName.LoadingNotification(msg);
        client = new Twitch(username, token);
        client.OnMessage += OnMessage;
        client.OnConnection += OnConnection;
        cts = new CancellationTokenSource();
        client.Start(cts);

        await client.JoinChannel(channel);
    }

    public void Reset()
    {
        activeChattersWithColours.Clear();
    }

    public static Dictionary<string, NameEntry> GetAllEntries()
    {
        return activeChattersWithColours;
    }

    private bool ShouldAddName(TwitchChatMessage message)
    {
        if (message.Sender.Username.ToLower() == username.ToLower()) return false;

        if (blacklist.Contains(message.Sender.Username.ToLower())) return false;

        var subOnly = TwitchSharkName.ExtraSettingsAPI_GetCheckboxState(TwitchSharkName.SETTINGS_SUB_ONLY);

        if (activeChattersWithColours.Keys.Contains(message.Sender.Username)) return false;

        if (subOnly)
        {
            if (!message.IsSub && !message.IsMod)
            {
                return false;
            }
        }

        return true;
    }

    private async void OnConnection(object sender, TwitchConnection connection)
    {
        connectionNotification.Close();

        if (connection.Success == true)
        {
            TwitchSharkName.SuccessNotification("Connected to Twitch");

            if (isTest)
            {
                Debug.Log("Test successful");
                Stop();
            }

            return;
        }

        TwitchSharkName.ErrorNotification("Could not connect to Twitch. Please check your settings.");
    }

    private async void OnMessage(object sender, TwitchChatMessage message)
    {
        var processedMessage = ProcessMessage(message);

        if (processedMessage.Type == CommandType.COMMAND)
        {
            if (!message.IsMod) return;

            if (processedMessage.Command == "noshark")
            {
                var firstArgument = processedMessage.Message.Split(' ')[0].ToLower();

                if (firstArgument.StartsWith("@"))
                {
                    firstArgument = firstArgument.Substring(1);
                }

                if (!blacklist.Contains(firstArgument))
                {
                    blacklist.Add(firstArgument);
                    var msg = $"{firstArgument} is now blacklisted";

                    Debug.Log(msg);
                    Notify(message.Channel, msg);

                    TwitchSharkName.ExtraSettingsAPI_SetDataValue(TwitchSharkName.SETTINGS_BLACKLIST, firstArgument, "");
                }
                else
                {
                    var msg = $"{firstArgument} is already blacklisted";
                    Debug.Log(msg);
                    Notify(message.Channel, msg);
                }
            }

            if (processedMessage.Command == "allowshark")
            {
                var firstArgument = processedMessage.Message.Split(' ')[0].ToLower();

                if (firstArgument.StartsWith("@"))
                {
                    firstArgument = firstArgument.Substring(1);
                }

                if (blacklist.Contains(firstArgument))
                {
                    blacklist.Remove(firstArgument);

                    Dictionary<string, string> persistedBlacklist = new Dictionary<string, string>();

                    foreach (var name in blacklist)
                    {
                        persistedBlacklist.Add(name, "");
                    }

                    TwitchSharkName.ExtraSettingsAPI_SetDataValues(TwitchSharkName.SETTINGS_BLACKLIST, persistedBlacklist);
                    var msg = $"{firstArgument} is now allowed to be a shark";
                    Debug.Log(msg);
                    Notify(message.Channel, msg);
                }
                else
                {
                    var msg = $"{firstArgument} is not blacklisted";
                    Debug.Log(msg);
                    Notify(message.Channel, msg);
                }
            }
        }

        if (processedMessage.Type == CommandType.REGULAR)
        {
            if (!ShouldAddName(message)) return;

            activeChattersWithColours.Add(message.Sender.Username, new NameEntry
            {
                Color = TwitchSharkName.GetColorFromHex(message.Sender.Color),
                Name = message.Sender.Username,
                EnteredOn = message.DateTime
            });

            var msg = $"{message.Sender.Username} just entered the Shark Name Pool";

            if (TwitchSharkName.ExtraSettingsAPI_GetCheckboxState(TwitchSharkName.SETTINGS_ANNOUNCE_TWITCH))
            {
                await client.SendMessage(message.Channel, $"@{msg}");
            }

            if (TwitchSharkName.ExtraSettingsAPI_GetCheckboxState(TwitchSharkName.SETTINGS_ANNOUNCE_GAME) && TwitchSharkName.InWorld())
            {
                RAPI.BroadcastChatMessage(msg);
            }
        }
    }

    public class SharkChatter
    {
        public string Username { get; set; }
        public Color Color { get; set; }
    }

    public NameEntry Next()
    {
        if (activeChattersWithColours.Count == 0)
        {
            return new NameEntry
            {
                Name = TwitchSharkName.ExtraSettingsAPI_GetInputValue(TwitchSharkName.SETTINGS_DEFAULT_SHARK_NAME),
                Color = TwitchSharkName.GetColorFromHex(TwitchSharkName.DEFAULT_COLOR),
                EnteredOn = DateTime.Now
            };
        }

        var random = new System.Random();
        var array = activeChattersWithColours.Keys.ToArray();
        var username = array[random.Next(array.Length)];
        var entry = activeChattersWithColours[username];

        if (HasEntryTimedOut(entry))
        {
            if (TwitchSharkName.ExtraSettingsAPI_GetCheckboxState(TwitchSharkName.SETTINGS_DEBUG))
            {
                Debug.Log($"{username}'s entry has timed out. Removing them from the list");
            }

            activeChattersWithColours.Remove(username);
            return Next();
        }

        Debug.Log($"Randomly chosen the name: {username}");
        activeChattersWithColours.Remove(username);
        return entry;
    }

    private bool HasEntryTimedOut(NameEntry entry)
    {
        var timeout = TwitchSharkName.ExtraSettingsAPI_GetComboboxSelectedItem(TwitchSharkName.SETTINGS_TIMEOUT).ToLower();

        if (timeout == "never") return false;

        switch (timeout)
        {
            case "5 minutes":
                return entry.EnteredOn.AddMinutes(5) < DateTime.Now;
            case "10 minutes":
                return entry.EnteredOn.AddMinutes(10) < DateTime.Now;
            case "15 minutes":
                return entry.EnteredOn.AddMinutes(15) < DateTime.Now;
            case "30 minutes":
                return entry.EnteredOn.AddMinutes(30) < DateTime.Now;
            case "1 hour":
                return entry.EnteredOn.AddHours(1) < DateTime.Now;
            case "2 hours":
                return entry.EnteredOn.AddHours(2) < DateTime.Now;
            case "4 hours":
                return entry.EnteredOn.AddHours(4) < DateTime.Now;
            default:
                return false;
        }
    }

    private ControlCommand ProcessMessage(TwitchChatMessage message)
    {
        var result = new ControlCommand
        {
            Original = message,
            Type = message.Message.StartsWith("!") ? CommandType.COMMAND : CommandType.REGULAR,
            Message = message.Message
        };

        if (result.Type == CommandType.COMMAND && message.Message.Length == 1)
        {
            //If the message is just a single !, disregard it as a command
            result.Type = CommandType.REGULAR;
            return result;
        }

        if (result.Type == CommandType.COMMAND)
        {
            result.Message = "";
            result.Command = message.Message.Substring(1).TrimEnd().ToLower();

            var delimPos = message.Message.IndexOf(" ");

            if (delimPos >= 0)
            {
                result.Command = message.Message.Substring(1, delimPos).TrimEnd().ToLower();
                result.Message = message.Message.Substring(delimPos).Trim();
            }
        }

        return result;
    }

    private enum CommandType
    {
        REGULAR,
        COMMAND
    }

    private class ControlCommand
    {
        public TwitchChatMessage Original { get; set; }
        public CommandType Type { get; set; }
        public String Message { get; set; }
        public String Command { get; set; }
    }

    private async void Notify(String channel, String msg)
    {
        if (TwitchSharkName.ExtraSettingsAPI_GetCheckboxState(TwitchSharkName.SETTINGS_ANNOUNCE_TWITCH))
        {
            await client.SendMessage(channel, msg);
        }

        if (TwitchSharkName.ExtraSettingsAPI_GetCheckboxState(TwitchSharkName.SETTINGS_ANNOUNCE_GAME) && TwitchSharkName.InWorld())
        {
            RAPI.BroadcastChatMessage(msg);
        }
    }

    public class NameEntry
    {
        public string Name { get; set; }
        public DateTime EnteredOn { get; set; }
        public Color Color { get; set; }
    }
}
