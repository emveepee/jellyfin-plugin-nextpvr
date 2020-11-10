﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Controller.LiveTv;
using System.Linq;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Model.Dto;
using System.Globalization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NextPVR
{
    public class RecordingsChannel : IChannel, IHasCacheKey, ISupportsDelete, ISupportsLatestMedia, ISupportsMediaProbe, IHasFolderAttributes, IDisposable
    {
        public ILiveTvManager _liveTvManager;
        public event EventHandler ContentChanged;
        private Timer _updateTimer;
        private DateTimeOffset _lastUpdate = DateTimeOffset.FromUnixTimeSeconds(0);
        private CancellationTokenSource _cancellationToken;
        private readonly ILogger<LiveTvService> _logger;

        IEnumerable<MyRecordingInfo> allRecordings = null;
        bool useCachedRecordings = false;
        public virtual void OnContentChanged()
        {
            if (ContentChanged != null)
            {
                ContentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        private async void OnUpdateTimerCallbackAsync(object state)
        {
            if (GetService().isActive)
            {
                DateTimeOffset backendUpate;
                backendUpate = await GetService().GetLastUpdate(_cancellationToken.Token);
                if (backendUpate > _lastUpdate)
                {
                    useCachedRecordings = false;
                    OnContentChanged();
                    _lastUpdate = backendUpate;
                }
                else if (backendUpate == DateTimeOffset.FromUnixTimeSeconds(0))
                {
                    _logger.LogDebug("Server offline");
                }
                else
                {
                    _logger.LogDebug("No change");
                }
            }
            else
            {
                _logger.LogDebug("Not active");
            }
        }

        public RecordingsChannel(ILiveTvManager liveTvManager, ILogger<LiveTvService> logger)
        {
            _logger = logger;
            _liveTvManager = liveTvManager;
            var interval = TimeSpan.FromSeconds(20);
            _updateTimer = new Timer(OnUpdateTimerCallbackAsync, null, interval, interval);
            if (_updateTimer != null)
            {
                _cancellationToken = new CancellationTokenSource();
            }
        }

        public void Dispose()
        {
            if (_updateTimer != null)
            {
                _updateTimer.Dispose();
                _logger.LogDebug("{0}", _cancellationToken.IsCancellationRequested);
                _cancellationToken.Cancel();
                _updateTimer = null;
            }
        }

        public string Name
        {
            get
            {
                return "NextPVR Recordings";
            }
        }

        public string Description
        {
            get
            {
                return "NextPVR Recordings";
            }
        }

        public string[] Attributes
        {
            get
            {
                return new []{ "Recordings" };
            }
        }

        public string DataVersion
        {
            get
            {
                return "1";
            }
        }

        public string HomePageUrl
        {
            get { return "http://www.nextpvr.com"; }
        }

        public ChannelParentalRating ParentalRating
        {
            get { return ChannelParentalRating.GeneralAudience; }
        }

        public string GetCacheKey(string userId)
        {
            var now = DateTime.UtcNow;

            var values = new List<string>();

            values.Add(now.DayOfYear.ToString(CultureInfo.InvariantCulture));
            values.Add(now.Hour.ToString(CultureInfo.InvariantCulture));

            double minute = now.Minute;
            minute /= 5;

            values.Add(Math.Floor(minute).ToString(CultureInfo.InvariantCulture));

            values.Add(GetService().LastRecordingChange.Ticks.ToString(CultureInfo.InvariantCulture));

            return string.Join("-", values.ToArray());
        }

        public InternalChannelFeatures GetChannelFeatures()
        {
            return new InternalChannelFeatures
            {
                ContentTypes = new List<ChannelMediaContentType>
                {
                    ChannelMediaContentType.Movie,
                    ChannelMediaContentType.Episode,
                    ChannelMediaContentType.Clip
                },
                MediaTypes = new List<ChannelMediaType>
                {
                    ChannelMediaType.Audio,
                    ChannelMediaType.Video
                },
                SupportsContentDownloading = true
            };
        }

        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        {
            if (type == ImageType.Primary)
            {
                return Task.FromResult(new DynamicImageResponse
                {
                    Path = "https://raw.githubusercontent.com/MediaBrowser/MediaBrowser.Resources/master/images/catalog/nextpvr.png",
                    Protocol = MediaProtocol.Http,
                    HasImage = true
                });
            }

            return Task.FromResult(new DynamicImageResponse
            {
                HasImage = false
            });
        }

        public IEnumerable<ImageType> GetSupportedChannelImages()
        {
            return new List<ImageType>
            {
                 ImageType.Primary
            };
        }

        public bool IsEnabledFor(string userId)
        {
            return true;
        }

        private LiveTvService GetService()
        {
            return _liveTvManager.Services.OfType<LiveTvService>().First();
        }

        public bool CanDelete(BaseItem item)
        {
            return !item.IsFolder;
        }

        public Task DeleteItem(string id, CancellationToken cancellationToken)
        {
            return GetService().DeleteRecordingAsync(id, cancellationToken);
        }

        public async Task<IEnumerable<ChannelItemInfo>> GetLatestMedia(ChannelLatestMediaSearch request, CancellationToken cancellationToken)
        {
            var result = await GetChannelItems(new InternalChannelItemQuery(), i => true, cancellationToken).ConfigureAwait(false);

            return result.Items.OrderByDescending(i => i.DateCreated ?? DateTime.MinValue);
        }

        public Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query.FolderId))
            {
                return GetRecordingGroups(query, cancellationToken);
            }

            if (query.FolderId.StartsWith("series_", StringComparison.OrdinalIgnoreCase))
            {
                var hash = query.FolderId.Split('_')[1];
                return GetChannelItems(query, i => i.IsSeries && string.Equals(i.Name.GetMD5().ToString("N"), hash, StringComparison.Ordinal), cancellationToken);
            }

            if (string.Equals(query.FolderId, "kids", StringComparison.OrdinalIgnoreCase))
            {
                return GetChannelItems(query, i => i.IsKids, cancellationToken);
            }

            if (string.Equals(query.FolderId, "movies", StringComparison.OrdinalIgnoreCase))
            {
                return GetChannelItems(query, i => i.IsMovie, cancellationToken);
            }

            if (string.Equals(query.FolderId, "news", StringComparison.OrdinalIgnoreCase))
            {
                return GetChannelItems(query, i => i.IsNews, cancellationToken);
            }

            if (string.Equals(query.FolderId, "sports", StringComparison.OrdinalIgnoreCase))
            {
                return GetChannelItems(query, i => i.IsSports, cancellationToken);
            }

            if (string.Equals(query.FolderId, "others", StringComparison.OrdinalIgnoreCase))
            {
                return GetChannelItems(query, i => !i.IsSports && !i.IsNews && !i.IsMovie && !i.IsKids && !i.IsSeries, cancellationToken);
            }

            var result = new ChannelItemResult()
            {
                Items = new List<ChannelItemInfo>()
            };

            return Task.FromResult(result);
        }

        public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, Func<MyRecordingInfo, bool> filter, CancellationToken cancellationToken)
        {
            var service = GetService();
            if (useCachedRecordings == false)
            {
                allRecordings = await service.GetAllRecordingsAsync(cancellationToken).ConfigureAwait(false);

                var channels = await service.GetChannelsAsync(cancellationToken).ConfigureAwait(false);

                useCachedRecordings = true;
            }
            else
            {
                _logger.LogDebug("using cached recordings");
            }

            var result = new ChannelItemResult()
            {
                Items = new List<ChannelItemInfo>()
            };

            result.Items.AddRange(allRecordings.Where(filter).Select(ConvertToChannelItem));

            return result;
        }

        private ChannelItemInfo ConvertToChannelItem(MyRecordingInfo item)
        {
            var path = string.IsNullOrEmpty(item.Path) ? item.Url : item.Path;

            var channelItem = new ChannelItemInfo
            {
                Name = string.IsNullOrEmpty(item.EpisodeTitle) ? item.Name : item.EpisodeTitle,
                SeriesName = !string.IsNullOrEmpty(item.EpisodeTitle) || item.IsSeries ? item.Name : null,
                OfficialRating = item.OfficialRating,
                CommunityRating = item.CommunityRating,
                ContentType = item.IsMovie ? ChannelMediaContentType.Movie : (item.IsSeries ? ChannelMediaContentType.Episode : ChannelMediaContentType.Clip),
                Genres = item.Genres,
                ImageUrl = item.ImageUrl,
                //HomePageUrl = item.HomePageUrl
                Id = item.Id,
                ParentIndexNumber = item.SeasonNumber,
                IndexNumber = item.EpisodeNumber,
                //IndexNumber = item.IndexNumber,
                MediaType = item.ChannelType == MediaBrowser.Model.LiveTv.ChannelType.TV ? ChannelMediaType.Video : ChannelMediaType.Audio,
                MediaSources = new List<MediaSourceInfo>
                {
                    new MediaSourceInfo
                    {
                        Path = path,
                        Protocol = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? MediaProtocol.Http : MediaProtocol.File,
                        Id = item.Id,
                        IsInfiniteStream = item.Status == RecordingStatus.InProgress ? true : false,
                        RunTimeTicks = (item.EndDate - item.StartDate).Ticks,
                    }
                },
                //ParentIndexNumber = item.ParentIndexNumber,
                PremiereDate = item.OriginalAirDate,
                ProductionYear = item.ProductionYear,
                //Studios = item.Studios,
                Type = ChannelItemType.Media,
                DateModified = item.DateLastUpdated,
                Overview = item.Overview,
                //People = item.People
                IsLiveStream = item.Status == MediaBrowser.Model.LiveTv.RecordingStatus.InProgress,
                Etag = item.Status.ToString()
            };

            return channelItem;
        }

        private async Task<ChannelItemResult> GetRecordingGroups(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var service = GetService();
            if (useCachedRecordings == false)
            {
                allRecordings = await service.GetAllRecordingsAsync(cancellationToken).ConfigureAwait(false);
                var channels = await service.GetChannelsAsync(cancellationToken).ConfigureAwait(false);
                useCachedRecordings = true;
            }
            else
            {
                _logger.LogDebug("using cached recordings@2");
            }

            var result = new ChannelItemResult()
            {
                Items = new List<ChannelItemInfo>()
            };

            var series = allRecordings
                .Where(i => i.IsSeries)
                .ToLookup(i => i.Name, StringComparer.OrdinalIgnoreCase);

            result.Items.AddRange(series.OrderBy(i => i.Key).Select(i => new ChannelItemInfo
            {
                Name = i.Key,
                FolderType = ChannelFolderType.Container,
                Id = "series_" + i.Key.GetMD5().ToString("N"),
                Type = ChannelItemType.Folder,
                DateCreated = i.Last().StartDate,
                ImageUrl = i.Last().ImageUrl.Replace("=poster", "=landscape")
            })); ; ; ; ;

            var kids = allRecordings.FirstOrDefault(i => i.IsKids);

            if (kids != null)
            {
                result.Items.Add(new ChannelItemInfo
                {
                    Name = "Kids",
                    FolderType = ChannelFolderType.Container,
                    Id = "kids",
                    Type = ChannelItemType.Folder,
                    ImageUrl = kids.ImageUrl
                });
            }

            var movies = allRecordings.FirstOrDefault(i => i.IsMovie);
            if (movies != null)
            {
                result.Items.Add(new ChannelItemInfo
                {
                    Name = "Movies",
                    FolderType = ChannelFolderType.Container,
                    Id = "movies",
                    Type = ChannelItemType.Folder,
                    ImageUrl = movies.ImageUrl
                });
            }

            var news = allRecordings.FirstOrDefault(i => i.IsNews);
            if (news != null)
            {
                result.Items.Add(new ChannelItemInfo
                {
                    Name = "News",
                    FolderType = ChannelFolderType.Container,
                    Id = "news",
                    Type = ChannelItemType.Folder,
                    ImageUrl = news.ImageUrl
                });
            }

            var sports = allRecordings.FirstOrDefault(i => i.IsSports);
            if (sports != null)
            {
                result.Items.Add(new ChannelItemInfo
                {
                    Name = "Sports",
                    FolderType = ChannelFolderType.Container,
                    Id = "sports",
                    Type = ChannelItemType.Folder,
                    ImageUrl = sports.ImageUrl
                });
            }

            var other = allRecordings.FirstOrDefault(i => !i.IsSports && !i.IsNews && !i.IsMovie && !i.IsKids && !i.IsSeries);
            if (other != null)
            {
                result.Items.Add(new ChannelItemInfo
                {
                    Name = "Others",
                    FolderType = ChannelFolderType.Container,
                    Id = "others",
                    Type = ChannelItemType.Folder,
                    ImageUrl = other.ImageUrl
                });
            }

            return result;
        }
    }

    public class MyRecordingInfo
    {
        /// <summary>
        /// Id of the recording.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the series timer identifier.
        /// </summary>
        /// <value>The series timer identifier.</value>
        public string SeriesTimerId { get; set; }

        /// <summary>
        /// Gets or sets the timer identifier.
        /// </summary>
        /// <value>The timer identifier.</value>
        public string TimerId { get; set; }

        /// <summary>
        /// ChannelId of the recording.
        /// </summary>
        public string ChannelId { get; set; }

        /// <summary>
        /// Gets or sets the type of the channel.
        /// </summary>
        /// <value>The type of the channel.</value>
        public ChannelType ChannelType { get; set; }

        /// <summary>
        /// Name of the recording.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the path.
        /// </summary>
        /// <value>The path.</value>
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets the URL.
        /// </summary>
        /// <value>The URL.</value>
        public string Url { get; set; }

        /// <summary>
        /// Gets or sets the overview.
        /// </summary>
        /// <value>The overview.</value>
        public string Overview { get; set; }

        /// <summary>
        /// The start date of the recording, in UTC.
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// The end date of the recording, in UTC.
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Gets or sets the program identifier.
        /// </summary>
        /// <value>The program identifier.</value>
        public string ProgramId { get; set; }

        /// <summary>
        /// Gets or sets the status.
        /// </summary>
        /// <value>The status.</value>
        public RecordingStatus Status { get; set; }

        /// <summary>
        /// Genre of the program.
        /// </summary>
        public List<string> Genres { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is repeat.
        /// </summary>
        /// <value><c>true</c> if this instance is repeat; otherwise, <c>false</c>.</value>
        public bool IsRepeat { get; set; }

        /// <summary>
        /// Gets or sets the episode title.
        /// </summary>
        /// <value>The episode title.</value>
        public string EpisodeTitle { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is hd.
        /// </summary>
        /// <value><c>true</c> if this instance is hd; otherwise, <c>false</c>.</value>
        public bool? IsHD { get; set; }

        /// <summary>
        /// Gets or sets the audio.
        /// </summary>
        /// <value>The audio.</value>
        public ProgramAudio? Audio { get; set; }

        /// <summary>
        /// Gets or sets the original air date.
        /// </summary>
        /// <value>The original air date.</value>
        public DateTime? OriginalAirDate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is movie.
        /// </summary>
        /// <value><c>true</c> if this instance is movie; otherwise, <c>false</c>.</value>
        public bool IsMovie { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is sports.
        /// </summary>
        /// <value><c>true</c> if this instance is sports; otherwise, <c>false</c>.</value>
        public bool IsSports { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is series.
        /// </summary>
        /// <value><c>true</c> if this instance is series; otherwise, <c>false</c>.</value>
        public bool IsSeries { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is live.
        /// </summary>
        /// <value><c>true</c> if this instance is live; otherwise, <c>false</c>.</value>
        public bool IsLive { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is news.
        /// </summary>
        /// <value><c>true</c> if this instance is news; otherwise, <c>false</c>.</value>
        public bool IsNews { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is kids.
        /// </summary>
        /// <value><c>true</c> if this instance is kids; otherwise, <c>false</c>.</value>
        public bool IsKids { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is premiere.
        /// </summary>
        /// <value><c>true</c> if this instance is premiere; otherwise, <c>false</c>.</value>
        public bool IsPremiere { get; set; }

        /// <summary>
        /// Gets or sets the official rating.
        /// </summary>
        /// <value>The official rating.</value>
        public string OfficialRating { get; set; }

        /// <summary>
        /// Gets or sets the community rating.
        /// </summary>
        /// <value>The community rating.</value>
        public float? CommunityRating { get; set; }

        /// <summary>
        /// Supply the image path if it can be accessed directly from the file system
        /// </summary>
        /// <value>The image path.</value>
        public string ImagePath { get; set; }

        /// <summary>
        /// Supply the image url if it can be downloaded
        /// </summary>
        /// <value>The image URL.</value>
        public string ImageUrl { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance has image.
        /// </summary>
        /// <value><c>null</c> if [has image] contains no value, <c>true</c> if [has image]; otherwise, <c>false</c>.</value>
        public bool? HasImage { get; set; }

        /// <summary>
        /// Gets or sets the show identifier.
        /// </summary>
        /// <value>The show identifier.</value>
        public string ShowId { get; set; }

        /// <summary>
        /// Gets or sets the date last updated.
        /// </summary>
        /// <value>The date last updated.</value>
        public DateTime DateLastUpdated { get; set; }
        /// <summary>
        /// Gets or sets the season number
        /// </summary>
        /// <value>The date last updated.</value>
        public int? SeasonNumber { get; set; }

        /// <summary>
        /// Gets or sets the episode number
        /// </summary>
        /// <value>The date last updated.</value>
        public int? EpisodeNumber { get; set; }
        /// <summary>
        /// Gets or sets the Year
        /// </summary>
        /// <value>The date last updated.</value>
        public int? ProductionYear { get; set; }

        public MyRecordingInfo()
        {
            Genres = new List<string>();
        }
    }
}
