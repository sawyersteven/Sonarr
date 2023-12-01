using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.RSS
{
    public class TorrentRSS : TorrentClientBase<TorrentRSSSettings>
    {
        private object _FileLock = new object();

        public TorrentRSS(ITorrentFileInfoReader torrentFileInfoReader,
                          IHttpClient httpClient,
                          IConfigService configService,
                          IDiskProvider diskProvider,
                          IRemotePathMappingService remotePathMappingService,
                          ILocalizationService localizationService,
                          IBlocklistService blocklistService,
                          Logger logger)
        : base(torrentFileInfoReader, httpClient, configService, diskProvider, remotePathMappingService, localizationService, blocklistService, logger)
        {
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

                var children = channel.Descendants();
                while (children.Count() >= Settings.MaxItems)
                {
                    children.Last().Remove();
                }

                channel.Add(MakeElementFromRelease(remoteEpisode, hash, magnetLink));

                _diskProvider.WriteAllText(Settings.RSSFilePath, xDoc.ToString());

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
            XDocument xDoc = null;
            lock (_FileLock)
            {
                xDoc = ReadExistingXML();
            }

            var children = xDoc.Descendants("channel").FirstOrDefault()?.Descendants();

            foreach (var item in children)
            {
                item.TryGetAttributeValue("title", out var itemName);

                yield return new DownloadClientItem
                {
                    DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this),
                    DownloadId = Definition.Name + "_" + itemName,
                    Category = "sonarr",
                    Title = itemName,
                    CanMoveFiles = true,
                    CanBeRemoved = true
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
                OutputRootFolders = new List<OsPath>() { new OsPath(Settings.RSSDirectory) }
            };
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            // Write base xml to filepath
            try
            {
                _diskProvider.WriteAllText(Settings.RSSFilePath, MakeEmptyXML().ToString());
            }
            catch (Exception e)
            {
                var a = e;
                failures.Add(new NzbDroneValidationFailure("RSS Feed Output Directory", "Cannot write to RSS file")
                {
                    DetailedDescription = string.Format("The folder you specified does not exist or is inaccessible. Please verify the folder permissions for the user account '{0}', which is used to execute Sonarr.", Environment.UserName)
                });
            }
        }

        public static string FormatPubDate(DateTime now) => now.ToString("ddd',' d MMM yyyy HH':'mm':'ss") + " " + now.ToString("zzzz").Replace(":", "");

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

        private XDocument ReadExistingXML()
        {
            if (!File.Exists(Settings.RSSDirectory))
            {
                return MakeEmptyXML();
            }

            return XDocument.Load(Settings.RSSDirectory);
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
