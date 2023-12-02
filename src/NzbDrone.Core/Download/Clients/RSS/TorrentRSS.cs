using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.Blackhole;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.RSS
{
    public class TorrentRSS : TorrentClientBase<TorrentRSSSettings>
    {
        private readonly IScanWatchFolder _scanWatchFolder;
        public TimeSpan ScanGracePeriod { get; set; }
        private object _FileLock = new object();

        public TorrentRSS(IScanWatchFolder scanWatchFolder,
                          ITorrentFileInfoReader torrentFileInfoReader,
                          IHttpClient httpClient,
                          IConfigService configService,
                          IDiskProvider diskProvider,
                          IRemotePathMappingService remotePathMappingService,
                          ILocalizationService localizationService,
                          IBlocklistService blocklistService,
                          Logger logger)
        : base(torrentFileInfoReader, httpClient, configService, diskProvider, remotePathMappingService, localizationService, blocklistService, logger)
        {
            _scanWatchFolder = scanWatchFolder;
            ScanGracePeriod = TimeSpan.FromSeconds(30);
        }

        public override string Name => _localizationService.GetLocalizedString("TorrentRSS");

        protected override string AddFromMagnetLink(RemoteEpisode remoteEpisode, string hash, string magnetLink)
        {
            lock (_FileLock)
            {
                var xDoc = ReadExistingXML();
                var channel = xDoc.Descendants("channel").FirstOrDefault();
                if (channel == null)
                {
                    _logger.Error("RSS file {0} is corrupt or malformed", Settings.RSSDirectory);
                    return null;
                }

                var children = channel.Descendants("item");
                var a = children.Count();
                if (children.Any(x => x.Descendants("guid").FirstOrDefault()?.Value.Equals(hash) ?? false))
                {
                    _logger.Debug("Entry for hash {0} already exists in rss document", hash);
                    return null;
                }

                while (children.Count() >= Settings.MaxItems)
                {
                    children.First().Remove();
                }

                channel.Add(MakeElementFromRelease(remoteEpisode, hash, magnetLink));

                WriteXMLToFile(xDoc);

                _logger.Debug("Added XML item to RSS file {0}", Settings.RSSDirectory);
                return null;
            }
        }

        protected override string AddFromTorrentFile(RemoteEpisode remoteEpisode, string hash, string filename, byte[] fileContent)
        {
            var magnet = @"magnet:?xt=urn:btih:{hash}";
            return AddFromMagnetLink(remoteEpisode, hash, magnet);
        }

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            foreach (var item in _scanWatchFolder.GetItems(Settings.WatchFolder, ScanGracePeriod))
            {
                yield return new DownloadClientItem
                {
                    DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this),
                    DownloadId = Definition.Name + "_" + item.DownloadId,
                    Category = "sonarr",
                    Title = item.Title,

                    TotalSize = item.TotalSize,
                    RemainingTime = item.RemainingTime,

                    OutputPath = item.OutputPath,

                    Status = item.Status,

                    CanMoveFiles = !Settings.ReadOnly,
                    CanBeRemoved = !Settings.ReadOnly
                };
            }
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            if (!deleteData)
            {
                throw new NotSupportedException("RSS cannot remove DownloadItem without deleting the data as well, ignoring.");
            }

            DeleteItemData(item);
        }

        public override DownloadClientInfo GetStatus()
        {
            return new DownloadClientInfo
            {
                IsLocalhost = true,
                OutputRootFolders = new List<OsPath>() { new OsPath(Settings.WatchFolder) }
            };
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            // Write base xml to filepath
            try
            {
                WriteXMLToFile(MakeEmptyXML());
            }
            catch (Exception)
            {
                failures.Add(new NzbDroneValidationFailure("RSS Feed Output Directory", "Cannot write to RSS file")
                {
                    DetailedDescription = string.Format("The folder you specified does not exist or is inaccessible. Please verify the folder permissions for the user account '{0}', which is used to execute Sonarr.", Environment.UserName)
                });
            }
        }

        private static string FormatPubDate(DateTime now) => now.ToString("ddd',' d MMM yyyy HH':'mm':'ss") + " " + now.ToString("zzzz").Replace(":", "");

        private XElement MakeElementFromRelease(RemoteEpisode remoteEpisode, string hash, string magnetLink)
        {
            var elem = new XElement("item");
            elem.Add(new XElement("title", remoteEpisode.Release.Title));
            elem.Add(new XElement("link", magnetLink));
            elem.Add(new XElement("guid", hash, new XAttribute("isPermalink", "false")));
            elem.Add(new XElement("pubDate", FormatPubDate(DateTime.Now)));
            elem.Add(new XElement("description", remoteEpisode.Episodes.FirstOrDefault()?.Overview));
            elem.Add(new XElement("enclosure",
                                  new XAttribute("url", magnetLink),
                                  new XAttribute("length", "0"),
                                  new XAttribute("type", "application/x-bittorrent")));
            return elem;
        }

        private void WriteXMLToFile(XDocument xDoc)
        {
            using (var stream = _diskProvider.OpenWriteStream(Settings.RSSFilePath))
            {
                var data = new UTF8Encoding(true).GetBytes(xDoc.ToString());
                stream.Write(data, 0, data.Length);
            }
        }

        public XDocument ReadExistingXML()
        {
            if (!_diskProvider.FileExists(Settings.RSSFilePath))
            {
                return MakeEmptyXML();
            }

            using (var stream = _diskProvider.OpenReadStream(Settings.RSSFilePath))
            {
                return XDocument.Load(stream);
            }
        }

        private XDocument MakeEmptyXML()
        {
            return new XDocument(
                new XElement("rss",
                    new XElement("title", "Sonarr RSS Feed"),
                    new XElement("link", "https://sonarr.tv/"),
                    new XElement("description", "Sonarr RSS Feed"),
                    new XElement("channel")));
        }
    }
}
