﻿using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.NextPVR.Helpers;
using System.Threading.Tasks;
using System.Text.Json;
using MediaBrowser.Common.Json;

namespace Jellyfin.Plugin.NextPVR.Responses
{
    public class InstantiateResponse
    {
        private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.GetOptions();

        public async Task<ClientKeys> GetClientKeys(Stream stream, ILogger<LiveTvService> logger)
        {
            try
            {
                var root = await JsonSerializer.DeserializeAsync<ClientKeys>(stream, _jsonOptions).ConfigureAwait(false);

                if (root.sid != null && root.salt != null)
                {
                    UtilsHelper.DebugInformation(logger, string.Format("[NextPVR] ClientKeys: {0}", JsonSerializer.Serialize(root, _jsonOptions)));
                    return root;
                }
                logger.LogError("[NextPVR] Failed to validate the ClientKeys from NextPVR.");
                throw new Exception("Failed to load the ClientKeys from NextPVR.");
            }
            catch
            {
                logger.LogError("Check NextPVR Version 5");
                throw new UnauthorizedAccessException("Check NextPVR Version");
            }
        }

        public class ClientKeys
        {
            public string sid { get; set; }
            public string salt { get; set; }
        }
    }
}
