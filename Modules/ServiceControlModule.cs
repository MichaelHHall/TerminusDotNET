﻿using Discord;
using Discord.Commands;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TerminusDotNetCore.Services;

namespace TerminusDotNetCore.Modules
{
    public class ServiceControlModule : ModuleBase<SocketCommandContext>
    {
        //shared config object - passed via DI
        public IConfiguration Config { get; set; }

        public ServiceControlModule(IConfiguration config)
        {
            Config = config;
        }


        //allow services to reply on a text channel
        public async Task ServiceReplyAsync(string s = null, Embed embed = null)
        {
            await ReplyAsync(message: s, embed: embed);
        }
    }
}
