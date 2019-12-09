using Discord.Commands;
using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using TerminusDotNetCore.Services;
using System.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.FileExtensions;

namespace TerminusDotNetCore.Modules
{
    public class MangioneModule : ModuleBase<SocketCommandContext>, IServiceModule
    {
        private AudioService _service;

        public MangioneModule(AudioService service)
        {
            _service = service;
            _service.ParentModule = this;
        }

        public async Task ServiceReplyAsync(string s, EmbedBuilder embedBuilder = null)
        {
            if (embedBuilder == null)
            {
                await ReplyAsync(s);
            }
            else
            {
                await ReplyAsync(s, false, embedBuilder.Build());
            }
        }

        [Command("play", RunMode = RunMode.Async)]
        [Summary("Play a song of your choice in an audio channel of your choice (defaults to verbal shitposting)\nAvailable song aliases are: \"mangione1\", \"mangione2\", \"poloski\"")]
        public async Task PlaySong([Summary("name of song to play")]string song, [Summary("ID of channel to play in (defaults to verbal shitposting)")]string channelID = "-1")
        {
            if( Context != null && Context.Guild != null)
            {
                _service.setGuildClient(Context.Guild, Context.Client);
            }
            // TODO allow this function to accept mp3 attachments and play those
            //check if channel id is valid and exists
            ulong voiceID;
            IConfiguration config = new ConfigurationBuilder()
                                        .AddJsonFile("appsettings.json", true, true)
                                        .Build();
            if ( channelID.Equals("-1") )
            {
                voiceID = ulong.Parse(config["AudioChannelId"]);
            }
            else
            {
                try
                {
                    voiceID = ulong.Parse(channelID);
                }
                catch
                {
                    await ReplyAsync("Unable to parse channel ID, try letting it use the default");
                    return;
                }
            }
            if ( Context.Guild.GetVoiceChannel(voiceID) == null )
            {
                await ReplyAsync("Invalid channel ID, try letting it use the default");
                return;
            }
            //check if path is valid and exists
            // TODO check if file type can be played (mp3, wav, idk what ffmpeg can play)
            string path = "assets/";
            switch ( song )
            {
                case "mangione1":
                    path += "feels_so_good.mp3";
                    break;
                case "mangione2":
                    path += "pina_colada.mp3";
                    break;
                case "poloski":
                    path += "poloski.mp3";
                    break;
                default:
                    path += song;
                    break;
            }
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
            {
                await ReplyAsync("File does not exist.");
                return;
            }
            await _service.QueueSong(Context.Guild, path, voiceID, config["FfmpegCommand"]);
        }

        [Command("killmusic", RunMode = RunMode.Async)]
        [Summary("Flush song queue and leave voice channels")]
        public async Task KillMusic()
        {
            await _service.StopAllAudio(Context.Guild);
        }

    }
}