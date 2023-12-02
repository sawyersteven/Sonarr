using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.Blackhole;
using NzbDrone.Core.Download.Clients.RSS;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.RSS
{
    [TestFixture]
    public class TorrentRSSFixture : DownloadClientFixtureBase<TorrentRSS>
    {
        protected string _completedDownloadFolder;
        protected string _rssFolder;
        protected string _rssFile;
        protected string _watchFolder;
        protected string _magnetFilePath;
        protected DownloadClientItem _downloadClientItem;

        [SetUp]
        public void Setup()
        {
            _completedDownloadFolder = @"c:\rss\completed".AsOsAgnostic();
            _rssFolder = @"c:\rss".AsOsAgnostic();

            _downloadClientItem = Builder<DownloadClientItem>
                                  .CreateNew()
                                  .With(d => d.DownloadId = "_Droned.S01E01.Pilot.1080p.WEB-DL-DRONE_0")
                                  .With(d => d.OutputPath = new OsPath(Path.Combine(_completedDownloadFolder, _title)))
                                  .Build();

            Mocker.SetConstant<IScanWatchFolder>(Mocker.Resolve<ScanWatchFolder>());

            Subject.Definition = new DownloadClientDefinition();
            Subject.Definition.Settings = new TorrentRSSSettings
            {
                RSSDirectory = _rssFolder,
                WatchFolder = _completedDownloadFolder,
                MaxItems = 10
            };

            _rssFile = Subject.Definition.Settings.As<TorrentRSSSettings>().RSSFilePath.AsOsAgnostic();

            var persistentRSSTempFile = GetTempFilePath(); // prevents opening a new temp file with every filesteam to the rss file

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.OpenWriteStream(It.IsAny<string>()))
                .Returns(() => new FileStream(GetTempFilePath(), FileMode.Create));

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.OpenWriteStream(It.Is<string>(x => x == _rssFile)))
                .Returns(() => new FileStream(persistentRSSTempFile, FileMode.OpenOrCreate));

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.Is<string>(x => x == _rssFile)))
                .Returns(() => File.Exists(persistentRSSTempFile));

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.OpenReadStream(It.Is<string>(x => x == _rssFile)))
                .Returns(() => new FileStream(persistentRSSTempFile, FileMode.Open));

            Mocker.GetMock<ITorrentFileInfoReader>()
                .Setup(c => c.GetHashFromTorrentFile(It.IsAny<byte[]>()))
                .Returns("myhash");

            Mocker.GetMock<IDiskScanService>().Setup(c => c.FilterPaths(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                  .Returns<string, IEnumerable<string>, bool>((b, s, c) => s.ToList());
        }

        protected void GivenFailedDownload()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(s => s.GetAsync(It.IsAny<HttpRequest>()))
                .Throws(new WebException());
        }

        protected void GivenCompletedItem()
        {
            var targetDir = Path.Combine(_completedDownloadFolder, _title);

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.GetDirectories(_completedDownloadFolder))
                .Returns(new[] { targetDir });

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.GetFiles(targetDir, true))
                .Returns(new[] { Path.Combine(targetDir, "somefile.mkv") });

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.GetFileSize(It.IsAny<string>()))
                .Returns(1000000);
        }

        protected override RemoteEpisode CreateRemoteEpisode()
        {
            var remoteEpisode = base.CreateRemoteEpisode();
            var torrentInfo = new TorrentInfo();

            torrentInfo.Title = remoteEpisode.Release.Title;
            torrentInfo.DownloadUrl = remoteEpisode.Release.DownloadUrl;
            torrentInfo.DownloadProtocol = remoteEpisode.Release.DownloadProtocol;
            torrentInfo.MagnetUrl = "magnet:?xt=urn:btih:755248817d32b00cc853e633ecdc48e4c21bff15&dn=Series.S05E10.PROPER.HDTV.x264-DEFiNE%5Brartv%5D&tr=http%3A%2F%2Ftracker.trackerfix.com%3A80%2Fannounce&tr=udp%3A%2F%2F9.rarbg.me%3A2710&tr=udp%3A%2F%2F9.rarbg.to%3A2710";

            remoteEpisode.Release = torrentInfo;

            return remoteEpisode;
        }

        [Test]
        public void completed_download_should_have_required_properties()
        {
            Subject.ScanGracePeriod = TimeSpan.Zero;

            GivenCompletedItem();

            var result = Subject.GetItems().Single();

            VerifyCompleted(result);

            result.CanBeRemoved.Should().BeFalse();
            result.CanMoveFiles.Should().BeFalse();
        }

        [Test]
        public void partial_download_should_have_required_properties()
        {
            GivenCompletedItem();

            var result = Subject.GetItems().Single();

            VerifyPostprocessing(result);
        }

        [Test]
        public async Task newest_item_should_be_last()
        {
            var magneturl = "magnet:?xt=urn:btih:{0}&dn=Series.S05E10.PROPER.HDTV.x264-DEFiNE%5Brartv%5D&tr=http%3A%2F%2Ftracker.trackerfix.com%3A80%2Fannounce&tr=udp%3A%2F%2F9.rarbg.me%3A2710&tr=udp%3A%2F%2F9.rarbg.to%3A2710";

            for (var i = 0; i < 5; i++)
            {
                var url = string.Format(magneturl, HashConverter.GetHash(i.ToString()).ToHexString());
                var remoteEpisode = CreateRemoteEpisode();

                remoteEpisode.Release.As<TorrentInfo>().MagnetUrl = url;
                remoteEpisode.Release.DownloadUrl = url;
                remoteEpisode.Release.Title = i.ToString();
                await Subject.Download(remoteEpisode, CreateIndexer());
            }

            var xDoc = Subject.As<TorrentRSS>().ReadExistingXML();

            var title = 0;
            foreach (var item in xDoc.Descendants("item"))
            {
                item.Descendants("title").First().Value.Should().Be(title.ToString());
                title++;
            }
        }

        [Test]
        public async Task oldest_item_should_be_removed_when_over_limit()
        {
            Subject.Definition.Settings.As<TorrentRSSSettings>().MaxItems = 5;

            var magneturl = "magnet:?xt=urn:btih:{0}&dn=Series.S05E10.PROPER.HDTV.x264-DEFiNE%5Brartv%5D&tr=http%3A%2F%2Ftracker.trackerfix.com%3A80%2Fannounce&tr=udp%3A%2F%2F9.rarbg.me%3A2710&tr=udp%3A%2F%2F9.rarbg.to%3A2710";

            for (var i = 0; i < 6; i++)
            {
                var url = string.Format(magneturl, HashConverter.GetHash(i.ToString()).ToHexString());
                var remoteEpisode = CreateRemoteEpisode();

                remoteEpisode.Release.As<TorrentInfo>().MagnetUrl = url;
                remoteEpisode.Release.DownloadUrl = url;
                remoteEpisode.Release.Title = i.ToString();
                await Subject.Download(remoteEpisode, CreateIndexer());
            }

            var xDoc = Subject.As<TorrentRSS>().ReadExistingXML();
            xDoc.Descendants("title").Any(x => x.Value == 0.ToString()).Should().Be(false);
        }

        [Test]
        public async Task Do_not_exceed_max_items()
        {
            Subject.Definition.Settings.As<TorrentRSSSettings>().MaxItems = 5;

            var magneturl = "magnet:?xt=urn:btih:{0}&dn=Series.S05E10.PROPER.HDTV.x264-DEFiNE%5Brartv%5D&tr=http%3A%2F%2Ftracker.trackerfix.com%3A80%2Fannounce&tr=udp%3A%2F%2F9.rarbg.me%3A2710&tr=udp%3A%2F%2F9.rarbg.to%3A2710";

            for (var i = 0; i < 10; i++)
            {
                var url = string.Format(magneturl, HashConverter.GetHash(i.ToString()).ToHexString());
                var remoteEpisode = CreateRemoteEpisode();

                remoteEpisode.Release.As<TorrentInfo>().MagnetUrl = url;
                remoteEpisode.Release.DownloadUrl = url;
                await Subject.Download(remoteEpisode, CreateIndexer());
            }

            var xDoc = Subject.As<TorrentRSS>().ReadExistingXML();
            var childCount = xDoc.Descendants("item").Count();
            childCount.Should().Be(5);
        }

        [Test]
        public async Task Do_not_download_duplicate_releases()
        {
            var remoteEpisode = CreateRemoteEpisode();

            await Subject.Download(remoteEpisode, CreateIndexer());
            await Subject.Download(remoteEpisode, CreateIndexer());
            Mocker.GetMock<IDiskProvider>().Verify(c => c.OpenWriteStream(_rssFile), Times.Once());
        }

        [Test]
        public async Task Download_should_download_file_if_it_doesnt_exist()
        {
            var remoteEpisode = CreateRemoteEpisode();

            await Subject.Download(remoteEpisode, CreateIndexer());

            Mocker.GetMock<IHttpClient>().Verify(c => c.GetAsync(It.Is<HttpRequest>(v => v.Url.FullUri == _downloadUrl)), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(c => c.OpenWriteStream(_rssFile), Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void RemoveItem_should_delete_file()
        {
            GivenCompletedItem();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(true);

            Subject.RemoveItem(_downloadClientItem, true);

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFile(It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void RemoveItem_should_delete_directory()
        {
            GivenCompletedItem();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FolderExists(It.IsAny<string>()))
                .Returns(true);

            Subject.RemoveItem(_downloadClientItem, true);

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFolder(It.IsAny<string>(), true), Times.Once());
        }

        [Test]
        public void RemoveItem_should_ignore_if_unknown_item()
        {
            Subject.RemoveItem(_downloadClientItem, true);

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFile(It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFolder(It.IsAny<string>(), true), Times.Never());
        }

        [Test]
        public void RemoveItem_should_throw_if_deleteData_is_false()
        {
            GivenCompletedItem();

            Assert.Throws<NotSupportedException>(() => Subject.RemoveItem(_downloadClientItem, false));

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFile(It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFolder(It.IsAny<string>(), true), Times.Never());
        }

        [Test]
        public void should_return_status_with_outputdirs()
        {
            var result = Subject.GetStatus();

            result.IsLocalhost.Should().BeTrue();
            result.OutputRootFolders.Should().NotBeNull();
            result.OutputRootFolders.First().Should().Be(_completedDownloadFolder);
        }

        [Test]
        public async Task should_return_null_hash()
        {
            var remoteEpisode = CreateRemoteEpisode();

            var result = await Subject.Download(remoteEpisode, CreateIndexer());

            result.Should().BeNull();
        }
    }
}
