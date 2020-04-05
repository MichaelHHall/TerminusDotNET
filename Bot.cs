﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using TerminusDotNetCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.FileExtensions;

namespace TerminusDotNetCore
{
    public class Bot
    {
        //for detecting regex matches in messages
        private RegexCommands _regexMsgParser;

        private DiscordSocketClient _client;

        //Discord.NET command management
        private CommandService _commandService;

        //command services
        private IServiceProvider _serviceProvider;

        public bool IsRegexActive { get; set; } = true;

        //ignored channels
        private List<ulong> _blacklistChannels = new List<ulong>();

        private IConfiguration _config = new ConfigurationBuilder()
                                        .AddJsonFile("appsettings.json", true, true)
                                        .AddJsonFile("secrets.json", true, true)
                                        .Build();

        public async Task Initialize()
        {
            _regexMsgParser = new RegexCommands();
            _commandService = new CommandService();

            //instantiate client and register log event handler
            _client = new DiscordSocketClient();
            _client.Log += Log;
            _client.MessageReceived += HandleCommandAsync;

            //verify that each required client secret is in the secrets file
            Dictionary<string, string> requiredSecrets = new Dictionary<string, string>()
            {
                {"DiscordToken", "Token to connect to your discord server"}
            };

            //verify that each required config entry is in the appsettings file
            Dictionary<string, string> requiredConfigs = new Dictionary<string, string>()
            {
                {"FfmpegCommand", "should be ffmpeg.exe for windows, ffmpeg for linux"},
                {"AudioChannelId", "ID of main audio channel to play audio in"},
                {"WeedChannelId", "ID of weed sesh audio channel"}
            };

            //alert in console for each missing config field
            foreach (var configEntry in requiredConfigs)
            {
                if (_config[configEntry.Key] == null)
                {
                    await Log(new LogMessage(LogSeverity.Warning, "appsettings.json", $"WARN: Missing item in appsettings config file :: {configEntry.Key} --- Description :: {configEntry.Value}"));
                }
            }

            //alert in console for each missing client secret field
            foreach (var secretEntry in requiredSecrets)
            {
                if (_config[secretEntry.Key] == null)
                {
                    await Log(new LogMessage(LogSeverity.Warning, "secrets.json", $"WARN: Missing item in secrets file :: {secretEntry.Key} --- Description :: {secretEntry.Value}"));
                }
            }

            //log in & start the client
            string token = _config["DiscordToken"];
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            //load blacklisted channels
            var blacklistSection = _config.GetSection("BlacklistChannels");
            foreach (var section in blacklistSection.GetChildren())
            {
                ulong id = ulong.Parse(section.Value);
                _blacklistChannels.Add(id);
            }

            //init custom services
            _serviceProvider = InstallServices();

            //init commands service
            await _commandService.AddModulesAsync(assembly: Assembly.GetEntryAssembly(), services: _serviceProvider);
            _commandService.CommandExecuted += OnCommandExecutedAsync;

            //hang out for now
            await Task.Delay(-1);
        }

        //log message to file
        public static Task Log(LogMessage message)
        {
            //Logger.WriteMessage(message.Source + ".txt", message.ToString());

            switch (message.Severity)
            {
                case LogSeverity.Critical:
                case LogSeverity.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case LogSeverity.Debug:
                case LogSeverity.Verbose:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;
                case LogSeverity.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case LogSeverity.Info:
                    Console.ForegroundColor = ConsoleColor.Green;
                    break;
                default:
                    break;
            }
            Console.WriteLine(message.ToString());
            Console.ResetColor();
            return Task.CompletedTask;
        }

        private async Task HandleCommandAsync(SocketMessage messageParam)
        {
            var message = messageParam as SocketUserMessage;

            //don't act in blacklisted channels
            if (message == null || _blacklistChannels.Contains(message.Channel.Id))
            {
                return;
            }

            //track position of command prefix char 
            int argPos = 0;

            //look for regex matches and reply if any are found
            if (IsRegexActive)
            {
                await HandleRegexResponses(message);
            }

            //check if message is not command or not sent by bot
            if (!(message.HasCharPrefix('!', ref argPos) || message.HasMentionPrefix(_client.CurrentUser, ref argPos))
            || message.Author.IsBot)
            {
                return;
            }

            //handle commands
            var context = new SocketCommandContext(_client, message);
            var commandResult = await _commandService.ExecuteAsync(context: context, argPos: argPos, services: _serviceProvider);
        }

        private async Task OnCommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
        {
            try
            {
                if (!result.IsSuccess && result is ExecuteResult execResult)
                {
                    //alert user and print error details to console
                    await context.Channel.SendMessageAsync(result.ErrorReason);
                    await Log(new LogMessage(LogSeverity.Error, "CommandExecution", $"Error in command '{command.Value.Name}': {execResult.ErrorReason}"));
                    await Log(new LogMessage(LogSeverity.Error, "CommandExecution", $"Exception details (see errors.txt): {execResult.Exception.StackTrace}"));

                    //dump exception details to error log
                    using (StreamWriter writer = new StreamWriter("errors.txt", true))
                    {
                        writer.WriteLine("----- BEGIN ENTRY -----");
                        writer.WriteLine($"ERROR DATETIME: {DateTime.Now.ToString()}");
                        writer.WriteLine($"COMMAND NAME  : {command.Value.Name}");
                        writer.WriteLine();
                        writer.WriteLine(execResult.Exception.ToString());
                        writer.WriteLine("----- END ENTRY   -----");
                        writer.WriteLine();
                    }
                }
                else
                {
                    //on successful command execution
                    await Log(new LogMessage(LogSeverity.Info, "CommandExecution", $"Command '{command.Value.Name}' executed successfully."));
                }
            }
            catch (InvalidOperationException)
            {
                await context.Channel.SendMessageAsync("Unknown command.");
                await Log(new LogMessage(LogSeverity.Error, "CommandExecution", $"Unknown command."));
            }
        }

        private IServiceProvider InstallServices()
        {
            var serviceCollection = new ServiceCollection();

            //new custom services (and objects passed via DI) get added here
            serviceCollection.AddSingleton(_config)
                             .AddSingleton<ImageService>()
                             .AddSingleton<TextEditService>()
                             .AddSingleton<TwitterService>()
                             .AddSingleton<AudioService>()
                             .AddSingleton<MarkovService>()
                             .AddSingleton<TicTacToeService>()
                             .AddSingleton(new Random())
                             .AddSingleton(this);

            //serviceCollection.AddSingleton<WideTextService>();

            return serviceCollection.BuildServiceProvider();
        }

        //check the given message for regex matches and send responses accordingly
        private async Task HandleRegexResponses(SocketUserMessage message)
        {
            //don't respond to bots (maybe change this to only ignore itself)
            int charPos = 0;
            if (message.HasCharPrefix('!', ref charPos))
            {
                return;
            }

            //look for wildcards in the current message 
            List<Tuple<string, string>> matches = _regexMsgParser.ParseMessage(message.Content);

            //respond for each matching regex
            if (matches.Count > 0 && !message.Author.IsBot)
            {
                foreach (var match in matches)
                {
                    await message.Channel.SendMessageAsync(match.Item1);
                    if (!string.IsNullOrEmpty(match.Item2))
                    {
                        //play the audio file specified
                        AudioService audioService = _serviceProvider.GetService(typeof(AudioService)) as AudioService;
                        _ = audioService.PlayRegexAudio((message.Channel as SocketGuildChannel).Guild, match.Item2);
                    }
                }
            }
        }
    }
}
