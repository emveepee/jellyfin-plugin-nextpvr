﻿using System;
using MediaBrowser.Model.Plugins;

namespace NextPvr.Configuration
{
    /// <summary>
    /// Class PluginConfiguration
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string WebServiceUrl { get; set; }

        public string Pin { get; set; }

        public Boolean EnableDebugLogging { get; set; }
        public Boolean NewEpisodes { get; set; }
        public bool? ShowRepeat { get; set; }
        public bool GetEpisodeImage { get; set; }
        public string RecordingDefault { get; set; }
        public int PrePaddingSeconds { get; set; }
        public int PostPaddingSeconds { get; set; }

        public PluginConfiguration()
        {
            Pin = "0000";
            WebServiceUrl = "http://localhost:8866";
            EnableDebugLogging = false;
        }
    }
}
